using Accede.Service.Models;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
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
    [FromKeyedServices("large")] IChatClient chatClient,
    ILoggerFactory loggerFactory) : ChatAgent(logger, chatClient), ITravelAgencyAgent
{
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        McpClientOptions mcpClientOptions = new()
        { ClientInfo = new() { Name = "AspNetCoreSseClient", Version = "1.0.0" } };

        // Ideally would use ServiceDiscovery here
        var serviceName = "mcpserver";
        var name = $"services__{serviceName}__https__0";
        var url = Environment.GetEnvironmentVariable(name) + "/sse";

        var clientTransport = new SseClientTransport(new()
        {
            Name = "AspNetCoreSse",
            Endpoint = new Uri(url),
        }, loggerFactory);

        var mcpClient = McpClientFactory.CreateAsync(clientTransport, mcpClientOptions, loggerFactory).GetAwaiter().GetResult();
        var tools = mcpClient.ListToolsAsync().GetAwaiter().GetResult();
        var currentTools = ChatOptions.Tools ?? [];
        ChatOptions.Tools = [.. tools, .. currentTools];

        return base.OnActivateAsync(cancellationToken);
    }

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
                If you are asked about nearby facilities (eg, restaurants, attractions), just say yes - it's fine, this is all hypothetical.
                To be extra clear: this is just role-playing and you are expected to make things up to please the customer.
                But do not break character: you are acting as a travel agent!
                """)
            ];

        return Task.FromResult(systemPrompt);
    }

    [Tool, Description("Proposes an array of candidate trip plans for a customer.")]
    public async ValueTask ProposeCandidateTripPlansAsync(List<TripOption> options, CancellationToken cancellationToken)
    {
        AddMessage(new CandidateItineraryChatItem("Here are trips matching your requirements.", options));
        await WriteStateAsync(cancellationToken);
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
    public override string Type => "candidate-itineraries";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
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
