using System;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace DataMart.Domain.UnitTests.Entities;

[TestFixture]
public class ImportBatchImmutabilityTests
{
    private ImportBatch CreateBatch(ImportStatus status)
    {
        var batch = new ImportBatch
        {
            Id = 1,
            FileName = "test.xlsx",
            AffectedTable = "TestTable",
            TotalRows = 10,
            SourceChecksum = "abc123"
        };
        // Set status after construction to avoid initialization order issues
        // Reverted can only be reached via Revert() method, so handle it specially
        if (status == ImportStatus.Reverted)
        {
            batch.Status = ImportStatus.Success;
            batch.Revert("test-user");
        }
        else
        {
            batch.Status = status;
        }
        return batch;
    }

    [Test]
    public void FileName_CanBeSet_WhenStatusIsProcessing()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act
        batch.FileName = "updated.xlsx";

        // Assert
        batch.FileName.ShouldBe("updated.xlsx");
    }

    [Test]
    public void FileName_Throws_WhenStatusIsSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.FileName = "updated.xlsx")
            .Message.ShouldContain("Cannot modify audit fields after batch status is Success");
    }

    [Test]
    public void FileName_Throws_WhenStatusIsFailed()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Failed);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.FileName = "updated.xlsx")
            .Message.ShouldContain("Cannot modify audit fields after batch status is Failed");
    }

    [Test]
    public void FileName_Throws_WhenStatusIsReverted()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Reverted);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.FileName = "updated.xlsx")
            .Message.ShouldContain("Cannot modify audit fields after batch status is Reverted");
    }

    [Test]
    public void AffectedTable_CanBeSet_WhenStatusIsProcessing()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act
        batch.AffectedTable = "NewTable";

        // Assert
        batch.AffectedTable.ShouldBe("NewTable");
    }

    [Test]
    public void AffectedTable_Throws_WhenStatusIsSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.AffectedTable = "NewTable")
            .Message.ShouldContain("Cannot modify audit fields after batch status is Success");
    }

    [Test]
    public void SourceChecksum_CanBeSet_WhenStatusIsProcessing()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act
        batch.SourceChecksum = "updated";

        // Assert
        batch.SourceChecksum.ShouldBe("updated");
    }

    [Test]
    public void SourceChecksum_Throws_WhenStatusIsSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.SourceChecksum = "updated")
            .Message.ShouldContain("Cannot modify audit fields after batch status is Success");
    }

    [Test]
    public void Status_CanTransition_FromProcessingToSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act
        batch.Status = ImportStatus.Success;

        // Assert
        batch.Status.ShouldBe(ImportStatus.Success);
    }

    [Test]
    public void Status_CanTransition_FromProcessingToFailed()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act
        batch.Status = ImportStatus.Failed;

        // Assert
        batch.Status.ShouldBe(ImportStatus.Failed);
    }

    [Test]
    public void Status_Throws_WhenChangingFromSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.Status = ImportStatus.Failed)
            .Message.ShouldContain("Cannot change status from Success");
    }

    [Test]
    public void Status_Throws_WhenChangingFromFailed()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Failed);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.Status = ImportStatus.Success)
            .Message.ShouldContain("Cannot change status from Failed");
    }

    [Test]
    public void Status_Throws_WhenChangingFromReverted()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Reverted);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.Status = ImportStatus.Success)
            .Message.ShouldContain("Cannot change status from Reverted");
    }

    [Test]
    public void Status_Throws_InvalidTransition_FromProcessingToReverted()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Processing);

        // Act & Assert - Reverted should only be set via Revert() method
        Should.Throw<InvalidOperationException>(() => batch.Status = ImportStatus.Reverted)
            .Message.ShouldContain("Invalid status transition");
    }

    [Test]
    public void SetControlTotals_Throws_WhenStatusIsSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.SetControlTotals("{}"))
            .Message.ShouldContain("Cannot modify audit fields after batch status is Success");
    }

    [Test]
    public void SetExecutedBy_Throws_WhenStatusIsSuccess()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.SetExecutedBy("user123"))
            .Message.ShouldContain("Cannot modify audit fields after batch status is Success");
    }

    [Test]
    public void Revert_SetsRevertedFields_AndStatus()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act
        batch.Revert("user123");

        // Assert
        batch.Status.ShouldBe(ImportStatus.Reverted);
        batch.RevertedBy.ShouldBe("user123");
        batch.RevertedAt.ShouldNotBeNull();
    }

    [Test]
    public void Revert_Throws_WhenAlreadyReverted()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Reverted);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => batch.Revert("user123"))
            .Message.ShouldContain("already reverted");
    }

    [Test]
    public void LegalHold_CanBeToggled_Anytime()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);

        // Act
        batch.SetLegalHold(true);

        // Assert
        batch.LegalHold.ShouldBeTrue();

        // Act
        batch.SetLegalHold(false);

        // Assert
        batch.LegalHold.ShouldBeFalse();
    }

    [Test]
    public void RetentionExpiresAt_CanBeSet_Anytime()
    {
        // Arrange
        var batch = CreateBatch(ImportStatus.Success);
        var expiresAt = DateTimeOffset.UtcNow.AddYears(7);

        // Act
        batch.SetRetentionExpiresAt(expiresAt);

        // Assert
        batch.RetentionExpiresAt.ShouldBe(expiresAt);
    }
}

[TestFixture]
public class ImportRecordImmutabilityTests
{
    [Test]
    public void SourceKey_CanBeSet_Once()
    {
        // Arrange
        var record = new ImportRecord
        {
            Id = 1,
            Payload = "{}"
        };

        // Act
        record.SourceKey = "row-1";

        // Assert
        record.SourceKey.ShouldBe("row-1");
    }

    [Test]
    public void SourceKey_Throws_WhenSetTwice()
    {
        // Arrange
        var record = new ImportRecord
        {
            Id = 1,
            Payload = "{}",
            SourceKey = "row-1"
        };

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => record.SourceKey = "row-2")
            .Message.ShouldContain("SourceKey is immutable after insert");
    }

    [Test]
    public void ImportBatchId_CanBeSet_Once()
    {
        // Arrange
        var record = new ImportRecord
        {
            Id = 1,
            Payload = "{}"
        };

        // Act
        record.ImportBatchId = 42;

        // Assert
        record.ImportBatchId.ShouldBe(42);
    }

    [Test]
    public void ImportBatchId_Throws_WhenSetTwice()
    {
        // Arrange
        var record = new ImportRecord
        {
            Id = 1,
            Payload = "{}",
            ImportBatchId = 42
        };

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => record.ImportBatchId = 43)
            .Message.ShouldContain("ImportBatchId is immutable after insert");
    }
}