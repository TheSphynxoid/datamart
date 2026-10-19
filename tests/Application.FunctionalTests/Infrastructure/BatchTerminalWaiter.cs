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
}
