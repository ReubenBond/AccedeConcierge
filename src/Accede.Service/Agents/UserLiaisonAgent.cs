using Accede.Service.Api;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
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
        return
            [
                new SystemPrompt(
                    $"""

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

