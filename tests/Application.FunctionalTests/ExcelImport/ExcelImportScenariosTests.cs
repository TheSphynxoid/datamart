using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Commands.RunValidation;
using DataMart.Application.ImportFile.Queries.GetImportReport;
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
/// Integration tests for the five scenarios required by the assignment:
///   1. import reussi
///   2. colonne obligatoire absente
///   3. type invalide
///   4. fichier avec des erreurs partielles
///   5. mapping inexistant
///
/// These tests exercise the MediatR command pipeline (RunValidationCommand and
/// ImportFileCommand) end-to-end against a real Postgres via the Aspire test
/// host. The .xlsx fixtures are produced in memory from
/// <see cref="ClientAccountFixtures"/>; the same bytes are also materialised
/// to the source Fixtures folder on the first test run (the "jeux de tests"
/// deliverable).
/// </summary>
[Collection("FunctionalTests")]
public class ExcelImportScenariosTests : IAsyncLifetime
{
    // GetEntityList() returns DTOs whose Name is the EF table name (e.g. "ClientAccounts"),
    // not the CLR class name. The validator's uniqueness/Must check and the import
    // executor's schema lookup both match on this table-name identifier, so the test
    // uses the same string the wizard stores on the mapping.
    private const string TargetEntity = "ClientAccounts";
    private const string MappingName = "ClientAccount_Default";

    private int _mappingId;

    public async Task InitializeAsync()
    {
        // Each test starts from a clean database (Respawn truncates all tables).
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _mappingId = await CreateClientAccountMappingAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // 1. Import reussi
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Scenario_01_SuccessfulImport_ImportsAllRows()
    {
        var started = await SendImportAsync(ClientAccountFixtures.Scenario.Ok, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch.ShouldNotBeNull();
        batch!.Status.ShouldBe(ImportStatus.Success);

        // 5 rows, all valid, all imported, 0 rejected.
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(5);

        // Regression (FC-1): control totals count each non-null cell ONCE.
        // A double-count bug (call site + UpdateStats both incrementing) made
        // Count read 10 for 5 rows — silently wrong reconciliation numbers.
        using var totals = JsonDocument.Parse(batch.ControlTotalsJson!);
        totals.RootElement.GetProperty("RowCount").GetInt32().ShouldBe(5);
        var accountNumberStats = totals.RootElement.GetProperty("ColumnStats").EnumerateArray()
            .Single(s => s.GetProperty("Column").GetString() == "AccountNumber");
        accountNumberStats.GetProperty("Count").GetInt64().ShouldBe(5);

        var report = await TestApp.SendAsync(new GetImportReportQuery(started.BatchId));
        report.ImportedRows.ShouldBe(5);
        report.RejectedRows.ShouldBe(0);
        report.Errors.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // 2. Colonne obligatoire absente
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Scenario_02_MissingRequiredColumn_RejectsOnlyThoseRows()
    {
        var validation = await SendValidationAsync(ClientAccountFixtures.Scenario.MissingAccountNumber);
        validation.RejectedRows.ShouldBe(3);
        validation.ImportedRows.ShouldBe(4);
        validation.Errors.ShouldContain(e => e.Reason.Contains("required field 'AccountNumber'"));

        var started = await SendImportAsync(ClientAccountFixtures.Scenario.MissingAccountNumber, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var report = await TestApp.SendAsync(new GetImportReportQuery(started.BatchId));
        report.ImportedRows.ShouldBe(4);
        report.RejectedRows.ShouldBe(3);
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(4);
    }

    // -----------------------------------------------------------------------
    // 3. Type invalide
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Scenario_03_InvalidType_RejectsBadRows()
    {
        var validation = await SendValidationAsync(ClientAccountFixtures.Scenario.BadTypes);
        validation.RejectedRows.ShouldBe(3);
        validation.ImportedRows.ShouldBe(3);

        var started = await SendImportAsync(ClientAccountFixtures.Scenario.BadTypes, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var report = await TestApp.SendAsync(new GetImportReportQuery(started.BatchId));
        report.ImportedRows.ShouldBe(3);
        report.RejectedRows.ShouldBe(3);
        report.Errors.Any(e => e.Reason.Contains("Cannot parse decimal")).ShouldBeTrue();
        report.Errors.Any(e => e.Reason.Contains("Cannot parse date")).ShouldBeTrue();
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(3);
    }

    // -----------------------------------------------------------------------
    // 4. Fichier avec des erreurs partielles (the showcase scenario)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Scenario_04_PartialErrors_ImportsGoodRowsAndReportsRejections()
    {
        // 10 rows: 7 good + 2 missing required + 1 bad type. Under SkipRow the import
        // succeeds with 7 imported and 3 rejected, and the journal + report both reflect
        // exactly that (proving the shared ImportCellParser keeps validation and import
        // aligned).
        var validation = await SendValidationAsync(ClientAccountFixtures.Scenario.PartialErrors);
        validation.TotalRows.ShouldBe(10);
        validation.ImportedRows.ShouldBe(7);
        validation.RejectedRows.ShouldBe(3);

        var started = await SendImportAsync(ClientAccountFixtures.Scenario.PartialErrors, EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Success);

        var report = await TestApp.SendAsync(new GetImportReportQuery(started.BatchId));
        report.ImportedRows.ShouldBe(7);
        report.RejectedRows.ShouldBe(3);
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(7);

        // The report errors include both kinds of rejection.
        report.Errors.Any(e => e.Reason.Contains("required field 'AccountNumber'")).ShouldBeTrue();
        report.Errors.Any(e => e.Reason.Contains("Cannot parse decimal")).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------
    // 5. Mapping inexistant
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Scenario_05_MappingDoesNotExist_ValidationThrowsNotFound()
    {
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok);
        using var stream = new MemoryStream(bytes);

        var command = new RunValidationCommand(stream, "client_ok.xlsx", MappingId: 9999, EmptyFieldMode.SkipRow, SheetIndex: null);

        // The handler uses Guard.Against.NotFound for the mapping lookup.
        await Should.ThrowAsync<NotFoundException>(async () => await TestApp.SendAsync(command));
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

    private async Task<ImportReportDto> SendValidationAsync(ClientAccountFixtures.Scenario scenario)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(scenario);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new RunValidationCommand(stream, $"client_{scenario}.xlsx", _mappingId, EmptyFieldMode.SkipRow, SheetIndex: null));
    }

    private async Task<ImportStarted> SendImportAsync(ClientAccountFixtures.Scenario scenario, EmptyFieldMode emptyMode)
    {
        var bytes = ClientAccountFixtures.CreateXlsx(scenario);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new ImportFileCommand(stream, $"client_{scenario}.xlsx", _mappingId, emptyMode, SheetIndex: null));
    }

}

