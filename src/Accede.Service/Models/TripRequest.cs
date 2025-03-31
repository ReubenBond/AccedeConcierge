namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
public record TripRequest(
    string RequestId,
    TripOption TripOption,
    string? AdditionalNotes = null);

[GenerateSerializer]
[Description("Status of a trip request in its approval and booking lifecycle")]
public enum TripRequestStatus
{
    [Description("Request is awaiting approval")]
    Pending,
    [Description("Request has been approved")]
    Approved,
    [Description("Request has been rejected")]
    Rejected,
    [Description("Trip request has been cancelled")]
    Cancelled
}

[GenerateSerializer]
[Description("Result of processing a trip request")]
public record TripRequestResult(
    [Description("Reference to the original trip request")] string RequestId,
    [Description("Current status of the trip request")] TripRequestStatus Status,
    [Description("Notes from the approval process")] string? ApprovalNotes,
    [Description("Date and time when the request was processed. Use ISO 8601 format.")] DateTime ProcessedTime);
