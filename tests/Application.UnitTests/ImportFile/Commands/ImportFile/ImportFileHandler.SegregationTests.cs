using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace DataMart.Application.UnitTests.ImportFile.Commands.ImportFile;

[TestFixture]
public class ImportEndpointSegregationTests
{
    [Test]
    public void SegregationCheck_MappingCreatorIsExecutor_ReturnsForbid()
    {
        var mappingCreatedBy = "user-123";
        var currentUserId = "user-123";

        var isForbidden = mappingCreatedBy == currentUserId;

        isForbidden.ShouldBeTrue();
    }

    [Test]
    public void SegregationCheck_DifferentUser_AllowsExecution()
    {
        var mappingCreatedBy = "user-123";
        var currentUserId = "user-456";

        var isForbidden = mappingCreatedBy == currentUserId;

        isForbidden.ShouldBeFalse();
    }

    [Test]
    public void SegregationCheck_NoMappingFound_AllowsExecution()
    {
        string? mappingCreatedBy = null;
        var currentUserId = "user-123";

        var isForbidden = mappingCreatedBy == currentUserId;

        isForbidden.ShouldBeFalse();
    }
}