using Accede.Service.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp();

app.Run();

[McpServerToolType]
public sealed class FlightTool
{
    [McpServerTool, Description("Returns a list of available flights.")]
    public static async ValueTask<List<Flight>> SearchFlightsAsync(CancellationToken cancellationToken)
    {
        try
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "flights.json");
            string jsonData = await File.ReadAllTextAsync(filePath, cancellationToken);

            var flights = JsonSerializer.Deserialize<List<Flight>>(jsonData, JsonSerializerOptions.Web);
            return flights ?? [];
        }
        catch //(Exception ex)
        {
            //logger.LogError(ex, "Error loading flights from JSON file");
            return [];
        }
    }
}