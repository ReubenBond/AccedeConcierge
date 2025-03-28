namespace Accede.Service.Models;

[GenerateSerializer]
public class ReceiptData
{
    public string ReceiptId { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    
    public float SubTotal { get; set; } = default;
    public float Total { get; set; } = default;
    public float Tax { get; set; } = default;
    public Currency Currency { get; set; } = Currency.USD;
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Miscellaneous;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new List<string>();
}

[GenerateSerializer]
public enum ExpenseCategory
{
    Travel,
    Food,
    Accommodation,
    Transportation,
    Miscellaneous
}

[GenerateSerializer]
public enum Currency
{
    USD,
    EUR,
    GBP,
    JPY,
    AUD,
    CAD,
    CNY,
    INR,
    MXN,
    BRL
}