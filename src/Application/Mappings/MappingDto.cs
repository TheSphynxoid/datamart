using AutoMapper;
using DataMart.Domain.Entities;

namespace DataMart.Application.Mappings;

public record class MappingDto(int Id, string Name, string? Description, string TargetEntity, List<ColumnMappingDto> Columns, bool IsDefault, DateTimeOffset Created)
{
    private class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Mapping, MappingDto>();
            CreateMap<ColumnMapping, ColumnMappingDto>();
            CreateMap<ColumnMappingDto, ColumnMapping>();
        }
    }
}

public record class ColumnMappingDto(string ColumnName, string? ColumnType, string TargetColName, string? DateFormat, string? DecimalSeparator, bool StripSymbols, string? BoolTrueValue,
    string? BoolFalseValue);
