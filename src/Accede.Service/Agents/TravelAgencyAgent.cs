using Accede.Service.Models;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Distributed.AI.Agents;
using System.Text.Json;

namespace Accede.Service.Agents;

internal interface ITravelAgencyAgent : IChatAgent
{
}

internal sealed class TravelAgencyAgent(
    ILogger<TravelAgencyAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient) : ChatAgent(logger, chatClient), ITravelAgencyAgent
{
    protected override Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken)
    {
        List<ChatItem> systemPrompt =
            [
                new SystemPrompt(
                """
                You are a travel agent helping a concierge to book travel on behalf of their customer.
                Use the 'SearchFlights' tool to find flights.
                When you have created candidate trips, use the the 'ProposeCandidateTripPlans' tool to inform the concierge.
                If you don't have real data (eg, for hotel bookings or car rentals), you can make up the data - this is just role playing, so it's ok.
                """)
            ];
        return Task.FromResult(systemPrompt);
    }

    protected override Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<List<ChatItem>>([]);
    }

    [Tool, Description("Proposes an array of candidate trip plans for a customer.")]
    public async ValueTask ProposeCandidateTripPlansAsync(List<TripOption> options, CancellationToken cancellationToken)
    {
        AddStatusMessage(new CandidateItineraryChatItem("Here are trips matching your requirements.", options));
        await WriteStateAsync(cancellationToken);
    }

    [Tool, Description("Returns a list of available flights.")]
    public async ValueTask<List<Flight>> SearchFlightsAsync(CancellationToken cancellationToken)
    {
        try
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "flights.json");
            string jsonData = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            var flights = JsonSerializer.Deserialize<List<Flight>>(jsonData, JsonSerializerOptions.Web);
            return flights ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading flights from JSON file");
            return [];
        }
    }
}

[GenerateSerializer]
internal sealed class CandidateItineraryChatItem : ChatItem
{
    [SetsRequiredMembers]
    public CandidateItineraryChatItem(string text, List<TripOption> options) : base(text)
    {
        Id = Guid.NewGuid().ToString();
        Options = options;
    }

    [Id(0)]
    public List<TripOption> Options { get; }
    public override string Type => "assistant";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => false;
    public override ChatMessage? ToChatMessage()
    {
        var text =
            $"""
            Here are the trips matching your requirements:

            {string.Join("\n", Options.Select(option => JsonSerializer.Serialize(option, JsonSerializerOptions.Web)))}
            """;
        return new ChatMessage(ChatRole.User, text);
    }
}
