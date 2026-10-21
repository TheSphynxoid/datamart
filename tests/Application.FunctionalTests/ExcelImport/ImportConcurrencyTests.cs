using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.Application.FunctionalTests.Infrastructure;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Shouldly;
using Xunit;

namespace DataMart.Application.FunctionalTests.ExcelImport;

/// <summary>
/// Functional coverage of the per-table import scheduling (TableTaskScheduler +
/// ImportProcessingService): imports to different tables may overlap and each
/// lands complete (rows + journal), while imports to the same table run strictly
/// in enqueue order with disjoint ID ranges. The scheduler's fine-grained
/// concurrency contract (overlap, cap, waiters-hold-no-slot) is proven by
/// TableTaskSchedulerTests; here we prove the real stack behaves.
/// </summary>
[Collection("FunctionalTests")]
public class ImportConcurrencyTests : IAsyncLifetime
{
    private const string AccountEntity = "ClientAccounts";
    private const string TransactionEntity = "CustomerTransactions";

    private int _accountMappingId;
    private int _transactionMappingId;

    public async Task InitializeAsync()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _accountMappingId = await CreateAccountMappingAsync();
        _transactionMappingId = await CreateTransactionMappingAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DifferentTables_BothImportsSucceed_WithCompleteJournals()
    {
        // Enqueue both; the per-table scheduler runs them concurrently (2 slots by
        // default). Each import holds its own COPY transaction and journal writer —
        // the assertion is that neither interferes with the other.
        var accountStart = await SendAccountImportAsync(_accountMappingId, ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok), EmptyFieldMode.SkipRow);
        var transactionStart = await SendTransactionImportAsync(_transactionMappingId, CustomerTransactionFixtures.CreateXlsx(rowCount: 25));

        await BatchTerminalWaiter.WaitForBatchTerminalAsync(accountStart.BatchId);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(transactionStart.BatchId);

        var accountBatch = await TestApp.FindAsync<ImportBatch>(accountStart.BatchId);
        var transactionBatch = await TestApp.FindAsync<ImportBatch>(transactionStart.BatchId);

        accountBatch!.Status.ShouldBe(ImportStatus.Success);
        accountBatch.JournalIncomplete.ShouldBeFalse();
        accountBatch.JournalRowsWritten.ShouldBe(5);

        transactionBatch!.Status.ShouldBe(ImportStatus.Success);
        transactionBatch.JournalIncomplete.ShouldBeFalse();
        transactionBatch.JournalRowsWritten.ShouldBe(25);

        // Journals are per-batch: no cross-contamination of row records.
        var accountRecords = await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == accountStart.BatchId));
        var transactionRecords = await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == transactionStart.BatchId));
        accountRecords.Count.ShouldBe(5);
        transactionRecords.Count.ShouldBe(25);
        accountRecords.All(r => r.Status == ImportRowStatus.Imported).ShouldBeTrue();
        transactionRecords.All(r => r.Status == ImportRowStatus.Imported).ShouldBeTrue();

        // Each batch's ID range brackets only its own table's rows.
        var accountIds = (await TestApp.ListAsync<ClientAccount>()).Select(c => c.Id).OrderBy(id => id).ToList();
        var transactionIds = (await TestApp.ListAsync<CustomerTransaction>()).Select(t => t.Id).OrderBy(id => id).ToList();
        accountIds.Count.ShouldBe(5);
        transactionIds.Count.ShouldBe(25);
        accountBatch.MinImpactedId.ShouldBe(accountIds.First());
        accountBatch.MaxImpactedId.ShouldBe(accountIds.Last());
        transactionBatch.MinImpactedId.ShouldBe(transactionIds.First());
        transactionBatch.MaxImpactedId.ShouldBe(transactionIds.Last());
    }

    [Fact]
    public async Task SameTable_SecondImportStartsOnlyAfterFirstCompletes()
    {
        // The first import is large enough that its row pass takes real time: if
        // the scheduler failed to serialize same-table work, batch 2 would start
        // (StartedAt) while batch 1 was still Processing (CompletedAt later).
        var largeStart = await SendAccountImportAsync(_accountMappingId, ClientAccountFixtures.CreateLargeXlsx(rowCount: 3000), EmptyFieldMode.SkipRow);
        var smallStart = await SendAccountImportAsync(_accountMappingId, ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok), EmptyFieldMode.SkipRow);

        await BatchTerminalWaiter.WaitForBatchTerminalAsync(largeStart.BatchId, timeoutSeconds: 120);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(smallStart.BatchId, timeoutSeconds: 120);

        var largeBatch = await TestApp.FindAsync<ImportBatch>(largeStart.BatchId);
        var smallBatch = await TestApp.FindAsync<ImportBatch>(smallStart.BatchId);

        largeBatch!.Status.ShouldBe(ImportStatus.Success);
        smallBatch!.Status.ShouldBe(ImportStatus.Success);

        // FIFO: the second executor's start must not precede the first's completion.
        smallBatch.StartedAt.ShouldNotBeNull();
        largeBatch.CompletedAt.ShouldNotBeNull();
        (smallBatch.StartedAt!.Value >= largeBatch.CompletedAt!.Value)
            .ShouldBeTrue("the second same-table import started before the first one completed");

        // Disjoint, ordered ID ranges — and no gaps between batches (the second
        // COPY starts right after the first commits).
        (largeBatch.MaxImpactedId!.Value < smallBatch.MinImpactedId!.Value)
            .ShouldBeTrue("same-table imports must not interleave their ID ranges");

        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(3005);
        (await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == largeStart.BatchId))).Count.ShouldBe(3000);
        (await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == smallStart.BatchId))).Count.ShouldBe(5);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<int> CreateAccountMappingAsync()
    {
        var columns = new List<ColumnMappingDto>
        {
            new("AccountNumber", null, "AccountNumber", null, null, null, false, null, null, IsRequired: true),
            new("ClientName",    null, "ClientName",    null, null, null, false, null, null, IsRequired: true),
            new("Balance",       null, "Balance",       null, null, ".",  false, null, null, IsRequired: false),
            new("OpenedDate",    null, "OpenedDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: false),
        };
        return await TestApp.SendAsync(new CreateMappingCommand("ClientAccount_Concurrency", null, AccountEntity, columns));
    }

    private static async Task<int> CreateTransactionMappingAsync()
    {
        var columns = new List<ColumnMappingDto>
        {
            new("TransactionID",      null, "TransactionID",      null, null, null, false, null, null, IsRequired: true),
            new("CustomerID",         null, "CustomerID",         null, null, null, false, null, null, IsRequired: true),
            new("CustomerDOB",        null, "CustomerDOB",        "yyyy-MM-dd", null, null, false, null, null, IsRequired: true),
            new("CustGender",         null, "CustGender",         null, null, null, false, null, null, IsRequired: false),
            new("CustLocation",       null, "CustLocation",       null, null, null, false, null, null, IsRequired: false),
            new("CustAccountBalance", null, "CustAccountBalance", null, null, ".",  false, null, null, IsRequired: true),
            new("TransactionDate",    null, "TransactionDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: true),
            new("TransactionTime",    null, "TransactionTime",    null, null, null, false, null, null, IsRequired: false),
            new("TransactionAmount",  null, "TransactionAmount",  null, null, ".",  false, null, null, IsRequired: true),
        };
        return await TestApp.SendAsync(new CreateMappingCommand("CustomerTransaction_Concurrency", null, TransactionEntity, columns));
    }

    private static async Task<ImportStarted> SendAccountImportAsync(int mappingId, byte[] xlsx, EmptyFieldMode emptyMode)
    {
        using var stream = new MemoryStream(xlsx);
        return await TestApp.SendAsync(new ImportFileCommand(stream, "clients_concurrency.xlsx", mappingId, emptyMode, SheetIndex: null));
    }

    private static async Task<ImportStarted> SendTransactionImportAsync(int mappingId, byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        return await TestApp.SendAsync(new ImportFileCommand(stream, "transactions_concurrency.xlsx", mappingId, EmptyFieldMode.SkipRow, SheetIndex: null));
    }
}
