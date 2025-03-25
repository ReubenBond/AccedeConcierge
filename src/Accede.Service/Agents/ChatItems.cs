using Microsoft.Extensions.AI;
using System.Distributed.AI.Agents;

namespace Accede.Service.Agents;

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
public class AgentMessage(string agentName, string text) : ChatItem(text)
{
    public override string Type => nameof(AgentMessage);

    [Id(0)]
    public string AgentName { get; } = agentName;

    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => true;
}
