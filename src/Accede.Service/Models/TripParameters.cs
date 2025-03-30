namespace Accede.Service.Models;

using System.ComponentModel;

[GenerateSerializer]
[Description("Parameters defining a trip's basic requirements")]
public record TripParameters(
    [Description("Origin location for the trip")] Location Origin,
    [Description("Destination location for the trip")] Location Destination,
    [Description("Starting date of the trip. Use ISO 8601 format.")] DateTime StartDate,
    [Description("Ending date of the trip. Use ISO 8601 format.")] DateTime EndDate,
    [Description("Essential travel requirements")] TravelRequirements Requirements,
    [Description("Optional travel preferences")] TravelPreferences? Preferences = null
);

[GenerateSerializer]
[Description("Geographic location information")]
public record Location(
    [Description("City name")] string City,
    [Description("State or region name if applicable")] string? State,
    [Description("Country name")] string Country,
    [Description("Three-letter IATA airport code if applicable")] string? AirportCode = null
);

[GenerateSerializer]
[Description("Essential requirements for a trip")]
public record TravelRequirements(
    [Description("Whether flight booking is required")] bool NeedsFlight,
    [Description("Whether hotel booking is required")] bool NeedsHotel,
    [Description("Whether car rental is required")] bool NeedsCarRental,
    [Description("Number of people traveling")] int NumberOfTravelers = 1
);

[GenerateSerializer]
[Description("Optional preferences for travel arrangements")]
public record TravelPreferences(
    [Description("Preferred airline information if any")] PreferredAirline? PreferredAirline = null,
    [Description("Hotel preferences if any")] HotelPreferences? HotelPreferences = null,
    [Description("Car rental preferences if any")] CarRentalPreferences? CarPreferences = null
);

[GenerateSerializer]
[Description("Information about preferred airline")]
public record PreferredAirline(
    [Description("Name of the preferred airline")] string Airline,
    [Description("Preferred seating arrangement if any")] string? SeatPreference = null
);

[GenerateSerializer]
[Description("Preferences for hotel accommodations")]
public record HotelPreferences(
    [Description("Preferred hotel chain if any")] string? Chain = null,
    [Description("Preferred room type if any")] string? RoomType = null
);

[GenerateSerializer]
[Description("Preferences for car rental")]
public record CarRentalPreferences(
    [Description("Preferred rental company if any")] string? Company = null,
    [Description("Preferred vehicle type if any")] string? CarType = null
);
