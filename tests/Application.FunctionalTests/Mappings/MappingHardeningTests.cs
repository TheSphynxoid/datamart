using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Application.FunctionalTests.Infrastructure;
using Shouldly;
using Xunit;

namespace DataMart.Application.FunctionalTests.Mappings;

/// <summary>
/// The mapping's target column names become SQL identifiers in the COPY
/// statement, so the create/update validators must reject anything that is not
/// a plain identifier. These tests pin that contract end-to-end through the
/// MediatR pipeline (real validator, real database) — a crafted TargetColName
/// must be refused before it can ever reach BulkImporter.
/// </summary>
[Collection("FunctionalTests")]
public class MappingHardeningTests
{
    private const string TargetEntity = "ClientAccounts";

    [Fact]
    public async Task CreateMapping_WithQuoteInTargetColumn_IsRejected()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();

        var command = new CreateMappingCommand(
            "evil-mapping",
            null,
            TargetEntity,
            new List<ColumnMappingDto>
            {
                new("AccountNumber", null, "AccountNumber\"; DROP TABLE \"ClientAccounts\"; --", null, null, null, false, null, null)
            });

        var exception = await Should.ThrowAsync<AppValidationException>(
            async () => await TestApp.SendAsync(command));

        exception.Errors.Values
            .SelectMany(v => v)
            .ShouldContain(m => m.Contains("letters, digits and underscores"));
    }

    [Fact]
    public async Task CreateMapping_WithNonIdentifierCharacters_IsRejected()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();

        foreach (var bad in new[] { "Balance; --", "two words", "col(x)", "a-b" })
        {
            var command = new CreateMappingCommand(
                $"mapping-for-{bad}",
                null,
                TargetEntity,
                new List<ColumnMappingDto>
                {
                    new("Balance", null, bad, null, null, null, false, null, null)
                });

            var exception = await Should.ThrowAsync<AppValidationException>(
                async () => await TestApp.SendAsync(command));

            exception.Errors.Values
                .SelectMany(v => v)
                .ShouldContain(m => m.Contains("letters, digits and underscores"));
        }
    }

    [Fact]
    public async Task CreateMapping_WithPlainIdentifiers_IsAccepted()
    {
        await TestApp.ResetState();
        await TestApp.RunAsDefaultUserAsync();

        var command = new CreateMappingCommand(
            "hardening-ok",
            null,
            TargetEntity,
            new List<ColumnMappingDto>
            {
                new("Balance", null, "Balance", null, null, null, false, null, null),
                new("AccountNumber", null, "_account2_number", null, null, null, false, null, null),
            });

        var mappingId = await TestApp.SendAsync(command);
        mappingId.ShouldBeGreaterThan(0);
    }
}
