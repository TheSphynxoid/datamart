using System;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using DataMart.Infrastructure.Data;
using Moq;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.ImportFile;

public class AccessLogServiceTests
{
    private readonly Mock<IApplicationDbContext> _context;
    private readonly AccessLogService _service;

    public AccessLogServiceTests()
    {
        _context = new Mock<IApplicationDbContext>();
        _service = new AccessLogService(_context.Object);
    }

    [Fact]
    public async Task LogAsync_CreatesAccessLogEntry()
    {
        // Arrange
        var userId = "user-123";
        var action = AccessAction.ViewSchema;
        var correlationId = Guid.NewGuid().ToString();

        _context.Setup(c => c.AccessLogs.Add(It.IsAny<AccessLog>()));
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.LogAsync(userId, action, correlationId: correlationId);

        // Assert
        _context.Verify(c => c.AccessLogs.Add(It.Is<AccessLog>(l => 
            l.UserId == userId &&
            l.Action == action &&
            l.CorrelationId == correlationId &&
            l.MappingId == null &&
            l.BatchId == null)), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithMappingId_IncludesMappingId()
    {
        // Arrange
        var userId = "user-123";
        var action = AccessAction.CreateMapping;
        var mappingId = 42;

        _context.Setup(c => c.AccessLogs.Add(It.IsAny<AccessLog>()));
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.LogAsync(userId, action, mappingId: mappingId);

        // Assert
        _context.Verify(c => c.AccessLogs.Add(It.Is<AccessLog>(l => 
            l.MappingId == mappingId)), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithBatchId_IncludesBatchId()
    {
        // Arrange
        var userId = "user-123";
        var action = AccessAction.ExecuteImport;
        var batchId = 99;

        _context.Setup(c => c.AccessLogs.Add(It.IsAny<AccessLog>()));
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.LogAsync(userId, action, batchId: batchId);

        // Assert
        _context.Verify(c => c.AccessLogs.Add(It.Is<AccessLog>(l => 
            l.BatchId == batchId)), Times.Once);
    }

    [Fact]
    public async Task LogAsync_SetsTimestampToUtcNow()
    {
        // Arrange
        var userId = "user-123";
        var action = AccessAction.PreviewData;

        _context.Setup(c => c.AccessLogs.Add(It.IsAny<AccessLog>()));
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var before = DateTimeOffset.UtcNow;

        // Act
        await _service.LogAsync(userId, action);

        var after = DateTimeOffset.UtcNow;

        // Assert
        _context.Verify(c => c.AccessLogs.Add(It.Is<AccessLog>(l => 
            l.Timestamp >= before && l.Timestamp <= after)), Times.Once);
    }
}

public class AccessLogEntityTests
{
    [Fact]
    public void AccessLog_CanBeCreated_WithAllProperties()
    {
        // Arrange & Act
        var log = new AccessLog
        {
            UserId = "user-123",
            Action = AccessAction.CreateMapping,
            MappingId = 1,
            BatchId = 2,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = "corr-123"
        };

        // Assert
        log.UserId.ShouldBe("user-123");
        log.Action.ShouldBe(AccessAction.CreateMapping);
        log.MappingId.ShouldBe(1);
        log.BatchId.ShouldBe(2);
        log.CorrelationId.ShouldBe("corr-123");
        log.Timestamp.ShouldNotBe(default);
    }

    [Fact]
    public void AccessAction_HasExpectedValues()
    {
        Enum.GetValues<AccessAction>().ShouldBe(
            new[] 
            { 
                AccessAction.ViewSchema, 
                AccessAction.CreateMapping, 
                AccessAction.UpdateMapping, 
                AccessAction.ExecuteImport, 
                AccessAction.PreviewData 
            });
    }
}