using Accede.Service.Utilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ClientModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Distributed.AI.Agents;
using System.Distributed.AI.Agents.Tools;
using System.Distributed.DurableTasks;
using System.Runtime.CompilerServices;

namespace Accede.Service.Agents;

[Reentrant]
public abstract class ChatAgent : Agent, IChatAgent
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _pendingMessageEvent = new();
    private readonly AsyncManualResetEvent _historyUpdatedEvent = new();
    private readonly ILogger _logger;
    private readonly IChatClient _chatClient;
    private readonly IDurableQueue<ChatItem> _pendingMessages;
    private readonly IDurableList<ChatItem> _conversationHistory;
    private readonly AgentToolRegistry _toolRegistry;
    private AssistantResponse? _streamingFragment;
    private AgentToolOptions? _functionFactoryOptions;
    private CancellationTokenSource? _currentChatCts;
    private Task? _pumpTask;

    public ChatAgent(
        ILogger logger,
        IChatClient chatClient)
    {
        _logger = logger;
        _chatClient = chatClient;
        _pendingMessages = ServiceProvider.GetRequiredKeyedService<IDurableQueue<ChatItem>>("pending");
        _conversationHistory = ServiceProvider.GetRequiredKeyedService<IDurableList<ChatItem>>("conversation-history");

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
    protected virtual Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken) => Task.FromResult<List<ChatItem>>([]);
    protected virtual ChatOptions ChatOptions { get; }
    protected IReadOnlyList<ChatItem> ConversationHistory => new ReadOnlyCollection<ChatItem>(_conversationHistory);

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
        if (_pendingMessages.LastOrDefault() is { } lastMessage && lastMessage.Text == message.Text)
        {
            // Skip unsent dupes
            return;
        }

        _pendingMessages.Enqueue(message);
        await WriteStateAsync(cancellationToken);
        _pendingMessageEvent.SignalAndReset();
    }

    protected void AddMessage(ChatItem item)
    {
        var lastItem = _conversationHistory.LastOrDefault();
        if (lastItem is { Text: { } text } && text == item.Text)
        {
            return;
        }

        if (lastItem is AssistantResponse { IsFinal: false })
        {
            // A response is currently being streamed from the language model.
            _pendingMessages.Enqueue(item);
            _pendingMessageEvent.SignalAndReset();
        }
        else
        {
            _conversationHistory.Add(item);
            _historyUpdatedEvent.SignalAndReset();
        }
    }

    private async Task PumpChatCompletionStream(CancellationToken shutdownToken)
    {
        await Task.Yield();
        List<ChatMessage> chatMessages = new(_conversationHistory.Count);
        foreach (var message in _conversationHistory)
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
                if (_conversationHistory.LastOrDefault() is { IsUserMessage: true } lastHistoryMessage)
                {
                    lastMessage = lastHistoryMessage;
                }
                else if (_pendingMessages.TryDequeue(out var pendingMessage))
                {
                    if (pendingMessage.IsUserMessage)
                    {
                        lastMessage = pendingMessage;
                    }

                    _conversationHistory.Add(pendingMessage);
                }

                if (lastMessage is null)
                {
                    // Give application code a chance to seed the conversation.
                    bool addedNewMessages = await SeedConversation(chatMessages, shutdownToken, currentChatCancellation);
                    if (!addedNewMessages && _pendingMessages.Count == 0)
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
                await foreach (var response in _chatClient.GetStreamingResponseAsync(chatMessages, options: ChatOptions, cancellationToken: currentChatCancellation))
                {
                    if (string.IsNullOrEmpty(response?.Text)) continue;
                    if (_streamingFragment is not null && string.Equals(response.ResponseId, _streamingFragment.ResponseId))
                    {
                        // Update to the current message fragment.
                        _streamingFragment = new AssistantResponse(_streamingFragment.Text + response.Text)
                        {
                            Id = _streamingFragment.Id,
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
                            Id = response.ResponseId ?? Guid.NewGuid().ToString("N"),
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
                _logger.LogError(exception, "Error submitting chat messages.");
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
                Id = _streamingFragment.Id,
                ResponseId = _streamingFragment.ResponseId,
                IsFinal = true
            };
            _conversationHistory.Add(response);
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
                var failingItem = _conversationHistory.Last();
                _conversationHistory[^1] = new QuarantinedMessage($"The message could not be processed: {clientResultException.Message}", failingItem)
                {
                    Id = Guid.NewGuid().ToString("N")
                };
                chatMessages.RemoveAt(chatMessages.Count - 1);
                await WriteStateAsync(shutdownToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error quarantining chat message.");
            }
        }
    }

    private async Task<bool> SeedConversation(List<ChatMessage> chatMessages, CancellationToken shutdownToken, CancellationToken currentChatCancellation)
    {
        List<ChatItem> newMessages;
        bool createdConversation = false;
        if (_conversationHistory.Count == 0)
        {
            // Create the conversation.
            newMessages = await OnChatCreatedAsync(shutdownToken);
            if (newMessages is { Count: > 0 })
            {
                createdConversation = true;
                foreach (var msg in newMessages)
                {
                    _conversationHistory.Add(msg);
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
                    _pendingMessages.Enqueue(msg);
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
        await StateMachineManager.DeleteStateAsync(cancellationToken);
        DeactivateOnIdle();
        return true;
    }

    public ValueTask<List<ChatItem>> GetMessagesAsync(CancellationToken cancellationToken = default) => new([.. _conversationHistory.Where(msg => msg.IsUserVisible)]);

    public IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex = 0, CancellationToken cancellationToken = default)
        => WatchChatHistoryAsync(startIndex, includePartialResponses: true, cancellationToken);

    private async IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(
        int startIndex = 0,
        bool includePartialResponses = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var index = startIndex;
        string? lastFragment = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Account for partial responses.
            if (includePartialResponses &&
                index == _conversationHistory.Count && _streamingFragment is { } fragment &&
                (lastFragment is null || fragment.Text.Length > lastFragment.Length))
            {
                var partialResponse = new AssistantResponse(fragment.Text[(lastFragment?.Length ?? 0)..])
                {
                    Id = fragment.Id,
                    ResponseId = fragment.ResponseId,
                    IsFinal = false
                };

                lastFragment = fragment.Text;
                yield return partialResponse;
            }

            if (index >= _conversationHistory.Count)
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
                continue;
            }

            lastFragment = null;
            var message = _conversationHistory[index];
            ++index;

            if (!message.IsUserVisible)
            {
                // Hide system & tool messages
                continue;
            }

            yield return message;
        }
    }

    public async DurableTask<ChatItem> SendRequestAsync(ChatItem request)
    {
        var cancellationToken = CancellationToken.None;
        if (request.Role != ChatRole.User)
        {
            throw new ArgumentException("Only user messages can be sent to the chat agent.", nameof(request));
        }

        // Only add the message if it hasn't been added before.
        if (!_pendingMessages.Any(item => item.Id == request.Id) && !_conversationHistory.Any(item => item.Id == request.Id))
        {
            AddMessage(request);
            await WriteStateAsync(cancellationToken);
        }

        // Wait for a response.
        var foundOurMessage = false;
        var index = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index >= _conversationHistory.Count)
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
                continue;
            }

            var message = _conversationHistory[index];
            ++index;

            if (foundOurMessage)
            {
                return message;
            }

            if (message.Id == request.Id)
            {
                // The next message assistant response should correspond to our message.
                foundOurMessage = true;
            }
        }
    }
}