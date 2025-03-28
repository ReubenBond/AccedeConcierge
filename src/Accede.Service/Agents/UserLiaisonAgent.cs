using Accede.Service.Models;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ComponentModel;
using System.Distributed.AI.Agents;
using System.Distributed.DurableTasks;
#pragma warning disable 1998

namespace Accede.Service.Agents;

[Reentrant]
internal sealed partial class UserLiaisonAgent(
    ILogger<UserLiaisonAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient,
    [FromKeyedServices("trip-request")] IDurableValue<TripParameters> tripRequest,
    [FromKeyedServices("user-preferences")] IDurableDictionary<string, string> userPreferences)
    : ChatAgent(logger, chatClient), IUserLiaisonAgent
{
    public async ValueTask PostMessageAsync(ChatItem message, CancellationToken cancellationToken = default) => await AddMessageAsync(message, cancellationToken);

    protected override async Task<List<ChatItem>> OnChatCreatedAsync(CancellationToken cancellationToken)
    {
        var userName = this.GetPrimaryKeyString();
        return
            [
                new SystemPrompt(
                    $"""
                    ## User liaison agent
                    You are a travel agent, liaising with a user, '{userName}'.
                    You are helping them to:
                    - book travel
                    - submit expense reports
                    
                    ## User preferences
                    When the user expresses a travel preference, use the 'UpdateUserPreference' tool to store the preference.
                    Do not ask the user if they would like to store the preference, just do it.
                    Preferences which have been stored previously do not need to be stored again.

                    ## Greeting the user
                    Use the 'SayHello' tool to greet the user.
                    """)
            ];
    }

    protected override Task<List<ChatItem>> OnChatIdleAsync(CancellationToken cancellationToken)
    {
        if (tripRequest.Value // is not fully fleshed out) {
            }
        // Check whether the goal conditions are met.
        // If not, provide more instruction to the language model to guide it towards that goal.
        return Task.FromResult<List<ChatItem>>([]);
    }

    [Tool, Description("Creates a greeting message for the user")]
    public async DurableTask<string> SayHello([Description("The user's name.")] string userName)
    {
        return $"Hello, {userName}! How can I help you today?";
    }

    [Tool, Description("Clears all stored history and resets the chat state.")]
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await base.DeleteAsync(cancellationToken);
    }

    [Tool, Description(
        """
        Updates a user travel preference. Preferences are stored as key-value pairs of strings.
        Examples:
         - "hotel-brand": "Hyatt"
         - "flight-class": "Economy"
         - "flight-seating": "Aisle"
         - "car-type": "Compact"
        """)]
    public async Task UpdateUserPreferenceAsync(
        [Description("The preference name, eg: \"hotel-brand\"")] string name,
        [Description("The preference value, eg: \"Hyatt\"")] string value,
        [Description("A user-friendly message describing this action")] string message,
        CancellationToken cancellationToken)
    {
        AddStatusMessage(new UserPreferenceUpdated(message) { Id = Guid.NewGuid().ToString("N") });

        if (string.IsNullOrWhiteSpace(value))
        {
            userPreferences.Remove(name);
        }
        else
        {
            userPreferences[name] = value;
        }

        await WriteStateAsync(cancellationToken);
    }

    [Tool, Description("Gets the user's travel preferences.")]
    public async Task<IDictionary<string, string>> GetUserPreferences(CancellationToken cancellationToken)
    {
        return userPreferences;
    }

    [Tool, Description("Creates a candidate itinerary for the user based on their travel preferences and plans.")]
    public async DurableTask<string> CreateCandidateItineraries()
    {
        // Craft an initial request for the travel agency agent based on the user's travel plans & request.
        // Submit the request to the travel agency agent.
        // While the response does not include suitable candidate itineraries (matching the preferences & plans), iterate with the travel agency agent.
        // Post the candidate itineraries to the chat history for the user and return them to the LLM as well.
    }
}

internal interface IUserLiaisonAgent : IGrainWithStringKey
{
    ValueTask PostMessageAsync(ChatItem message, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex, CancellationToken cancellationToken = default);
}

