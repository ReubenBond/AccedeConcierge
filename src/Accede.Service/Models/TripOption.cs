namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
[Description("A travel option presented to the user for selection")]
public record TripOption(
    [Description("Unique identifier for this travel option")] string OptionId,
    [Description("List of flights included in this option")] IReadOnlyList<Flight> Flights,
    [Description("Hotel details if included in this option")] Hotel? Hotel,
    [Description("Car rental details if included in this option")] CarRental? Car,
    [Description("Total cost for all components of this travel option")] decimal TotalCost,
    [Description("Human-readable description of this travel option")] string Description
);

[GenerateSerializer]
[Description("Information about a flight segment")]
public record Flight(
    [Description("Unique flight identifier")] string FlightNumber,
    [Description("Name of the operating airline")] string Airline,
    [Description("Departure airport code or city")] string Origin,
    [Description("Arrival airport code or city")] string Destination,
    [Description("Scheduled departure time")] DateTime DepartureTime,
    [Description("Scheduled arrival time")] DateTime ArrivalTime,
    [Description("Price of this flight segment")] float Price,
    [Description("Duration of the flight")] string Duration,
    [Description("Whether this flight has intermediate stops")] bool HasLayovers,
    [Description("Class of service (Economy, Business, First)")] string? CabinClass = null
);

[GenerateSerializer]
[Description("Details about a hotel accommodation")]
public record Hotel(
    [Description("Name of the hotel property")] string PropertyName,
    [Description("Hotel chain or brand name")] string Chain,
    [Description("Physical location of the hotel")] string Address,
    [Description("Date and time of check-in")] DateTime CheckIn,
    [Description("Date and time of check-out")] DateTime CheckOut,
    [Description("Number of nights of stay")] int NightCount,
    [Description("Cost per night")] float PricePerNight,
    [Description("Total price including all nights and fees")] float TotalPrice,
    [Description("Type of room reserved")] string RoomType,
    [Description("Whether breakfast is included in the rate")] bool BreakfastIncluded
);

[GenerateSerializer]
[Description("Details about a car rental")]
public record CarRental(
    [Description("Name of the rental company")] string Company,
    [Description("Type or model of vehicle")] string CarType,
    [Description("Location where car will be picked up")] string PickupLocation,
    [Description("Location where car will be returned")] string DropoffLocation,
    [Description("Date and time of vehicle pickup")] DateTime PickupTime,
    [Description("Date and time of vehicle return")] DateTime DropoffTime,
    [Description("Cost per day for the rental")] float DailyRate,
    [Description("Total price including all days and fees")] float TotalPrice,
    [Description("Whether rental includes unlimited mileage")] bool UnlimitedMileage
);
