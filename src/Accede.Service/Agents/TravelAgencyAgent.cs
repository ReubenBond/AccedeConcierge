using Accede.Service.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Distributed.AI.Agents;

namespace Accede.Service.Agents;

internal interface ITravelAgencyAgent : IChatAgent
{
}

internal sealed class TravelAgencyAgent(
    ILogger<TravelAgencyAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient) : ChatAgent(logger, chatClient)
{
    protected override Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken)
    {
        List<ChatItem> systemPrompt =
            [
                new SystemPrompt(
                """
                You are a travel agent helping a concierge to book travel on behalf of their customer.
                Use the 'SearchFlights' tool to find flights.
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
    public override string Type => "candidate-itinerary";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => false;
}
