using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using DataMart.Application.FunctionalTests.Infrastructure;
using DataMart.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DataMart.Application.FunctionalTests.ExcelImport;

/// <summary>
/// Functional coverage of the ImportProcessingService's startup reconciliation:
/// batches a previous (crashed) run left behind. Tests seed the fake crashed
/// state directly in the DB and call the extracted
/// <c>ReconcileStaleBatchesAsync</c> — no process is actually killed; the sweep
/// logic itself is what's under test.
/// </summary>
[Collection("FunctionalTests")]
public class SweepRecoveryTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sweep_MarksStaleProcessingBatchFailed_LeavesOthersUntouched()
    {
        // Simulate a crash mid-row-pass: Processing + snapshots persisted + a
        // non-null phase (the sweep's Fail must clear it).
        var stale = new ImportBatch
        {
            FileName = "crashed_mid_run.xlsx",
            AffectedTable = "ClientAccounts",
            SourceChecksum = "stale-checksum"
        };
        await TestApp.AddAsync(stale);
        var staleId = stale.Id;
        await TestApp.EditAsync<ImportBatch>(staleId, b =>
        {
            b.TotalRows = 100;
            b.MarkStarted("{}", "{}");
            b.Phase = ImportPhase.Validating;
        });

        // An already-terminal batch must not be touched by the sweep.
        var success = new ImportBatch
        {
            FileName = "already_success.xlsx",
            AffectedTable = "ClientAccounts",
            SourceChecksum = "done-checksum"
        };
        await TestApp.AddAsync(success);
        var successId = success.Id;
        await TestApp.EditAsync<ImportBatch>(successId, b =>
        {
            b.TotalRows = 5;
            b.ProcessedRows = 5;
            b.Complete();
        });

        var reconciled = await ReconcileAsync();

        reconciled.ShouldBeTrue();

        var staleReloaded = await TestApp.FindAsync<ImportBatch>(staleId);
        staleReloaded!.Status.ShouldBe(ImportStatus.Failed);
        staleReloaded.CompletedAt.ShouldNotBeNull();
        staleReloaded.FailedReason.ShouldNotBeNull();
        staleReloaded.FailedReason.ShouldContain("Interrupted");
        staleReloaded.Phase.ShouldBeNull();

        var successReloaded = await TestApp.FindAsync<ImportBatch>(successId);
        successReloaded!.Status.ShouldBe(ImportStatus.Success);
        successReloaded.FailedReason.ShouldBeNull();
    }

    [Fact]
    public async Task Sweep_FlagsPartialJournalOnSuccessBatch()
    {
        // A Success batch whose journal upload died mid-flight: 4 of 10 rows written.
        var partial = new ImportBatch
        {
            FileName = "journal_killed.xlsx",
            AffectedTable = "ClientAccounts",
            SourceChecksum = "partial-journal"
        };
        await TestApp.AddAsync(partial);
        var partialId = partial.Id;
        await TestApp.EditAsync<ImportBatch>(partialId, b =>
        {
            b.TotalRows = 10;
            b.ProcessedRows = 10;
            b.JournalRowsWritten = 4;
            b.Complete();
        });

        // A complete journal stays untouched.
        var complete = new ImportBatch
        {
            FileName = "journal_complete.xlsx",
            AffectedTable = "ClientAccounts",
            SourceChecksum = "complete-journal"
        };
        await TestApp.AddAsync(complete);
        var completeId = complete.Id;
        await TestApp.EditAsync<ImportBatch>(completeId, b =>
        {
            b.TotalRows = 10;
            b.ProcessedRows = 10;
            b.JournalRowsWritten = 10;
            b.Complete();
        });

        // Already-flagged batches are not re-touched (idempotent sweep).
        var flagged = new ImportBatch
        {
            FileName = "already_flagged.xlsx",
            AffectedTable = "ClientAccounts",
            SourceChecksum = "already-flagged"
        };
        await TestApp.AddAsync(flagged);
        var flaggedId = flagged.Id;
        await TestApp.EditAsync<ImportBatch>(flaggedId, b =>
        {
            b.TotalRows = 10;
            b.ProcessedRows = 10;
            b.JournalRowsWritten = 2;
            b.JournalIncomplete = true;
            b.Complete();
        });

        await ReconcileAsync();

        (await TestApp.FindAsync<ImportBatch>(partialId))!.JournalIncomplete.ShouldBeTrue();
        (await TestApp.FindAsync<ImportBatch>(completeId))!.JournalIncomplete.ShouldBeFalse();
        (await TestApp.FindAsync<ImportBatch>(flaggedId))!.JournalIncomplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Sweep_OnCleanState_IsANoOp()
    {
        var reconciled = await ReconcileAsync();
        reconciled.ShouldBeFalse();
        (await TestApp.CountAsync<ImportBatch>()).ShouldBe(0);
    }

    /// <summary>Runs the reconciliation and reports whether it wrote anything
    /// (so a clean-state no-op is distinguishable from a repair).</summary>
    private static async Task<bool> ReconcileAsync()
    {
        var before = await TestApp.ListAsync<ImportBatch>();
        var beforeJson = before.OrderBy(b => b.Id).Select(b => (b.Id, b.Status, b.JournalIncomplete, b.FailedReason)).ToList();

        // The processing service is registered as a hosted service (IHostedService),
        // not by concrete type: build a fresh instance from the host's provider.
        // ReconcileStaleBatchesAsync creates its own scope internally.
        using var outerScope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var service = ActivatorUtilities.CreateInstance<ImportProcessingService>(outerScope.ServiceProvider);
        await service.ReconcileStaleBatchesAsync(CancellationToken.None);

        var after = await TestApp.ListAsync<ImportBatch>();
        var afterJson = after.OrderBy(b => b.Id).Select(b => (b.Id, b.Status, b.JournalIncomplete, b.FailedReason)).ToList();

        return !beforeJson.SequenceEqual(afterJson);
    }
}
