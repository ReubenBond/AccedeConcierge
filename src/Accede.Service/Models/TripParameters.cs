namespace Accede.Service.Models;

[GenerateSerializer]
public record TripParameters(
    Location Origin,
    Location Destination,
    DateTime StartDate,
    DateTime EndDate,
    TravelRequirements Requirements,
    TravelPreferences? Preferences = null
);

[GenerateSerializer]
public record Location(
    string City,
    string? State,
    string Country,
    string? AirportCode = null
);

[GenerateSerializer]
public record TravelRequirements(
    bool NeedsFlight,
    bool NeedsHotel,
    bool NeedsCarRental,
    int NumberOfTravelers = 1
);

[GenerateSerializer]
public record TravelPreferences(
    PreferredAirline? PreferredAirline = null,
    HotelPreferences? HotelPreferences = null,
    CarRentalPreferences? CarPreferences = null
);

[GenerateSerializer]
public record PreferredAirline(
    string Airline,
    string? SeatPreference = null
);

[GenerateSerializer]
public record HotelPreferences(
    string? Chain = null,
    string? RoomType = null
);

[GenerateSerializer]
public record CarRentalPreferences(
    string? Company = null,
    string? CarType = null
);
