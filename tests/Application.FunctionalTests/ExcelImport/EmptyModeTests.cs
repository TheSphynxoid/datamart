using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.Application.FunctionalTests.Infrastructure;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Commands.RunValidation;
using DataMart.Application.ImportFile.Queries.GetImportReport;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Shouldly;
using Xunit;

namespace DataMart.Application.FunctionalTests.ExcelImport;

/// <summary>
/// The two EmptyFieldMode variants without a dedicated scenario yet (SkipRow is
/// the tested default in ExcelImportScenariosTests):
///   - Throw: an empty REQUIRED field aborts the whole import. Nothing is
///     written anywhere (no rows, no journal) and the batch lands Failed with
///     the offending field named.
///   - Default: type defaults ("" for text, 0 for decimals, ...) are
///     substituted and every row imports, including the ones that were empty.
/// The empty-cell fixtures are the existing MissingAccountNumber set (7 rows,
/// 3 of them with an empty required AccountNumber).
/// </summary>
[Collection("FunctionalTests")]
public class EmptyModeTests : IAsyncLifetime
{
    private const string TargetEntity = "ClientAccounts";

    private int _mappingId;

    public async Task InitializeAsync()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _mappingId = await CreateMappingAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ThrowMode_AbortsTheWholeImport_WithZeroSideEffects()
    {
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.MissingAccountNumber, EmptyFieldMode.Throw);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Failed);
        batch.FailedReason.ShouldNotBeNull();
        batch.FailedReason.ShouldContain("AccountNumber");
        batch.Phase.ShouldBeNull();
        batch.CompletedAt.ShouldNotBeNull();

        // Nothing anywhere: no rows in the target table, no journal rows at all.
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(0);
        (await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == started.BatchId))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task DefaultMode_SubstitutesTypeDefaults_ImportsEverything()
    {
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.MissingAccountNumber, EmptyFieldMode.Default);
        await BatchTerminalWaiter.WaitForJournalCompleteAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Success);
        batch.JournalIncomplete.ShouldBeFalse();
        batch.JournalRowsWritten.ShouldBe(7);

        // All 7 rows imported, none rejected.
        var records = await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == started.BatchId));
        records.Count.ShouldBe(7);
        records.All(r => r.Status == ImportRowStatus.Imported).ShouldBeTrue();

        // The text default for the substituted rows is the empty string; the
        // non-empty rows keep their real values.
        var accounts = await TestApp.ListAsync<ClientAccount>();
        accounts.Count.ShouldBe(7);
        var rowsWithNumber = accounts.Where(a => !string.IsNullOrEmpty(a.AccountNumber)).ToList();
        var rowsWithoutNumber = accounts.Where(a => a.AccountNumber == string.Empty).ToList();
        rowsWithNumber.Count.ShouldBe(4);
        rowsWithoutNumber.Count.ShouldBe(3);

        // Balance is mapped with decimalSeparator "." and has no empties in this
        // fixture; the date columns carry real dates. The one column with actual
        // substituted defaults is AccountNumber (checked above).
        accounts.All(a => a.Balance > 0).ShouldBeTrue();
    }

    [Fact]
    public async Task ThrowMode_DryRunListsAllEmptyRows_ImportAbortsOnFirst()
    {
        // Documented divergence between the two paths (ImportFileValidator): the
        // dry-run does NOT abort under Throw — it keeps scanning so the user gets
        // a COMPLETE error list. The real import aborts on the first empty
        // required field and writes nothing. Same file, same mapping, same mode.
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.MissingAccountNumber);

        ImportReportDto report;
        using (var stream = new MemoryStream(bytes))
        {
            report = await TestApp.SendAsync(new RunValidationCommand(stream, "throw_mode.xlsx", _mappingId, EmptyFieldMode.Throw));
        }

        report.RejectedRows.ShouldBe(3);
        report.Errors.Select(e => e.RowNumber).ShouldBe([2, 4, 6]);
        report.Errors.All(e => e.Reason.Contains("AccountNumber")).ShouldBeTrue();

        // The real import: aborted, nothing written, batch Failed with the cause.
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.MissingAccountNumber, EmptyFieldMode.Throw);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);
        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Failed);
        batch.FailedReason.ShouldNotBeNull();
        batch.FailedReason.ShouldContain("AccountNumber");
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(0);
        (await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == started.BatchId))).Count.ShouldBe(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<int> CreateMappingAsync()
    {
        var columns = new System.Collections.Generic.List<ColumnMappingDto>
        {
            new("AccountNumber", null, "AccountNumber", null, null, null, false, null, null, IsRequired: true),
            new("ClientName",    null, "ClientName",    null, null, null, false, null, null, IsRequired: true),
            new("Balance",       null, "Balance",       null, null, ".",  false, null, null, IsRequired: false),
            new("OpenedDate",    null, "OpenedDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: false),
        };
        return await TestApp.SendAsync(new CreateMappingCommand("ClientAccount_EmptyModes", null, TargetEntity, columns));
    }

    private async Task<ImportStarted> SendImportAsync(ClientAccountFixtures.Scenario scenario, EmptyFieldMode emptyMode)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(scenario);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new ImportFileCommand(stream, $"client_{scenario}.xlsx", _mappingId, emptyMode, SheetIndex: null));
    }
}
