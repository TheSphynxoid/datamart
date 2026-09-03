using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataMart.Application.Dashboard.Queries.GetImportBatches;
using DataMart.Application.Dashboard.Queries.GetImportJournal;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.Application.FunctionalTests.Infrastructure;
using NUnit.Framework;
using Shouldly;

namespace DataMart.Application.FunctionalTests.Dashboard;

/// <summary>
/// Integration tests for the dashboard's filter params (search, status, date range
/// on batches; status on the journal). These exercise the MediatR pipeline against
/// a real Postgres via the Aspire test host.
/// </summary>
[TestFixture]
public class DashboardFilterTests
{
    private const string TargetEntity = "ClientAccounts";
    private const string MappingName = "ClientAccount_Dashboard";

    private int _mappingId;

    [SetUp]
    public async Task SetUp()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _mappingId = await CreateClientAccountMappingAsync();
    }

    // -----------------------------------------------------------------------
    // 1. Search by file name (case-insensitive)
    // -----------------------------------------------------------------------
    [Test]
    public async Task Search_FiltersByFileName_CaseInsensitive()
    {
        var startedA = await SendImportAsync("alpha-batch.xlsx");
        var startedB = await SendImportAsync("beta-batch.xlsx");
        await WaitForBatchTerminalAsync(startedA.BatchId);
        await WaitForBatchTerminalAsync(startedB.BatchId);

        var result = await TestApp.SendAsync(new GetImportBatchesQuery(Search: "ALPHA"));
        result.Items.ShouldHaveSingleItem();
        result.Items[0].Id.ShouldBe(startedA.BatchId);

        result = await TestApp.SendAsync(new GetImportBatchesQuery(Search: "batch"));
        result.TotalCount.ShouldBe(2);
    }

    [Test]
    public async Task Search_EmptyString_ReturnsAll()
    {
        var startedA = await SendImportAsync("one.xlsx");
        await WaitForBatchTerminalAsync(startedA.BatchId);

        var result = await TestApp.SendAsync(new GetImportBatchesQuery(Search: ""));
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(1);

        result = await TestApp.SendAsync(new GetImportBatchesQuery(Search: "  "));
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var started = await SendImportAsync("solo.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var result = await TestApp.SendAsync(new GetImportBatchesQuery(Search: "no-such-file"));
        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    // -----------------------------------------------------------------------
    // 2. Status filter
    // -----------------------------------------------------------------------
    [Test]
    public async Task Status_Filter_Success_ExcludesProcessingAndFailed()
    {
        var started = await SendImportAsync("ok.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var successOnly = await TestApp.SendAsync(new GetImportBatchesQuery(Status: ImportStatus.Success));
        successOnly.Items.ShouldContain(b => b.Id == started.BatchId);

        var processingOnly = await TestApp.SendAsync(new GetImportBatchesQuery(Status: ImportStatus.Processing));
        processingOnly.Items.ShouldNotContain(b => b.Id == started.BatchId);
    }

    // -----------------------------------------------------------------------
    // 3. Affected table filter
    // -----------------------------------------------------------------------
    [Test]
    public async Task AffectedTable_Filter_ReturnsOnlyMatching()
    {
        var started = await SendImportAsync("ok.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var result = await TestApp.SendAsync(new GetImportBatchesQuery(AffectedTable: TargetEntity));
        result.Items.ShouldContain(b => b.Id == started.BatchId);

        result = await TestApp.SendAsync(new GetImportBatchesQuery(AffectedTable: "NonExistent"));
        result.Items.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // 4. Date range filter
    // -----------------------------------------------------------------------
    [Test]
    public async Task DateRange_FromInFuture_ReturnsEmpty()
    {
        var started = await SendImportAsync("ok.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var future = DateTimeOffset.UtcNow.AddDays(7);
        var result = await TestApp.SendAsync(new GetImportBatchesQuery(From: future));
        result.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task DateRange_FromInPast_IncludesAll()
    {
        var started = await SendImportAsync("ok.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var past = DateTimeOffset.UtcNow.AddDays(-7);
        var result = await TestApp.SendAsync(new GetImportBatchesQuery(From: past));
        result.Items.ShouldContain(b => b.Id == started.BatchId);
    }

    // -----------------------------------------------------------------------
    // 5. Combined filters
    // -----------------------------------------------------------------------
    [Test]
    public async Task Search_CombinedWithStatus_ReturnsIntersection()
    {
        var started = await SendImportAsync("combined.xlsx");
        await WaitForBatchTerminalAsync(started.BatchId);

        var result = await TestApp.SendAsync(new GetImportBatchesQuery(
            Search: "combined",
            Status: ImportStatus.Success,
            AffectedTable: TargetEntity));
        result.Items.ShouldHaveSingleItem();
        result.Items[0].Id.ShouldBe(started.BatchId);

        result = await TestApp.SendAsync(new GetImportBatchesQuery(
            Search: "combined",
            Status: ImportStatus.Failed,
            AffectedTable: TargetEntity));
        result.Items.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // 6. Journal status filter
    // -----------------------------------------------------------------------
    [Test]
    public async Task Journal_Status_ImportedOnly_ExcludesRejected()
    {
        // Use the partial-errors fixture (some imported, some rejected).
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.PartialErrors);
        using var stream = new MemoryStream(bytes);
        var started = await TestApp.SendAsync(new ImportFileCommand(
            stream, $"journal-status-{Guid.NewGuid():N}.xlsx", _mappingId, EmptyFieldMode.SkipRow, null));
        await WaitForBatchTerminalAsync(started.BatchId);

        var imported = await TestApp.SendAsync(new GetImportJournalQuery(
            started.BatchId, Status: ImportRowStatus.Imported));
        imported.TotalCount.ShouldBeGreaterThan(0);
        imported.Items.All(e => e.Status == ImportRowStatus.Imported).ShouldBeTrue();

        var rejected = await TestApp.SendAsync(new GetImportJournalQuery(
            started.BatchId, Status: ImportRowStatus.Rejected));
        rejected.TotalCount.ShouldBeGreaterThan(0);
        rejected.Items.All(e => e.Status == ImportRowStatus.Rejected).ShouldBeTrue();

        // Sum should equal the batch's row count.
        (imported.TotalCount + rejected.TotalCount).ShouldBe(10);
    }

    [Test]
    public async Task Journal_NoStatusFilter_ReturnsAll()
    {
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.PartialErrors);
        using var stream = new MemoryStream(bytes);
        var started = await TestApp.SendAsync(new ImportFileCommand(
            stream, $"journal-all-{Guid.NewGuid():N}.xlsx", _mappingId, EmptyFieldMode.SkipRow, null));
        await WaitForBatchTerminalAsync(started.BatchId);

        var all = await TestApp.SendAsync(new GetImportJournalQuery(started.BatchId));
        all.TotalCount.ShouldBe(10);
    }

    [Test]
    public void Journal_UnknownBatch_ThrowsNotFound()
    {
        Should.ThrowAsync<Ardalis.GuardClauses.NotFoundException>(
            () => TestApp.SendAsync(new GetImportJournalQuery(99999)));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<int> CreateClientAccountMappingAsync()
    {
        var columns = new List<ColumnMappingDto>
        {
            new("AccountNumber", null, "AccountNumber", null, null, null, false, null, null, IsRequired: true),
            new("ClientName",    null, "ClientName",    null, null, null, false, null, null, IsRequired: true),
            new("Balance",       null, "Balance",       null, null, ".",  false, null, null, IsRequired: false),
            new("OpenedDate",    null, "OpenedDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: false),
        };
        var command = new CreateMappingCommand(MappingName, null, TargetEntity, columns);
        return await TestApp.SendAsync(command);
    }

    private async Task<ImportStarted> SendImportAsync(string fileName)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new ImportFileCommand(
            stream, fileName, _mappingId, EmptyFieldMode.SkipRow, null));
    }

    private static async Task WaitForBatchTerminalAsync(int batchId, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var batch = await TestApp.FindAsync<ImportBatch>(batchId);
            if (batch is not null && batch.Status != ImportStatus.Processing)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Batch {batchId} did not reach a terminal state within {timeoutSeconds}s.");
    }
}
