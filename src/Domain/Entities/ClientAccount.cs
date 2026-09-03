namespace DataMart.Domain.Entities;

/// <summary>
/// Comprehensive test entity for the import pipeline.
/// Covers all CLR types that trigger format options: dates, decimals, booleans, integers, text.
/// </summary>
public class ClientAccount : BaseAuditableEntity
{
    public string AccountNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal InterestRate { get; set; }
    public bool IsActive { get; set; }
    public DateTime OpenedDate { get; set; }
    public DateOnly? LastActivityDate { get; set; }
    public int TransactionCount { get; set; }
    public string? Notes { get; set; }
}
