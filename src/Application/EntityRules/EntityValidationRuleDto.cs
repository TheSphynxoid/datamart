using AutoMapper;
using DataMart.Domain.Entities;

namespace DataMart.Application.EntityRules;

public record class EntityValidationRuleDto(
    int Id,
    string TargetEntity,
    string Field,
    string Operator,
    string ValueSource,
    string? Value,
    List<string> Values,
    string? Function,
    int? FunctionArg,
    string? Message,
    bool IsActive)
{
    private class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Enums cross the wire as strings: plain, stable contract for the
            // generated client (no NSwag enum-shape questions). Materialise
            // before mapping (see handler): ToString() is client-evaluated.
            CreateMap<EntityValidationRule, EntityValidationRuleDto>()
                .ForMember(d => d.Operator, o => o.MapFrom(s => s.Operator.ToString()))
                .ForMember(d => d.ValueSource, o => o.MapFrom(s => s.ValueSource.ToString()))
                .ForMember(d => d.Function, o => o.MapFrom(s => s.Function == null ? null : s.Function.ToString()));
        }
    }
}
