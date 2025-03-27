using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ComponentModel;
using System.Distributed.AI.Agents;
using System.Distributed.DurableTasks;
#pragma warning disable 1998

namespace Accede.Service.Agents;

[Reentrant]
internal sealed partial class UserLiaisonAgent(
    ILogger<UserLiaisonAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient,
    [FromKeyedServices("pending")] IDurableQueue<ChatItem> pendingMessages,
    [FromKeyedServices("conversation-history")] IDurableList<ChatItem> conversationHistory)
    : ChatAgent(logger, chatClient, pendingMessages, conversationHistory), IUserLiaisonAgent
{
    public async ValueTask AddUserMessageAsync(ChatItem message, CancellationToken cancellationToken = default) => await AddMessageAsync(message, cancellationToken);

    protected override async Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken)
    {
        var userName = this.GetPrimaryKeyString();
        return
            [
                new SystemPrompt(
                    $"""
                    You are a travel agent, liaising with a user, '{userName}'.
                    You are helping them to:
                    - book travel
                    - submit expense reports
                    Start by using the 'SayHello' tool to greet the user.
                    """)
            ];
    }

    protected override Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken)
    {
        // Check whether the goal conditions are met.
        // If not, provide more instruction to the language model to guide it towards that goal.
        return Task.FromResult<List<ChatItem>>([]);
    }

    [Tool, Description("Creates a greeting message for the user")]
    public async DurableTask<string> SayHello([Description("The user's name.")] string userName)
    {
        return $"Hello, {userName}! How can I help you today?";
    }

    [Tool, Description("Clears all stored history and resets the chat state.")]
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await base.DeleteAsync(cancellationToken);
    }
}

internal interface IUserLiaisonAgent : IGrainWithStringKey
{
    ValueTask AddUserMessageAsync(ChatItem message, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);
    ValueTask<List<ChatItem>> GetMessagesAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex, CancellationToken cancellationToken = default);
}

