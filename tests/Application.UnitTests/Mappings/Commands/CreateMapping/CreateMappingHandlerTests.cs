using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Application.Mappings;
using DataMart.Domain.Entities;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace DataMart.Application.UnitTests.Mappings.Commands.CreateMapping;

[TestFixture]
public class CreateMappingHandlerTests
{
    private Mock<IApplicationDbContext> _context = null!;
    private Mock<IUser> _user = null!;
    private CreateMappingCommandHandler _handler = null!;
    private int _nextId = 1;

    [SetUp]
    public void Setup()
    {
        _context = new Mock<IApplicationDbContext>();
        _user = new Mock<IUser>();
        _handler = new CreateMappingCommandHandler(_context.Object, _user.Object);
        _nextId = 1;
    }

    [Test]
    public async Task Handle_ValidCommand_CreatesMappingWithCreatedBy()
    {
        // Arrange
        _user.Setup(u => u.Id).Returns("user-123");
        _context.Setup(c => c.Mappings.Add(It.IsAny<Mapping>()))
            .Callback<Mapping>(m => m.Id = _nextId++);
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var command = new CreateMappingCommand(
            "Test Mapping",
            "Description",
            "CustomerTransaction",
            new List<ColumnMappingDto>
            {
                new ColumnMappingDto("Amount", "Decimal", "Amount", null, null, null, false, null, null)
            });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldBeGreaterThan(0);
        _context.Verify(c => c.Mappings.Add(It.Is<Mapping>(m => 
            m.Name == "Test Mapping" &&
            m.TargetEntity == "CustomerTransaction" &&
            m.CreatedBy == "user-123")), Times.Once);
    }

    [Test]
    public async Task Handle_NoUserId_UsesUnknown()
    {
        // Arrange
        _user.Setup(u => u.Id).Returns((string?)null);
        _context.Setup(c => c.Mappings.Add(It.IsAny<Mapping>()))
            .Callback<Mapping>(m => m.Id = _nextId++);
        _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var command = new CreateMappingCommand(
            "Test Mapping",
            null,
            "CustomerTransaction",
            new List<ColumnMappingDto>
            {
                new ColumnMappingDto("Amount", "Decimal", "Amount", null, null, null, false, null, null)
            });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldBeGreaterThan(0);
        _context.Verify(c => c.Mappings.Add(It.Is<Mapping>(m => 
            m.CreatedBy == "unknown")), Times.Once);
    }
}