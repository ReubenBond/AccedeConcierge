namespace Accede.Service.Models;

[GenerateSerializer]
public record TripRequest(
    string RequestId,
    TripParameters Parameters,
    float EstimatedBudget,
    string AdditionalNotes
);

[GenerateSerializer]
public enum TripRequestStatus
{
    Pending,
    Approved,
    Rejected,
    InProgress,
    Completed,
    Cancelled
}

[GenerateSerializer]
public record TripRequestResult(
    string RequestId,
    TripRequestStatus Status,
    string? ApprovalNotes,
    DateTime ProcessedTime
);
