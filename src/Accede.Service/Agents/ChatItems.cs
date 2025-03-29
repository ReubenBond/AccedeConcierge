using Microsoft.Extensions.AI;
using System.Diagnostics.CodeAnalysis;
using System.Distributed.AI.Agents;

namespace Accede.Service.Agents;

[GenerateSerializer]
public sealed class SystemPrompt : ChatItem
{
    [SetsRequiredMembers]
    public SystemPrompt(string text) : base(text)
    {
        // There is only one system prompt.
        Id = "system-prompt";
    }

    public override string Type => "system-prompt";
    public override ChatRole Role => ChatRole.System;
    public override bool IsUserVisible => false;
}

[GenerateSerializer]
public sealed class UserMessage(string text) : ChatItem(text)
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
        var content = new List<AIContent>(attachments.Count + 1) { new TextContent(Text) };
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
public sealed class UserPreferenceUpdated(string text) : ChatItem(text)
{
    public override string Type => "preference-updated";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
}

[GenerateSerializer]
public sealed class TripRequestUpdated(string text) : ChatItem(text)
{
    public override string Type => "trip-request-updated";
    public override ChatRole Role => ChatRole.Assistant;
    public override bool IsUserVisible => true;
    public override ChatMessage? ToChatMessage() => null;
}

[GenerateSerializer]
internal sealed class ItinerarySelectedChatItem(string text) : ChatItem(text)
{
    [Id(0)]
    public required string MessageId { get; init; }
    
    [Id(1)]
    public required string OptionId { get; init; }

    public override string Type => "itinerary-selected";
    public override ChatRole Role => ChatRole.User;
    public override bool IsUserVisible => false;
    public override ChatMessage? ToChatMessage() => 
        new ChatMessage(ChatRole.User, $"I've selected itinerary option {OptionId}. Please reach out ");
}

[GenerateSerializer]
public readonly record struct UriAttachment(string Uri, string ContentType);
