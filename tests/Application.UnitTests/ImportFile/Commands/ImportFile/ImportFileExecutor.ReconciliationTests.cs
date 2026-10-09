using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Common;
using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Moq;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.ImportFile.Commands.ImportFile;

public class ImportBatchControlTotalsTests
{
    [Fact]
    public void SetControlTotals_StoresJson()
    {
        // Arrange
        var batch = new ImportBatch
        {
            Id = 1,
            FileName = "test.xlsx",
            AffectedTable = "Test",
            Status = ImportStatus.Processing,
            TotalRows = 10,
            SourceChecksum = "abc"
        };

        var controlTotalsJson = @"{""RowCount"":5,""ColumnStats"":[]}";

        // Act
        batch.SetControlTotals(controlTotalsJson);

        // Assert
        batch.ControlTotalsJson.ShouldBe(controlTotalsJson);
    }

    [Fact]
    public void ControlTotalsJson_SerializesCorrectly()
    {
        // Arrange
        var controlTotals = new
        {
            RowCount = 100,
            ColumnStats = new[]
            {
                new { Column = "Amount", Type = "Decimal", Count = 100L, Sum = 10000.00m, Min = 10.00m, Max = 500.00m, MinDate = (string?)null, MaxDate = (string?)null }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(controlTotals);

        // Assert
        json.ShouldContain("RowCount");
        json.ShouldContain("100");
        json.ShouldContain("Amount");
        json.ShouldContain("10000.00");
    }
}

public class ImportFileExecutorDecimalPrecisionTests
{
    [Fact]
    public void ParseDecimal_Precision_PreservesExactValues()
    {
        // ParseDecimal moved from ImportFileExecutor to the shared ImportCellParser
        // when the cell-parsing primitives were centralised. Call it directly now
        // that it's public — no need for reflection.

        // Test US format
        var result1 = ImportCellParser.ParseDecimal("1,234.56", ".", stripSymbols: false);
        result1.ShouldBe(1234.56m);

        // Test EU format
        var result2 = ImportCellParser.ParseDecimal("1.234,56", ",", stripSymbols: false);
        result2.ShouldBe(1234.56m);

        // Test with strip symbols
        var result3 = ImportCellParser.ParseDecimal("$1,234.56", ".", stripSymbols: true);
        result3.ShouldBe(1234.56m);

        // Test precision preservation
        var result4 = ImportCellParser.ParseDecimal("1234.5678", ".", stripSymbols: false);
        result4.ShouldBe(1234.5678m);
    }
}

public class ImportFileExecutorColumnStatsTests
{
    [Fact]
    public void ColumnStats_AccumulatesIntegerValues_Correctly()
    {
        // Arrange
        var stats = new ImportFileExecutor.ColumnStats
        {
            ColumnName = "Quantity",
            Type = ImportColumnType.Integer
        };

        // Act
        ImportFileExecutor.UpdateStats(stats, 10);
        ImportFileExecutor.UpdateStats(stats, 20);
        ImportFileExecutor.UpdateStats(stats, 5);

        // Assert
        stats.Count.ShouldBe(3);
        stats.Sum.ShouldBe(35m);
        stats.Min.ShouldBe(5m);
        stats.Max.ShouldBe(20m);
    }

    [Fact]
    public void ColumnStats_AccumulatesDecimalValues_Correctly()
    {
        // Arrange
        var stats = new ImportFileExecutor.ColumnStats
        {
            ColumnName = "Amount",
            Type = ImportColumnType.Decimal
        };

        // Act
        ImportFileExecutor.UpdateStats(stats, 100.50m);
        ImportFileExecutor.UpdateStats(stats, 200.25m);
        ImportFileExecutor.UpdateStats(stats, 50.00m);

        // Assert
        stats.Count.ShouldBe(3);
        stats.Sum.ShouldBe(350.75m);
        stats.Min.ShouldBe(50.00m);
        stats.Max.ShouldBe(200.25m);
    }

    [Fact]
    public void ColumnStats_AccumulatesDateValues_Correctly()
    {
        // Arrange
        var stats = new ImportFileExecutor.ColumnStats
        {
            ColumnName = "Date",
            Type = ImportColumnType.Date
        };

        var date1 = new DateTime(2024, 1, 1);
        var date2 = new DateTime(2024, 1, 15);
        var date3 = new DateTime(2024, 1, 10);

        // Act
        ImportFileExecutor.UpdateStats(stats, date1);
        ImportFileExecutor.UpdateStats(stats, date2);
        ImportFileExecutor.UpdateStats(stats, date3);

        // Assert
        stats.Count.ShouldBe(3);
        stats.MinDate.ShouldBe(date1);
        stats.MaxDate.ShouldBe(date2);
        stats.Sum.ShouldBeNull();
        stats.Min.ShouldBeNull();
        stats.Max.ShouldBeNull();
    }

    [Fact]
    public void ColumnStats_NullValues_AreIgnored()
    {
        // Arrange
        var stats = new ImportFileExecutor.ColumnStats
        {
            ColumnName = "Amount",
            Type = ImportColumnType.Decimal
        };

        // Act
        ImportFileExecutor.UpdateStats(stats, 100m);
        ImportFileExecutor.UpdateStats(stats, (object?)null);
        ImportFileExecutor.UpdateStats(stats, 200m);

        // Assert
        stats.Count.ShouldBe(2);
        stats.Sum.ShouldBe(300m);
        stats.Min.ShouldBe(100m);
        stats.Max.ShouldBe(200m);
    }

    [Fact]
    public void ColumnStats_DifferentTypes_OnlyUpdateRelevantFields()
    {
        // Arrange
        var intStats = new ImportFileExecutor.ColumnStats { ColumnName = "Int", Type = ImportColumnType.Integer };
        var decStats = new ImportFileExecutor.ColumnStats { ColumnName = "Dec", Type = ImportColumnType.Decimal };
        var dateStats = new ImportFileExecutor.ColumnStats { ColumnName = "Date", Type = ImportColumnType.Date };
        var textStats = new ImportFileExecutor.ColumnStats { ColumnName = "Text", Type = ImportColumnType.Text };

        // Act
        ImportFileExecutor.UpdateStats(intStats, 10);
        ImportFileExecutor.UpdateStats(decStats, 10.5m);
        ImportFileExecutor.UpdateStats(dateStats, new DateTime(2024, 1, 1));
        ImportFileExecutor.UpdateStats(textStats, "hello");

        // Assert
        intStats.Count.ShouldBe(1);
        intStats.Sum.ShouldBe(10m);
        intStats.MinDate.ShouldBeNull();

        decStats.Count.ShouldBe(1);
        decStats.Sum.ShouldBe(10.5m);
        decStats.MinDate.ShouldBeNull();

        dateStats.Count.ShouldBe(1);
        dateStats.MinDate.ShouldBe(new DateTime(2024, 1, 1));
        dateStats.Sum.ShouldBeNull();

        textStats.Count.ShouldBe(1); // Count increments for all non-null values
        textStats.Sum.ShouldBeNull(); // But Sum/Min/Max remain null for Text type
    }
}