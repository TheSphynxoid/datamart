using System.Runtime.CompilerServices;
using AutoMapper;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.Mappings;
using DataMart.Domain.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataMart.Application.UnitTests.Common.Mappings;

public class MappingTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MapperConfiguration _configuration;
    private readonly IMapper _mapper;

    public MappingTests()
    {
        // Minimal logger factory for tests
        _loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        _configuration = new MapperConfiguration(cfg =>
            cfg.AddMaps(typeof(IApplicationDbContext).Assembly),
            loggerFactory: _loggerFactory);

        _mapper = _configuration.CreateMapper();
    }

    [Fact]
    public void ShouldHaveValidConfiguration()
    {
        _configuration.AssertConfigurationIsValid();
    }

    [Theory]
    [InlineData(typeof(Mapping), typeof(MappingDto))]
    public void ShouldSupportMappingFromSourceToDestination(Type source, Type destination)
    {
        var instance = GetInstanceOf(source);

        _mapper.Map(instance, source, destination);
    }

    private static object GetInstanceOf(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) != null)
            return Activator.CreateInstance(type)!;

        // Type without parameterless constructor
        return RuntimeHelpers.GetUninitializedObject(type);
    }


    public void Dispose()
    {
        _loggerFactory.Dispose();
    }
}
