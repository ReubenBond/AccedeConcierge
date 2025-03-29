namespace Accede.Service.Models;

[GenerateSerializer]
public class ReceiptData
{
    [Id(0)]
    public string ReceiptId { get; set; } = string.Empty;
    [Id(1)]
    public string MerchantName { get; set; } = string.Empty;
    [Id(2)]
    public DateTime TransactionDate { get; set; }

    [Id(3)]
    public float SubTotal { get; set; } = default;
    [Id(4)]
    public float Total { get; set; } = default;
    [Id(5)]
    public float Tax { get; set; } = default;
    [Id(6)]
    public Currency Currency { get; set; } = Currency.USD;
    [Id(7)]
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Miscellaneous;
    [Id(8)]
    public string Description { get; set; } = string.Empty;
    [Id(9)]
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