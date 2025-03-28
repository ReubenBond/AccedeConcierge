using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace System.Distributed.AI.Agents;

[GenerateSerializer]
public abstract class ChatItem(string text)
{
    [Id(0)]
    public string Text { get; init; } = text;

    [Id(1)]
    public required string Id { get; init; }

    public abstract ChatRole Role { get; }

    public abstract string Type { get; }

    [JsonIgnore]
    public abstract bool IsUserVisible { get; }

    public virtual ChatMessage? ToChatMessage() => new ChatMessage(Role, Text);

    [JsonIgnore]
    internal bool IsUserMessage => Role == ChatRole.User;
}

[GenerateSerializer]
public class AssistantResponse(string text) : ChatItem(text)
{
    public override string Type => "assistant";

    [Id(0)]
    public required string? ResponseId { get; set; }
    [Id(1)]
    public required bool IsFinal { get; set; }
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
}

[GenerateSerializer]
public class QuarantinedMessage(string text, ChatItem innerItem) : ChatItem(text)
{
    public override string Type => "quarantined";

    [Id(0)]
    public ChatItem InnerItem { get; init; } = innerItem;
    public override ChatRole Role => InnerItem.Role;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
}
