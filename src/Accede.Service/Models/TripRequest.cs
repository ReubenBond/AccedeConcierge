namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
[Description("Request for booking a business trip")]
public record TripRequest(
    [Description("Unique identifier for the trip request")] string RequestId,
    [Description("Travel requirements and specifications")] TripParameters Parameters,
    [Description("Expected budget for the trip")] float EstimatedBudget,
    [Description("Any supplementary information or special requests")] string AdditionalNotes
);

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
    [Description("Approved request is being processed")]
    InProgress,
    [Description("Trip has been successfully completed")]
    Completed,
    [Description("Trip request has been cancelled")]
    Cancelled
}

[GenerateSerializer]
[Description("Result of processing a trip request")]
public record TripRequestResult(
    [Description("Reference to the original trip request")] string RequestId,
    [Description("Current status of the trip request")] TripRequestStatus Status,
    [Description("Notes from the approval process")] string? ApprovalNotes,
    [Description("Date and time when the request was processed. Use ISO 8601 format.")] DateTime ProcessedTime
);
