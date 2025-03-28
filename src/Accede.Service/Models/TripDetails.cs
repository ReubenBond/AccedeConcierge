namespace Accede.Service.Models;

[GenerateSerializer]
public record TripDetails(
    string BookingId,
    string RequestId,
    BookingStatus Status,
    IReadOnlyList<Flight> Flights,
    Hotel? Hotel,
    CarRental? Car,
    float TotalCost,
    IReadOnlyDictionary<string, string> ConfirmationNumbers,
    string Itinerary,
    List<TripModification>? Changes = null
);

[GenerateSerializer]
public enum BookingStatus
{
    Reserved,
    Confirmed,
    PartiallyConfirmed,
    Failed,
    Cancelled,
    Completed
}

[GenerateSerializer]
public record TripModification(
    string ModificationId,
    DateTime ModifiedTime,
    string ModificationType,
    string Description,
    float? CostChange,
    string? NewConfirmationNumber = null
);

[GenerateSerializer]
public record ApprovalDetails(
    string RequestId,
    string ApproverId,
    DateTime ApprovalTime,
    float ApprovedBudget,
    string? Notes = null
);

[GenerateSerializer]
public record RejectionReason(
    string RequestId,
    string ReviewerId,
    DateTime RejectionTime,
    string Reason,
    string? Notes = null
);