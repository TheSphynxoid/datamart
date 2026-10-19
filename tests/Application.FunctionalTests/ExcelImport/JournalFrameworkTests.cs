using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataMart.Application.Dashboard.Queries.GetBatchDetail;
using DataMart.Application.Dashboard.Queries.GetImportJournal;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.Application.FunctionalTests.Infrastructure;
using Shouldly;
using Xunit;

namespace DataMart.Application.FunctionalTests.ExcelImport;

/// <summary>
/// Journal-framework functional tests (spec docs/specs/journal-framework.md §8):
/// snapshots match the live config at import time and survive later edits, the
/// impacted-ID range brackets the COPY output, journal chunks stamp Created, and
/// the drill-down orders by the typed RowNumber. The V1 import scenarios
/// (ExcelImportScenariosTests) are the behavior-preserving gate for the
/// writer-seam refactor — unchanged and still authoritative.
/// </summary>
[Collection("FunctionalTests")]
public class JournalFrameworkTests : IAsyncLifetime
{
    private const string TargetEntity = "ClientAccounts";

    private int _mappingId;

    public async Task InitializeAsync()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _mappingId = await CreateClientAccountMappingAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Snapshots_MatchLiveConfig_AtImportTime_AndSurviveLaterEdits()
    {
        // Seed one active rule so the rules snapshot has real content.
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "Balance",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Constant,
            Value = "0",
            Message = "Balance must be non-negative",
            IsActive = true
        });

        var started = await SendImportAsync(ClientAccountFixtures.Scenario.Ok, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch.ShouldNotBeNull();
        batch!.StartedAt.ShouldNotBeNull();
        batch.CompletedAt.ShouldNotBeNull();

        var mappingSnapshot = JsonDocument.Parse(batch.MappingSnapshotJson!);
        mappingSnapshot.RootElement.GetProperty("targetEntity").GetString().ShouldBe(TargetEntity);
        var snapshotColumns = mappingSnapshot.RootElement.GetProperty("columns");
        snapshotColumns.GetArrayLength().ShouldBe(4);
        snapshotColumns.EnumerateArray().Any(c => c.GetProperty("columnName").GetString() == "AccountNumber"
            && c.GetProperty("isRequired").GetBoolean()).ShouldBeTrue();
        snapshotColumns.EnumerateArray().Single(c => c.GetProperty("columnName").GetString() == "OpenedDate")
            .GetProperty("dateFormat").GetString().ShouldBe("yyyy-MM-dd");

        var rulesSnapshot = JsonDocument.Parse(batch.RulesSnapshotJson!);
        var snapshotRules = rulesSnapshot.RootElement.EnumerateArray().ToArray();
        snapshotRules.Length.ShouldBe(1);
        snapshotRules[0].GetProperty("field").GetString().ShouldBe("Balance");
        snapshotRules[0].GetProperty("operator").GetString().ShouldBe("GreaterThanOrEqual");

        // Snapshot immutability (the audit point): mutate the live mapping AND the
        // live rule after the import, then re-load the batch — the stored copies
        // must be byte-for-byte unchanged.
        var mappingJsonBefore = batch.MappingSnapshotJson;
        var rulesJsonBefore = batch.RulesSnapshotJson;

        // Owned collection: edit inside one tracked scope (shadow keys cannot
        // survive a detach/reattach round-trip).
        await TestApp.EditAsync<Mapping>(_mappingId, mapping =>
            mapping.Columns.Add(new ColumnMapping
            {
                ColumnName = "UnmappedSource",
                TargetColName = "ClientName",
                IsRequired = false
            }));

        var rule = (await TestApp.ListAsync<EntityValidationRule>()).Single();
        rule.Value = "-1";
        await TestApp.UpdateAsync(rule);

        var reloaded = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        reloaded!.MappingSnapshotJson.ShouldBe(mappingJsonBefore);
        reloaded.RulesSnapshotJson.ShouldBe(rulesJsonBefore);
    }

    [Fact]
    public async Task ImpactedIdRange_BracketsTheCopyOutput()
    {
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.Ok, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var createdIds = (await TestApp.ListAsync<ClientAccount>())
            .Select(c => c.Id)
            .OrderBy(id => id)
            .ToList();
        createdIds.Count.ShouldBe(5);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.MinImpactedId.ShouldBe(createdIds.First());
        batch.MaxImpactedId.ShouldBe(createdIds.Last());
    }

    [Fact]
    public async Task JournalChunks_StampCreated_AndDrillDownOrdersByRowNumber()
    {
        // PartialErrors: 10 rows, 7 imported + 3 rejected — rejected rows interleave
        // with imported ones, so RowNumber order proves the drill-down's stable sort.
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.PartialErrors, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Success);
        batch.JournalIncomplete.ShouldBeFalse();
        batch.JournalRowsWritten.ShouldBe(10);

        var records = await TestApp.ListAsync<ImportRecord>(q =>
            q.Where(r => r.ImportBatchId == started.BatchId));
        records.Count.ShouldBe(10);
        foreach (var record in records)
        {
            // Created is stamped in the chunk-write path (never DateTime.MinValue).
            record.Created.ShouldNotBe(default);
            record.Created.ShouldBeGreaterThanOrEqualTo(batch.StartedAt!.Value.AddSeconds(-5));
            // SourceKey stays as the write-once provenance string, equal to the typed row.
            record.SourceKey.ShouldBe(record.RowNumber!.Value.ToString());
            record.RowNumber!.Value.ShouldBeInRange(1, 10);
        }
        records.Select(r => r.RowNumber!.Value).ShouldBeUnique();

        var page = await TestApp.SendAsync(new GetImportJournalQuery(started.BatchId));
        page.TotalCount.ShouldBe(10);
        page.Items.Select(i => i.RowNumber).ShouldBe(new int?[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        var report = await TestApp.SendAsync(new DataMart.Application.ImportFile.Queries.GetImportReport.GetImportReportQuery(started.BatchId));
        report.RejectedRows.ShouldBe(3);
        report.Errors.Select(e => e.RowNumber).ShouldBeUnique();
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

        var command = new CreateMappingCommand("ClientAccount_JournalFramework", null, TargetEntity, columns);
        return await TestApp.SendAsync(command);
    }

    private async Task<ImportStarted> SendImportAsync(ClientAccountFixtures.Scenario scenario, EmptyFieldMode emptyMode)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(scenario);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new ImportFileCommand(stream, $"client_{scenario}.xlsx", _mappingId, emptyMode, SheetIndex: null));
    }

}

