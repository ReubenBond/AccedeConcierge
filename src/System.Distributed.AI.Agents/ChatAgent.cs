using Accede.Service.Utilities;
using Accede.Service.Utilities.Functions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using System.Collections.ObjectModel;
using System.Distributed.AI.Agents;
using System.Runtime.CompilerServices;

namespace Accede.Service.Agents;

public abstract class ChatAgent(
    ILogger logger,
    IChatClient chatClient,
    IDurableQueue<ChatItem> pendingMessages,
    IDurableList<ChatItem> conversationHistory) : DurableGrain
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _pendingMessageEvent = new();
    private readonly AsyncManualResetEvent _historyUpdatedEvent = new();
    private DurableAIFunctionFactoryOptions? _functionFactoryOptions;
    private CancellationTokenSource? _currentChatCts;
    private Task? _pumpTask;

    protected DurableAIFunctionFactoryOptions FunctionFactoryOptions => _functionFactoryOptions ??= new DurableAIFunctionFactoryOptions
    {
        TaskScheduler = TaskScheduler.Current
    };
    protected abstract Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken);
    protected abstract Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken);
    protected virtual ChatOptions? ChatOptions { get; } = null;
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
                    bool flowControl = await SeedConversation(chatMessages, shutdownToken, currentChatCancellation);
                    if (!flowControl)
                    {
                        continue;
                    }

                    // Wait for a user message
                    if (pendingMessages.Count == 0)
                    {
                        await _pendingMessageEvent.WaitAsync(shutdownToken);
                    }

                    continue;
                }

                if (lastMessage.ToChatMessage() is { } chatMessage)
                {
                    chatMessages.Add(chatMessage);
                }

                var lastResponseFragment = conversationHistory switch
                {
                    { Count: > 0 } => conversationHistory[^1] as AssistantResponse,
                    _ => null,
                };

                await foreach (var response in chatClient.GetStreamingResponseAsync(chatMessages, options: ChatOptions, cancellationToken: currentChatCancellation))
                {
                    if (string.IsNullOrEmpty(response?.Text)) continue;

                    if (response.ResponseId is { Length: > 0 } responseId && lastResponseFragment is { } && responseId.Equals(lastResponseFragment.ResponseId))
                    {
                        // Update to the current message fragment.
                        conversationHistory[^1] = new AssistantResponse(lastResponseFragment.Text + response.Text)
                        {
                            ResponseId = response.ResponseId,
                            IsFinal = false
                        };
                    }
                    else
                    {
                        // Seal the previous message.
                        if (lastResponseFragment is not null)
                        {
                            conversationHistory[^1] = new AssistantResponse(lastResponseFragment.Text)
                            {
                                ResponseId = lastResponseFragment.ResponseId,
                                IsFinal = true
                            };
                        }

                        // New message.
                        conversationHistory.Add(new AssistantResponse(response.Text ?? "")
                        {
                            ResponseId = response.ResponseId,
                            IsFinal = false
                        });
                    }

                    lastResponseFragment = conversationHistory[^1] as AssistantResponse;
                    _historyUpdatedEvent.SignalAndReset();
                }

                if (lastResponseFragment is not null)
                {
                    conversationHistory[^1] = new AssistantResponse(lastResponseFragment.Text)
                    {
                        ResponseId = lastResponseFragment.ResponseId,
                        IsFinal = true
                    };
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

    public async IAsyncEnumerable<ChatItem> SubscribeAsync(int startIndex = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var i = startIndex;
        string? partialMessageFragment = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i >= conversationHistory.Count)
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
                continue;
            }

            var message = conversationHistory[i];

            if (!message.IsUserVisible)
            {
                // Hide system & tool messages
                ++i;
                continue;
            }

            // This is the final update for this message if there are subsequent messages.
            var isFinal = (message as AssistantResponse)?.IsFinal ?? true;
            var responseId = (message as AssistantResponse)?.ResponseId ?? $"msg-{i}";

            if (message is AssistantResponse { IsFinal: false } fragment)
            {
                yield return new AssistantResponse(fragment.Text[(partialMessageFragment?.Length ?? 0)..])
                {
                    ResponseId = fragment.ResponseId,
                    IsFinal = false
                };

                partialMessageFragment = message.Text;
            }
            else
            {
                partialMessageFragment = null;
                yield return message;
            }

            // If there is a subsequent message, advance, otherwise wait for an update.
            if (conversationHistory.Count > i + 1)
            {
                ++i;
            }
            else
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
            }
        }
    }

    public async IAsyncEnumerable<ChatItem> GetChatItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var i = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i >= conversationHistory.Count)
            {
                await _historyUpdatedEvent.WaitAsync(cancellationToken);
                continue;
            }

            var message = conversationHistory[i];

            // Wait for whole responses.
            if (message is AssistantResponse { IsFinal: false } && i + 1 == conversationHistory.Count)
            {
                continue;
            }

            yield return message;
            ++i;
        }
    }
}
