using DataMart.Domain.Entities;
using DataMart.Domain.Enums;

namespace DataMart.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Shared wait helper for import tests: polls the batch until it reaches a
/// terminal status (Success/Failed/Reverted) or times out. Extracted after the
/// same body had been copied into four test classes.
/// </summary>
public static class BatchTerminalWaiter
{
    public static async Task WaitForBatchTerminalAsync(int batchId, int timeoutSeconds = 30)
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

    /// <summary>
    /// Waits until the batch is terminal AND its journal catch-up has finished
    /// (JournalRowsWritten >= TotalRows). The journal upload runs AFTER the
    /// terminal save by design, so assertions on JournalRowsWritten/JournalIncomplete
    /// must wait for the catch-up, not just the terminal status.
    /// </summary>
    public static async Task WaitForJournalCompleteAsync(int batchId, int timeoutSeconds = 30)
    {
        await WaitForBatchTerminalAsync(batchId, timeoutSeconds);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var batch = await TestApp.FindAsync<ImportBatch>(batchId);
            if (batch is not null && batch.JournalRowsWritten >= batch.TotalRows)
                return;
            await Task.Delay(200);
        }
        var timedOut = await TestApp.FindAsync<ImportBatch>(batchId);
        throw new TimeoutException(
            $"Batch {batchId} journal catch-up did not finish within {timeoutSeconds}s " +
            $"(JournalRowsWritten={timedOut?.JournalRowsWritten}/{timedOut?.TotalRows}).");
    }
}
