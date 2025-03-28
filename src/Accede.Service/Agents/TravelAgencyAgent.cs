using Microsoft.Extensions.AI;
using Orleans.Journaling;
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

                """
            ];
        return Task.FromResult(systemPrompt);
    }

    protected override Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
