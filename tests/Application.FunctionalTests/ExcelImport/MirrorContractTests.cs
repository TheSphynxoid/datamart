using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.Application.FunctionalTests.Infrastructure;
using DataMart.Application.Dashboard.Queries.GetImportJournal;
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
/// The mirror contract, widened: for the same file + mapping + format options,
/// the pre-flight validation (dry-run) and the real import must agree ROW BY
/// ROW — the imported/rejected status of every single row, not just the totals.
/// EntityRuleTests covers this for rules; this suite covers the base parse/
/// required-field path across all four fixture scenarios.
/// </summary>
[Collection("FunctionalTests")]
public class MirrorContractTests : IAsyncLifetime
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

    [Theory]
    [InlineData(ClientAccountFixtures.Scenario.Ok)]
    [InlineData(ClientAccountFixtures.Scenario.MissingAccountNumber)]
    [InlineData(ClientAccountFixtures.Scenario.BadTypes)]
    [InlineData(ClientAccountFixtures.Scenario.PartialErrors)]
    public async Task Validation_And_Import_AgreeRowByRow(ClientAccountFixtures.Scenario scenario)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(scenario);

        // 1. The dry-run: same file bytes, same mapping, SkipRow mode.
        ImportReportDto report;
        using (var stream = new MemoryStream(bytes))
        {
            report = await TestApp.SendAsync(new RunValidationCommand(stream, $"{scenario}_mirror.xlsx", _mappingId, EmptyFieldMode.SkipRow));
        }

        // 2. The real import: identical input.
        using (var stream = new MemoryStream(bytes))
        {
            var started = await TestApp.SendAsync(new ImportFileCommand(stream, $"{scenario}_mirror.xlsx", _mappingId, EmptyFieldMode.SkipRow, SheetIndex: null));
            await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

            var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
            batch!.Status.ShouldBe(ImportStatus.Success);

            // 3. Row-by-row agreement.
            var journal = await TestApp.SendAsync(new GetImportJournalQuery(started.BatchId));
            var records = await TestApp.ListAsync<ImportRecord>(q => q.Where(r => r.ImportBatchId == started.BatchId));

            var rejectedFromReport = report.Errors.Select(e => e.RowNumber).OrderBy(n => n).ToList();
            var rejectedFromImport = records.Where(r => r.Status == ImportRowStatus.Rejected)
                .Select(r => r.RowNumber!.Value).OrderBy(n => n).ToList();

            rejectedFromReport.ShouldBe(rejectedFromImport, "the dry-run and the import disagree on which rows are rejected");

            report.TotalRows.ShouldBe(records.Count);
            report.RejectedRows.ShouldBe(rejectedFromImport.Count);
            report.ImportedRows.ShouldBe(records.Count - rejectedFromImport.Count);

            // Every row the dry-run called "imported" is imported, and vice versa.
            var importedNumbers = records.Where(r => r.Status == ImportRowStatus.Imported)
                .Select(r => r.RowNumber!.Value).OrderBy(n => n).ToList();
            var expectedImported = Enumerable.Range(1, report.TotalRows).Except(rejectedFromImport).OrderBy(n => n).ToList();
            importedNumbers.ShouldBe(expectedImported);

            // Batch counters agree with the journal too.
            batch.TotalRows.ShouldBe(report.TotalRows);
            batch.SkippedRows.ShouldBe(rejectedFromImport.Count);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<int> CreateMappingAsync()
    {
        var columns = new List<ColumnMappingDto>
        {
            new("AccountNumber", null, "AccountNumber", null, null, null, false, null, null, IsRequired: true),
            new("ClientName",    null, "ClientName",    null, null, null, false, null, null, IsRequired: true),
            new("Balance",       null, "Balance",       null, null, ".",  false, null, null, IsRequired: false),
            new("OpenedDate",    null, "OpenedDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: false),
        };
        return await TestApp.SendAsync(new CreateMappingCommand("ClientAccount_Mirror", null, TargetEntity, columns));
    }
}
