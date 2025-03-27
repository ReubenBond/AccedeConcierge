using Accede.Service.Utilities;
using Accede.Service.Utilities.Functions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ClientModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Distributed.AI.Agents;
using System.Runtime.CompilerServices;

namespace Accede.Service.Agents;

[Reentrant]
public abstract class ChatAgent : DurableGrain
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _pendingMessageEvent = new();
    private readonly AsyncManualResetEvent _historyUpdatedEvent = new();
    private readonly ILogger logger;
    private readonly IChatClient chatClient;
    private readonly IDurableQueue<ChatItem> pendingMessages;
    private readonly IDurableList<ChatItem> conversationHistory;
    private readonly AgentToolRegistry _toolRegistry;
    private AssistantResponse? _streamingFragment;
    private AgentToolOptions? _functionFactoryOptions;
    private CancellationTokenSource? _currentChatCts;
    private Task? _pumpTask;

    public ChatAgent(
        ILogger logger,
        IChatClient chatClient,
        IDurableQueue<ChatItem> pendingMessages,
        IDurableList<ChatItem> conversationHistory)
    {
        this.logger = logger;
        this.chatClient = chatClient;
        this.pendingMessages = pendingMessages;
        this.conversationHistory = conversationHistory;

        // Register tools
        _toolRegistry = AgentToolRegistry.Create(GetType(), GrainContext, FunctionFactoryOptions);
        GrainContext.SetComponent(_toolRegistry);
        ChatOptions = new ChatOptions { Tools = _toolRegistry.Tools };
    }

    protected IList<AITool> Tools => _toolRegistry.Tools;
    protected AgentToolOptions FunctionFactoryOptions => _functionFactoryOptions ??= new AgentToolOptions { };
    protected void AddTool(Delegate toolFunc) => _toolRegistry.AddTool(toolFunc, toolName: null);
    protected void AddTool(Delegate toolFunc, string? toolName) => _toolRegistry.AddTool(toolFunc, toolName);

    protected abstract Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken);
    protected abstract Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken);
    protected virtual ChatOptions ChatOptions { get; }
    protected IReadOnlyList<ChatItem> ConversationHistory => new ReadOnlyCollection<ChatItem>(conversationHistory);

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _pumpTask = PumpChatCompletionStream(_shutdownCts.Token);
        _pumpTask.Ignore();
        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Cancel all waiters. They will need to reconnect.
        await _shutdownCts.CancelAsync();
        _historyUpdatedEvent.Cancel();
        _pendingMessageEvent.Cancel();
        if (_pumpTask is { } pumpTask)
        {
            await pumpTask;
        }
    }

    protected async ValueTask AddMessageAsync(ChatItem message, CancellationToken cancellationToken = default)
    {
        if (pendingMessages.LastOrDefault() is { } lastMessage && lastMessage.Text == message.Text)
        {
            // Skip unsent dupes
            return;
        }

        pendingMessages.Enqueue(message);
        await WriteStateAsync(cancellationToken);
        _pendingMessageEvent.SignalAndReset();
    }

    protected void AddStatusMessage(ChatItem item)
    {
        var lastItem = conversationHistory.LastOrDefault();
        if (lastItem is { Text: { } text } && text == item.Text)
        {
            return;
        }

        if (lastItem is AssistantResponse { IsFinal: false })
        {
            // A response is currently being streamed from the language model.
            pendingMessages.Enqueue(item);
            _pendingMessageEvent.SignalAndReset();
        }
        else
        {
            conversationHistory.Add(item);
            _historyUpdatedEvent.SignalAndReset();
        }
    }

    private async Task PumpChatCompletionStream(CancellationToken shutdownToken)
    {
        await Task.Yield();
        List<ChatMessage> chatMessages = new(conversationHistory.Count);
        foreach (var message in conversationHistory)
        {
            if (message.ToChatMessage() is { } chatMessage)
            {
                chatMessages.Add(chatMessage);
            }
        }

        while (!_shutdownCts.IsCancellationRequested)
        {
            _currentChatCts ??= CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            var currentChatCancellation = _currentChatCts.Token;
            try
            {
                // Completion always starts with a new, unanswered user message.
                ChatItem? lastMessage = null;
                if (conversationHistory.LastOrDefault() is { IsUserMessage: true } lastHistoryMessage)
                {
                    lastMessage = lastHistoryMessage;
                }
                else if (pendingMessages.TryDequeue(out var pendingMessage))
                {
                    if (pendingMessage.IsUserMessage)
                    {
                        lastMessage = pendingMessage;
                    }

                    conversationHistory.Add(pendingMessage);
                }

                if (lastMessage is null)
                {
                    // Give application code a chance to seed the conversation.
                    bool addedNewMessages = await SeedConversation(chatMessages, shutdownToken, currentChatCancellation);
                    if (!addedNewMessages && pendingMessages.Count == 0)
                    {
                        // Wait for a user message
                        await _pendingMessageEvent.WaitAsync(shutdownToken);
                    }

                    continue;
                }

                if (lastMessage.ToChatMessage() is { } chatMessage)
                {
                    chatMessages.Add(chatMessage);
                }

                _streamingFragment = null;
                await foreach (var response in chatClient.GetStreamingResponseAsync(chatMessages, options: ChatOptions, cancellationToken: currentChatCancellation))
                {
                    if (string.IsNullOrEmpty(response?.Text)) continue;
                    if (_streamingFragment is not null && string.Equals(response.ResponseId, _streamingFragment.ResponseId))
                    {
                        // Update to the current message fragment.
                        _streamingFragment = new AssistantResponse(_streamingFragment.Text + response.Text)
                        {
                            ResponseId = response.ResponseId,
                            IsFinal = false
                        };
                    }
                    else
                    {
                        // Seal the previous message.
                        if (_streamingFragment is not null)
                        {
                            AddFinalResponse();
                        }

                        // Start a new message.
                        _streamingFragment = new AssistantResponse(response.Text)
                        {
                            ResponseId = response.ResponseId,
                            IsFinal = false
                        };
                    }

                    _historyUpdatedEvent.SignalAndReset();
                }

                if (_streamingFragment is not null)
                {
                    AddFinalResponse();
                    _streamingFragment = null;
                    _historyUpdatedEvent.SignalAndReset();
                }

                await WriteStateAsync(shutdownToken);
            }
            catch (OperationCanceledException) when (currentChatCancellation.IsCancellationRequested)
            {
                // Ignore
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error submitting chat messages.");
                if (exception is ClientResultException clientResultException)
                {
                    if (clientResultException.Status == 400)
                    {
                        await QuarantineLastMessage(chatMessages, clientResultException);
                    }
                }
            }
        }

        void AddFinalResponse()
        {
            Debug.Assert(_streamingFragment is not null);
            var response = new AssistantResponse(_streamingFragment.Text)
            {
                ResponseId = _streamingFragment.ResponseId,
                IsFinal = true
            };
            conversationHistory.Add(response);
            if (response.ToChatMessage() is { } msg)
            {
                chatMessages.Add(msg);
            }
        }

        async Task QuarantineLastMessage(List<ChatMessage> chatMessages, ClientResultException clientResultException)
        {
            try
            {
                // Quarantine the message in a dead letter queue.
                var failingItem = conversationHistory.Last();
                conversationHistory[^1] = new QuarantinedMessage($"The message could not be processed: {clientResultException.Message}", failingItem);
                chatMessages.RemoveAt(chatMessages.Count - 1);
                await WriteStateAsync(shutdownToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error quarantining chat message.");
            }
        }
    }

    private async Task<bool> SeedConversation(List<ChatMessage> chatMessages, CancellationToken shutdownToken, CancellationToken currentChatCancellation)
    {
        List<ChatItem> newMessages;
        bool createdConversation = false;
        if (conversationHistory.Count == 0)
        {
            // Create the conversation.
            newMessages = await OnChatCreatedAsync(shutdownToken);
            if (newMessages is { Count: > 0 })
            {
                createdConversation = true;
                foreach (var msg in newMessages)
                {
                    conversationHistory.Add(msg);
                    if (msg.ToChatMessage() is { } chatMsg)
                    {
                        chatMessages.Add(chatMsg);
                    }
                }
            }
        }
        else
        {
            // Prod the conversation along.
            newMessages = await OnChatIdleAsync(currentChatCancellation);
            if (newMessages is { Count: > 0 })
            {
                foreach (var msg in newMessages)
                {
                    pendingMessages.Enqueue(msg);
                }

            }
        }

        if (newMessages is { Count: > 0 })
        {
            await WriteStateAsync(shutdownToken);
            _pendingMessageEvent.SignalAndReset();
            if (createdConversation)
            {
                _historyUpdatedEvent.SignalAndReset();
            }

            return true;
        }

        return false;
    }

    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentCts = _currentChatCts;
        _currentChatCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        if (currentCts != null)
        {
            await currentCts.CancelAsync();
            currentCts.Dispose();
        }
    }

    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        await this.StateMachineManager.DeleteStateAsync(cancellationToken);
        this.DeactivateOnIdle();
        return true;
    }

    public ValueTask<List<ChatItem>> GetMessagesAsync(CancellationToken cancellationToken = default) => new([.. conversationHistory.Where(msg => msg.IsUserVisible)]);

    public IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex = 0, CancellationToken cancellationToken = default)
        => WatchChatHistoryAsync(startIndex, includePartialResponses: true, cancellationToken);

    private async IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex = 0, bool includePartialResponses = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var i = startIndex;
        string? lastFragment = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Account for partial responses.
            if (includePartialResponses &&
                i == conversationHistory.Count && _streamingFragment is { } fragment &&
                (lastFragment is null || fragment.Text.Length > lastFragment.Length))
            {
                var partialResponse = new AssistantResponse(fragment.Text[(lastFragment?.Length ?? 0)..])
                {
                    ResponseId = fragment.ResponseId,
                    IsFinal = false
                };

                lastFragment = fragment.Text;
                yield return partialResponse;
            }

            if (i >= conversationHistory.Count)
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
                continue;
            }

            lastFragment = null;
            var message = conversationHistory[i];
            ++i;

            if (!message.IsUserVisible)
            {
                // Hide system & tool messages
                continue;
            }

            yield return message;
        }
    }
}
