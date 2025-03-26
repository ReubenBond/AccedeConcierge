using Microsoft.Extensions.AI;
using System.Distributed.AI.Agents;

namespace Accede.Service.Agents;

[GenerateSerializer]
public class SystemPrompt(string text) : ChatItem(text)
{
    public override string Type => "system-prompt";
    public override ChatRole Role => ChatRole.System;
    public override bool IsUserVisible => false;
}

[GenerateSerializer]
public class UserMessage(string text) : ChatItem(text)
{
    public override string Type => "user";
    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => true;

    [Id(0)]
    public List<UriAttachment>? Attachments { get; init; }

    public override ChatMessage? ToChatMessage() => Attachments switch
    {
        { Count: > 0 } attachments => new ChatMessage(ChatRole.User, [new TextContent(Text), .. attachments.Select(f => new UriContent(f.Uri, f.ContentType))]),
        _ => base.ToChatMessage(),
    };
}

[GenerateSerializer]
public readonly record struct UriAttachment(Uri Uri, string ContentType);
