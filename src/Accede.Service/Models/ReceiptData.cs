namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
[Description("Represents receipt data for an expense transaction")]
public record class ReceiptData(
    [Description("Date and time when the transaction occurred")] DateTime TransactionDate,
    [Description("Pre-tax amount of the transaction")] float SubTotal,
    [Description("Total amount of the transaction including taxes")] float Total,
    [Description("Tax amount applied to the transaction")] float Tax,
    [Description("Currency used for the transaction")] Currency Currency,
    [Description("Category of the expense")] ExpenseCategory Category,
    [Description("Unique identifier for the receipt")] string? ReceiptId = null,
    [Description("Name of the merchant or vendor")] string? MerchantName = null,
    [Description("Additional details about the transaction")] string? Description = null,
    [Description("List of tags used for categorizing and searching")] List<string>? Tags = null);

[GenerateSerializer]
public enum ExpenseCategory
{
    [Description("Travel-related expenses")]
    Travel,
    [Description("Food and dining expenses")]
    Food,
    [Description("Lodging and accommodation expenses")]
    Accommodation,
    [Description("Transportation and commuting expenses")]
    Transportation,
    [Description("Miscellaneous expenses not fitting other categories")]
    Miscellaneous
}

[GenerateSerializer]
public enum Currency
{
    [Description("United States Dollar")]
    USD,
    [Description("Euro")]
    EUR,
    [Description("British Pound Sterling")]
    GBP,
    [Description("Japanese Yen")]
    JPY,
    [Description("Australian Dollar")]
    AUD,
    [Description("Canadian Dollar")]
    CAD,
    [Description("Chinese Yuan Renminbi")]
    CNY,
    [Description("Indian Rupee")]
    INR,
    [Description("Mexican Peso")]
    MXN,
    [Description("Brazilian Real")]
    BRL
}