using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace System.Distributed.AI.Agents;

[GenerateSerializer]
public abstract class ChatItem(string text)
{
    [Id(0)]
    public string Text { get; init; } = text;

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
