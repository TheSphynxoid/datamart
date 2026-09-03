namespace DataMart.Domain.Enums;

/// <summary>
/// How the import pipeline treats a row whose required (non-nullable) field is empty.
/// </summary>
public enum EmptyFieldMode
{
    Throw,
    SkipRow,
    Default
}
