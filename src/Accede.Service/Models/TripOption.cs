namespace Accede.Service.Models;

[GenerateSerializer]
public record TripOption(
    string OptionId,
    IReadOnlyList<Flight> Flights,
    Hotel? Hotel,
    CarRental? Car,
    decimal TotalCost,
    string Description
);

[GenerateSerializer]
public record Flight(
    string FlightNumber,
    string Airline,
    string Origin,
    string Destination,
    DateTime DepartureTime,
    DateTime ArrivalTime,
    float Price,
    string Duration,
    bool HasLayovers,
    string? CabinClass = null
);

[GenerateSerializer]
public record Hotel(
    string PropertyName,
    string Chain,
    string Address,
    DateTime CheckIn,
    DateTime CheckOut,
    int NightCount,
    float PricePerNight,
    float TotalPrice,
    string RoomType,
    bool BreakfastIncluded
);

[GenerateSerializer]
public record CarRental(
    string Company,
    string CarType,
    string PickupLocation,
    string DropoffLocation,
    DateTime PickupTime,
    DateTime DropoffTime,
    float DailyRate,
    float TotalPrice,
    bool UnlimitedMileage
);
