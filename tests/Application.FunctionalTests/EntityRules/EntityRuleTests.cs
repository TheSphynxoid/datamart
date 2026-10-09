using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.EntityRules.Queries.GetEntityRules;
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

namespace DataMart.Application.FunctionalTests.EntityRules;

/// <summary>
/// End-to-end coverage for per-entity validation rules: the same rule set
/// rejects the same rows in pre-flight validation (Step 4) and in the real
/// import (Step 5). Reuses the ClientAccount Ok fixture (5 valid rows) so any
/// rejection is attributable to rules alone.
/// </summary>
[Collection("FunctionalTests")]
public class EntityRuleTests : IAsyncLifetime
{
    private const string TargetEntity = "ClientAccounts";
    private const string MappingName = "ClientAccount_Rules";

    private int _mappingId;

    public async Task InitializeAsync()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
        _mappingId = await CreateMappingAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConstantRule_RejectsSameRowsInValidationAndImport()
    {
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "Balance",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Constant,
            Value = "1000",
            IsActive = true,
        });
        // Inactive rules impose nothing, even when they would reject everything.
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "Balance",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Constant,
            Value = "99999999",
            IsActive = false,
        });

        // Ok balances: 1500.50, 2750.00, 99.99, 10000.0, 500.00 -> rows 3 + 5 rejected.
        var validation = await SendValidationAsync();
        validation.TotalRows.ShouldBe(5);
        validation.ImportedRows.ShouldBe(3);
        validation.RejectedRows.ShouldBe(2);
        validation.Errors.ShouldContain(e => e.Reason.Contains("Balance must be >= 1000"));

        var started = await SendImportAsync();
        await WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Success);

        var report = await TestApp.SendAsync(new GetImportReportQuery(started.BatchId));
        report.ImportedRows.ShouldBe(3);
        report.RejectedRows.ShouldBe(2);
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(3);
    }

    [Fact]
    public async Task FunctionRule_ResolvesBoundaryAtImportTime()
    {
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "OpenedDate",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Function,
            Function = RuleFunction.TodayMinusDays,
            FunctionArg = 30,
            IsActive = true,
        });

        // Fixture dates are 2024-xx: every row fails a rolling 30-day window.
        var validation = await SendValidationAsync();
        validation.ImportedRows.ShouldBe(0);
        validation.RejectedRows.ShouldBe(5);

        // The message carries the resolved boundary (yyyy-MM-dd), not the
        // function text, so the report explains itself after the fact.
        validation.Errors.ShouldAllBe(e =>
            Regex.IsMatch(e.Reason, @"OpenedDate must be >= \d{4}-\d{2}-\d{2}"));
    }

    [Fact]
    public async Task RuleOnUnmappedField_HasNoEffect()
    {
        // IsActive is not part of the mapping: the rule cannot judge what the
        // import does not carry, so it skips every row.
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "IsActive",
            Operator = RuleOperator.Equals,
            ValueSource = RuleValueSource.Constant,
            Value = "false",
            IsActive = true,
        });

        var validation = await SendValidationAsync();
        validation.ImportedRows.ShouldBe(5);
        validation.RejectedRows.ShouldBe(0);
    }

    [Fact]
    public async Task GetEntityRules_ActiveOnlyFilters()
    {
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "Balance",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Constant,
            Value = "0",
            IsActive = true,
        });
        await TestApp.AddAsync(new EntityValidationRule
        {
            TargetEntity = TargetEntity,
            Field = "Balance",
            Operator = RuleOperator.LessThan,
            ValueSource = RuleValueSource.Constant,
            Value = "0",
            IsActive = false,
        });

        var active = await TestApp.SendAsync(new GetEntityRulesQuery(TargetEntity, ActiveOnly: true));
        active.Count.ShouldBe(1);
        active[0].Field.ShouldBe("Balance");
        active[0].Operator.ShouldBe("GreaterThanOrEqual");

        var all = await TestApp.SendAsync(new GetEntityRulesQuery(TargetEntity, ActiveOnly: false));
        all.Count.ShouldBe(2);
    }

    private static async Task<int> CreateMappingAsync()
    {
        var columns = new List<ColumnMappingDto>
        {
            new("AccountNumber", null, "AccountNumber", null, null, null, false, null, null, IsRequired: true),
            new("ClientName",    null, "ClientName",    null, null, null, false, null, null, IsRequired: true),
            new("Balance",       null, "Balance",       null, null, ".",  false, null, null, IsRequired: false),
            new("OpenedDate",    null, "OpenedDate",    "yyyy-MM-dd", null, null, false, null, null, IsRequired: false),
        };

        return await TestApp.SendAsync(new CreateMappingCommand(MappingName, null, TargetEntity, columns));
    }

    private async Task<ImportReportDto> SendValidationAsync()
    {
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new RunValidationCommand(stream, "client_ok.xlsx", _mappingId, EmptyFieldMode.SkipRow, SheetIndex: null));
    }

    private async Task<ImportStarted> SendImportAsync()
    {
        var bytes = ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok);
        using var stream = new MemoryStream(bytes);
        return await TestApp.SendAsync(new ImportFileCommand(stream, "client_ok.xlsx", _mappingId, EmptyFieldMode.SkipRow, SheetIndex: null));
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
