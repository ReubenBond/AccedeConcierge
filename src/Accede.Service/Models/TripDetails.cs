namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
[Description("Contains detailed information about a trip booking")]
public record TripDetails(
    [Description("Unique identifier for the booking")] string BookingId,
    [Description("Reference to the original trip request")] string RequestId,
    [Description("Current status of the booking")] BookingStatus Status,
    [Description("List of flights included in the booking")] IReadOnlyList<Flight> Flights,
    [Description("Hotel reservation details if applicable")] Hotel? Hotel,
    [Description("Car rental details if applicable")] CarRental? Car,
    [Description("Total cost of the entire trip")] float TotalCost,
    [Description("Dictionary mapping service types to confirmation numbers")] IReadOnlyDictionary<string, string> ConfirmationNumbers,
    [Description("Full trip itinerary in text format")] string Itinerary,
    [Description("History of changes made to the booking")] List<TripModification>? Changes = null
);

[GenerateSerializer]
[Description("Status of a travel booking")]
public enum BookingStatus
{
    [Description("Booking is temporarily held but not confirmed")]
    Reserved,
    [Description("Booking is fully confirmed with all providers")]
    Confirmed,
    [Description("Some parts of the booking are confirmed while others are pending")]
    PartiallyConfirmed,
    [Description("Booking process has failed")]
    Failed,
    [Description("Booking has been cancelled")]
    Cancelled,
    [Description("Trip has been completed")]
    Completed
}

[GenerateSerializer]
[Description("Represents a change or modification made to an existing trip")]
public record TripModification(
    [Description("Unique identifier for the modification")] string ModificationId,
    [Description("Date and time when the modification was made. Use ISO 8601 format.")] DateTime ModifiedTime,
    [Description("Type of modification (e.g., 'Change Date', 'Cancel Flight')")] string ModificationType,
    [Description("Human-readable description of the change")] string Description,
    [Description("Change in cost resulting from this modification")] float? CostChange,
    [Description("New confirmation number if applicable")] string? NewConfirmationNumber = null
);

[GenerateSerializer]
[Description("Details about the approval of a trip request")]
public record ApprovalDetails(
    [Description("Reference to the approved trip request")] string RequestId,
    [Description("ID of the person who approved the request")] string ApproverId,
    [Description("Date and time when the approval was granted. Use ISO 8601 format.")] DateTime ApprovalTime,
    [Description("Maximum budget approved for the trip")] float ApprovedBudget,
    [Description("Additional notes from the approver")] string? Notes = null
);

[GenerateSerializer]
[Description("Information about why a trip request was rejected")]
public record RejectionReason(
    [Description("Reference to the rejected trip request")] string RequestId,
    [Description("ID of the person who reviewed and rejected the request")] string ReviewerId,
    [Description("Date and time when the rejection occurred")] DateTime RejectionTime,
    [Description("Primary reason for rejection")] string Reason,
    [Description("Additional explanation or comments")] string? Notes = null
);