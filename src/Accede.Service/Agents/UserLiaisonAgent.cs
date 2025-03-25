using Accede.Service.Api;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.Distributed.AI.Agents;
#pragma warning disable 1998

namespace Accede.Service.Agents;

[Reentrant]
internal sealed partial class UserLiaisonAgent(
    ILogger<UserLiaisonAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient,
    //[FromKeyedServices("state")] IDurableValue<LiaisonState> state,
    [FromKeyedServices("pending")] IDurableQueue<ChatItem> pendingMessages,
    [FromKeyedServices("conversation-history")] IDurableList<ChatItem> conversationHistory)
    : ChatAgent(logger, chatClient, pendingMessages, conversationHistory), IUserLiaisonAgent
{
    private ChatOptions? _chatOptions;

    protected override ChatOptions ChatOptions => _chatOptions ??= new ChatOptions
    {
        Tools =
        [
            //DurableAIFunctionFactory.Create(UpdateDraftResponse, FunctionFactoryOptions),
        ],
    };

    public async ValueTask AddUserMessageAsync(ChatItem message, CancellationToken cancellationToken = default) => await AddMessageAsync(message, cancellationToken);

    protected override async Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken)
    {
        var userName = this.GetPrimaryKeyString();
        return
            [
                new SystemPrompt(
                    $"""
                    You are a travel agent, liaising with a user, '{this.GetPrimaryKeyString()}'.
                    You are helping them to:
                    - book travel
                    - submit expense reports
                    - provide 
                    """)
            ];
    }

    protected override Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken)
    {
        // Check whether the goal conditions are met.
        // If not, provide more instruction to the language model to guide it towards that goal.
        return Task.FromResult<List<ChatItem>>([]);
    }
}

internal interface IUserLiaisonAgent : IGrainWithStringKey
{
    ValueTask AddUserMessageAsync(ChatItem message, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);
    ValueTask<List<ChatItem>> GetMessagesAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatItem> GetChatItemsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ClientMessageFragment> SubscribeAsync(int startIndex, CancellationToken cancellationToken = default);
}

