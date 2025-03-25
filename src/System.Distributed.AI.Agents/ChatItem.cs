using Microsoft.Extensions.AI;

namespace System.Distributed.AI.Agents;

[GenerateSerializer]
public abstract class ChatItem(string text)
{
    [Id(0)]
    public string Text { get; init; } = text;

    public abstract ChatRole Role { get; }

    public abstract string Type { get; }

    public abstract bool IsUserVisible { get; }

    public virtual ChatMessage? ToChatMessage() => new ChatMessage(Role, Text);
    internal bool IsUserMessage => Role == ChatRole.User;
}

[GenerateSerializer]
public class AssistantResponseFragment(string text) : ChatItem(text)
{
    public override string Type => nameof(AssistantResponseFragment);

    [Id(0)]
    public required string? ResponseId { get; set; }
    [Id(1)]
    public required bool IsFinal { get; set; }
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
}

[GenerateSerializer]
public record ClientMessageFragment(string Role, string Text, string? ResponseId, string Type, bool IsFinal);
