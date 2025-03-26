﻿using Microsoft.Extensions.AI;
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
        { Count: > 0 } attachments => CreateChatMessageWithAttachments(attachments),
        _ => base.ToChatMessage(),
    };

    private ChatMessage CreateChatMessageWithAttachments(List<UriAttachment> attachments)
    {
        var content = new List<AIContent>(attachments.Count + 1)
        {new TextContent(Text)
        };
        foreach (var attachment in attachments)
        {
            if (attachment.Uri.StartsWith("data:"))
            {
                content.Add(new DataContent(attachment.Uri, attachment.ContentType));
            }
            else
            {
                content.Add(new UriContent(attachment.Uri, attachment.ContentType));
            }
        }

        return new ChatMessage(ChatRole.User, content);
    }
}

[GenerateSerializer]
public readonly record struct UriAttachment(string Uri, string ContentType);
