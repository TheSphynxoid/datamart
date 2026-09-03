namespace DataMart.Domain.Entities;

/// <summary>
/// Test entity mirroring the customer-transactions Excel file used for manual testing.
/// Column names match the Excel headers for automatic mapping.
/// </summary>
public class CustomerTransaction : BaseAuditableEntity
{
    public string TransactionID { get; set; } = string.Empty;
    public string CustomerID { get; set; } = string.Empty;
    public DateTime CustomerDOB { get; set; }
    public string? CustGender { get; set; }
    public string? CustLocation { get; set; }
    public decimal CustAccountBalance { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? TransactionTime { get; set; }
    public decimal TransactionAmount { get; set; }
}
