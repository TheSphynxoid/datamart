using System;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Shouldly;
using Xunit;

namespace DataMart.Domain.UnitTests.Entities;

/// <summary>
/// Journal-framework (V2 build item 1) domain behavior: tier-1 facts, the
/// MarkStarted/Complete/Fail transitions, the snapshot set-once + size-cap
/// rules, and their interactions with Revert and the immutability guard.
/// Spec: docs/specs/journal-framework.md §3 and §8.
/// </summary>
public class ImportBatchJournalFrameworkTests
{
    private static ImportBatch CreateProcessingBatch() => new()
    {
        Id = 1,
        FileName = "test.xlsx",
        AffectedTable = "TestTable",
        TotalRows = 10,
        SourceChecksum = "abc123"
    };

    private static string JsonOfLength(int length) => new('x', length);

    // --- MarkStarted ---

    [Fact]
    public void MarkStarted_SetsStartedAt_AndStoresSnapshots()
    {
        var batch = CreateProcessingBatch();

        var before = DateTimeOffset.UtcNow;
        batch.MarkStarted("""{"name":"m"}""", """{"rules":[]}""");
        var after = DateTimeOffset.UtcNow;

        batch.Status.ShouldBe(ImportStatus.Processing);
        batch.StartedAt.ShouldNotBeNull();
        batch.StartedAt!.Value.ShouldBeInRange(before, after);
        batch.MappingSnapshotJson.ShouldBe("""{"name":"m"}""");
        batch.RulesSnapshotJson.ShouldBe("""{"rules":[]}""");
        batch.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkStarted_IsSetOnce()
    {
        var batch = CreateProcessingBatch();
        batch.MarkStarted("first", "rules-first");

        Should.Throw<InvalidOperationException>(() => batch.MarkStarted("second", "rules-second"))
            .Message.ShouldContain("MarkStarted");
        batch.MappingSnapshotJson.ShouldBe("first");
        batch.RulesSnapshotJson.ShouldBe("rules-first");
    }

    [Fact]
    public void MarkStarted_Throws_WhenMappingSnapshotOverCap_AndStoresNothing()
    {
        var batch = CreateProcessingBatch();

        Should.Throw<InvalidOperationException>(
                () => batch.MarkStarted(JsonOfLength(ImportBatch.MaxSnapshotJsonBytes + 1), "rules"))
            .Message.ShouldContain("snapshot");

        // Binary audit value: over the cap nothing is stored, not even a truncated copy.
        batch.MappingSnapshotJson.ShouldBeNull();
        batch.RulesSnapshotJson.ShouldBeNull();
        batch.StartedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkStarted_Throws_WhenRulesSnapshotOverCap_AndStoresNothing()
    {
        var batch = CreateProcessingBatch();

        Should.Throw<InvalidOperationException>(
                () => batch.MarkStarted("mapping", JsonOfLength(ImportBatch.MaxSnapshotJsonBytes + 1)))
            .Message.ShouldContain("snapshot");

        batch.MappingSnapshotJson.ShouldBeNull();
        batch.RulesSnapshotJson.ShouldBeNull();
        batch.StartedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkStarted_Throws_WhenStatusIsTerminal()
    {
        var batch = CreateProcessingBatch();
        batch.Status = ImportStatus.Success;

        Should.Throw<InvalidOperationException>(() => batch.MarkStarted("m", "r"))
            .Message.ShouldContain("Cannot modify audit fields");
    }

    // --- Complete ---

    [Fact]
    public void Complete_SetsSuccess_AndCompletedAt_WhileStarted()
    {
        var batch = CreateProcessingBatch();
        batch.MarkStarted("m", "r");

        var before = DateTimeOffset.UtcNow;
        batch.Complete();
        var after = DateTimeOffset.UtcNow;

        batch.Status.ShouldBe(ImportStatus.Success);
        batch.CompletedAt.ShouldNotBeNull();
        batch.CompletedAt!.Value.ShouldBeInRange(before, after);
        batch.FailedReason.ShouldBeNull();
    }

    [Fact]
    public void Complete_Throws_WhenAlreadySuccess()
    {
        var batch = CreateProcessingBatch();
        batch.Complete();

        Should.Throw<InvalidOperationException>(() => batch.Complete())
            .Message.ShouldContain("Processing");
    }

    [Fact]
    public void Complete_Throws_WhenFailed()
    {
        var batch = CreateProcessingBatch();
        batch.Fail("boom");

        Should.Throw<InvalidOperationException>(() => batch.Complete())
            .Message.ShouldContain("Processing");
    }

    // --- Fail ---

    [Fact]
    public void Fail_SetsFailed_CompletedAt_AndReason()
    {
        var batch = CreateProcessingBatch();

        batch.Fail("COPY failed");

        batch.Status.ShouldBe(ImportStatus.Failed);
        batch.FailedReason.ShouldBe("COPY failed");
        batch.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Fail_Throws_WhenAlreadySuccess()
    {
        var batch = CreateProcessingBatch();
        batch.Complete();

        Should.Throw<InvalidOperationException>(() => batch.Fail("late failure"))
            .Message.ShouldContain("Processing");
    }

    // --- Tier-1 setters (ID range, extras bag) ---

    [Fact]
    public void SetImpactedIdRange_StoresRange()
    {
        var batch = CreateProcessingBatch();

        batch.SetImpactedIdRange(100, 1099);

        batch.MinImpactedId.ShouldBe(100);
        batch.MaxImpactedId.ShouldBe(1099);
    }

    [Fact]
    public void SetImpactedIdRange_Throws_WhenStatusIsSuccess()
    {
        var batch = CreateProcessingBatch();
        batch.Complete();

        Should.Throw<InvalidOperationException>(() => batch.SetImpactedIdRange(1, 2))
            .Message.ShouldContain("Cannot modify audit fields");
    }

    [Fact]
    public void SetJournalExtras_StoresWholeObjectJson()
    {
        var batch = CreateProcessingBatch();

        batch.SetJournalExtras("""{"upsell":true}""");

        batch.JournalExtrasJson.ShouldBe("""{"upsell":true}""");
    }

    [Fact]
    public void SetJournalExtras_Throws_WhenStatusIsFailed()
    {
        var batch = CreateProcessingBatch();
        batch.Fail("boom");

        Should.Throw<InvalidOperationException>(() => batch.SetJournalExtras("{}"))
            .Message.ShouldContain("Cannot modify audit fields");
    }

    // --- Revert interactions (spec §8: guard interactions with Revert) ---

    [Fact]
    public void Revert_AfterComplete_KeepsCompletedAt_AndSetsRevertedAt()
    {
        var batch = CreateProcessingBatch();
        batch.MarkStarted("m", "r");
        batch.Complete();
        var completedAt = batch.CompletedAt!.Value;

        batch.Revert("user1");

        batch.Status.ShouldBe(ImportStatus.Reverted);
        batch.CompletedAt.ShouldBe(completedAt); // CompletedAt is not the revert stamp; RevertedAt is.
        batch.RevertedAt.ShouldNotBeNull();
        batch.RevertedBy.ShouldBe("user1");
    }

    [Fact]
    public void Revert_AfterFail_SetsReverted()
    {
        var batch = CreateProcessingBatch();
        batch.Fail("boom");

        batch.Revert("user1");

        batch.Status.ShouldBe(ImportStatus.Reverted);
        batch.RevertedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Revert_WhileProcessing_LeavesStartedAt_AndNoCompletedAt()
    {
        var batch = CreateProcessingBatch();
        batch.MarkStarted("m", "r");

        batch.Revert("user1");

        batch.Status.ShouldBe(ImportStatus.Reverted);
        batch.StartedAt.ShouldNotBeNull();
        batch.CompletedAt.ShouldBeNull();
    }
}
