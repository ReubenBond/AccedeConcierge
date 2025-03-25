using Microsoft.Extensions.AI;

namespace Accede.Service.Agents;

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
public class IssueDetail(string text) : ChatItem(text)
{
    public override string Type => "issue-detail";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => new ChatMessage(ChatRole.User, $"Here are the details of the issue you are working on:\n{Text}");
}

[GenerateSerializer]
public class SystemPrompt(string text) : ChatItem(text)
{
    public override string Type => nameof(SystemPrompt);
    public override ChatRole Role => ChatRole.System;
    public override bool IsUserVisible => false;
}

[GenerateSerializer]
public class ModelContext(string text) : ChatItem(text)
{
    public override string Type => "model-context";
    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => false;
}

[GenerateSerializer]
public class InstructionMessage(string text) : ChatItem(text)
{
    public override string Type => "instruction";
    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => false;
}

[GenerateSerializer]
public class UserMessage(string text) : ChatItem(text)
{
    public override string Type => "user";
    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => true;
}

[GenerateSerializer]
public class UpdateDraftResponseMessage(string text) : ChatItem(text)
{
    public override string Type => "update-draft";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
}

[GenerateSerializer]
public class AddLabelMessage(string label) : ChatItem($"Will add label '{label}' to the issue.")
{
    [Id(0)]
    public string Label { get; } = label;
    public override string Type => "add-label";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
}

[GenerateSerializer]
public class StatusChatItem(string text) : ChatItem(text)
{
    public override string Type => "status";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
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
public class AgentMessage(string agentName, string text) : ChatItem(text)
{
    public override string Type => nameof(AgentMessage);

    [Id(0)]
    public string AgentName { get; } = agentName;

    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => true;
}
