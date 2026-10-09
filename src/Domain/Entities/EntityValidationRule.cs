using DataMart.Domain.Enums;

namespace DataMart.Domain.Entities;

/// <summary>
/// Per-entity import validation rule (system configuration, not importable data).
/// All active rules for an entity are AND-ed: a row passes iff every active rule
/// passes. A field with no active rule imposes no constraint; a nullish cell
/// value skips the rule (requiredness is enforced by the schema, not by rules).
/// The right-hand side is either a <see cref="Value"/> constant or a
/// clock-resolved <see cref="Function"/> (evaluated as of import time, UTC);
/// <see cref="Values"/> carries the membership set for <c>In</c>/<c>NotIn</c>.
/// </summary>
[Exclude("Per-entity import validation rules; system configuration, not importable data.")]
public class EntityValidationRule : BaseAuditableEntity
{
    public string TargetEntity { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public RuleOperator Operator { get; set; }
    public RuleValueSource ValueSource { get; set; }
    public string? Value { get; set; }
    public List<string> Values { get; set; } = [];
    public RuleFunction? Function { get; set; }
    public int? FunctionArg { get; set; }
    public string? Message { get; set; }
    public bool IsActive { get; set; } = true;
}
