using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// The import phases exposed for progress display (Validating = the row pass
/// before any DB write, Copying = the COPY): the phase is always one of the
/// legal values while Processing, the processed count ticks during the row
/// pass (the old "stuck at 0%" bug), and it is cleared once the batch is
/// terminal.
/// </summary>
[Collection("FunctionalTests")]
public class ImportPhaseProgressTests : IAsyncLifetime
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
    public async Task Phase_IsClearedAtTerminalStatus()
    {
        var started = await SendImportAsync(ClientAccountFixtures.CreateXlsx(ClientAccountFixtures.Scenario.Ok), EmptyFieldMode.SkipRow);
        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId);

        var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        batch!.Status.ShouldBe(ImportStatus.Success);
        batch.Phase.ShouldBeNull();
    }

    [Fact]
    public async Task Phase_ObservationsMidFlight_AreLegal_AndProcessedRowsTickDuringRowPass()
    {
        // A file big enough that the row pass (parse/transform/validate, no DB
        // writes) lasts longer than one poll tick.
        var started = await SendImportAsync(ClientAccountFixtures.CreateLargeXlsx(rowCount: 3000), EmptyFieldMode.SkipRow);

        var phasesSeen = new HashSet<ImportPhase>();
        var maxValidatingProcessed = 0;
        var sawProcessing = false;
        var pollInterval = TimeSpan.FromMilliseconds(25);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < TimeSpan.FromSeconds(120))
        {
            var batch = await TestApp.FindAsync<ImportBatch>(started.BatchId);
            if (batch is null || batch.Status != ImportStatus.Processing)
                break;

            sawProcessing = true;
            if (batch.Phase is not null)
            {
                phasesSeen.Add(batch.Phase.Value);
                if (batch.Phase == ImportPhase.Validating)
                    maxValidatingProcessed = Math.Max(maxValidatingProcessed, batch.ProcessedRows);
            }
            await Task.Delay(pollInterval);
        }

        await BatchTerminalWaiter.WaitForBatchTerminalAsync(started.BatchId, timeoutSeconds: 120);

        sawProcessing.ShouldBeTrue("the batch should have been observed Processing at least once");
        // Big file: the pass + COPY last well over one poll tick — at least one
        // phase must have been observable mid-flight.
        phasesSeen.ShouldNotBeEmpty();
        phasesSeen.All(p => p is ImportPhase.Validating or ImportPhase.Copying)
            .ShouldBeTrue("every observed phase must be a legal mid-flight phase");

        // The old bug: the row pass never touched ProcessedRows, so the bar sat
        // at 0% until the COPY. With the throttle, at least one tick must have
        // carried a non-zero count while still Validating.
        maxValidatingProcessed.ShouldBeGreaterThan(0);

        var terminal = await TestApp.FindAsync<ImportBatch>(started.BatchId);
        terminal!.Phase.ShouldBeNull();
        terminal.Status.ShouldBe(ImportStatus.Success);
        // The journal catch-up is asynchronous by design (runs after Success) —
        // assert the durable outcome instead: every row landed in the target table.
        (await TestApp.CountAsync<ClientAccount>()).ShouldBe(3000);
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
        return await TestApp.SendAsync(new CreateMappingCommand("ClientAccount_PhaseProgress", null, TargetEntity, columns));
    }

    private async Task<ImportStarted> SendImportAsync(byte[] xlsx, EmptyFieldMode emptyMode)
    {
        using var stream = new MemoryStream(xlsx);
        return await TestApp.SendAsync(new ImportFileCommand(stream, "client_phase.xlsx", _mappingId, emptyMode, SheetIndex: null));
    }
}
