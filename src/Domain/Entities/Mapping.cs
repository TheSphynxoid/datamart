namespace DataMart.Domain.Entities;

[Exclude("This is a System Entity")]
public class Mapping : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetEntity { get; set; } = string.Empty;
    public List<ColumnMapping> Columns { get; set; } = [];
    public bool IsDefault { get; set; }
    
    // CreatedBy is inherited from BaseAuditableEntity (FC-3 Maker/Checker)
}

public class ColumnMapping
{
    public string ColumnName { get; set; } = string.Empty;
    public string? ColumnType { get; set; }
    public string TargetColName { get; set; } = string.Empty;
    /// <summary>User-declared "obligatoire": this column must have a value on every row.</summary>
    public bool IsRequired { get; set; }
    public string? DateFormat { get; set; }
    public string? TimeFormat { get; set; }
    public string? DecimalSeparator { get; set; }
    public bool StripSymbols { get; set; }
    public string? BoolTrueValue { get; set; }
    public string? BoolFalseValue { get; set; }
}
