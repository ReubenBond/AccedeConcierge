using Accede.Service.Models;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ComponentModel;
using System.Distributed.AI.Agents;
using System.Distributed.DurableTasks;
using System.Text.Json;
using System.Text.Json.Serialization;
#pragma warning disable 1998

namespace Accede.Service.Agents;

[Reentrant]
internal sealed partial class UserLiaisonAgent(
    ILogger<UserLiaisonAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient,
    [Memory("trip-request")] IDurableValue<TripParameters> tripRequest,
    [Memory("user-preferences")] IDurableDictionary<string, string> userPreferences)
    : ChatAgent(logger, chatClient), IUserLiaisonAgent
{
    private IChatClient _chatClient = chatClient;
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

                    ## Trip request
                    When the user expresses a travel plan, use the 'UpdateTripRequest' tool to store the plan.
                    You can use the 'GetTripRequest' tool to retrieve the current trip request details if you need to review them.

                    ## Trip itineraries
                    When the trip request is updated, use the 'CreateCandidateItineraries' tool to generate candidate itineraries for the user.

                    ## Greeting the user
                    Use the 'SayHello' tool to greet the user.
                    """)
            ];
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
    public async DurableTask<string> CreateCandidateItineraries(CancellationToken cancellationToken)
    {
        // TODO: If the user has not provided their preferences or travel plans, prompt them to do so by
        // returning an error message to the LLM.

        // This method is a tool call made by the LLM.
        // It implements a side conversation with a travel agency agent where the liaison (this agent)
        // acts as an advocate for the user, conversing with the travel agency agent to find suitable
        // candidate itineraries.
        // Once a set of suitable itineraries are found, it saves them in the local chat history so the
        // user can see them, and it returns to the LLM.

        // Craft an initial request for the travel agency agent based on the user's travel plans & request.
        List<ChatMessage> messages =
        [
            new ChatMessage(
                ChatRole.System,
                $"""
                You are an advocate/agent for your customer. You are helping them to find suitable travel itineraries based on their preferences and plans.
                You are conversing with a travel agent who will provide you with candidate itineraries based on what you tell them.
                Each time you receive a candidate itinerary, check that it meets the customer's preferences and plans.
                If it does not, ask the travel agent to rectify the itinerary.
                Continue until you have found the best itinerary for your customer.
                The customer has expressed the following preferences:

                {JsonSerializer.Serialize(userPreferences, JsonSerializerOptions.Web)}

                ---
                The customer's travel plans are as follows:

                {JsonSerializer.Serialize(tripRequest, JsonSerializerOptions.Web)}

                Once a suitable candidate itinerary is found, say "Donezo" to end the conversation.
                """),
            new ChatMessage(
                ChatRole.User,
                $"""
                Hello, I am a travel agent. I can find itineraries for you. How may I help you today?
                """)
            ];

        var id = DurableExecutionContext.CurrentContext!.TaskId.ToString();
        var travelAgent = GrainFactory.GetGrain<ITravelAgencyAgent>(id);
        var iteration = 0;
        CandidateItineraryChatItem? candidate = null;
        while (iteration++ < 5)
        {
            var request = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

            if (request.Text.Contains("Donezo") && candidate is not null)
            {
                break;
            }

            messages.AddRange(request.Messages);

            // Submit the request to the travel agency agent.
            var response = await travelAgent.SendRequestAsync(new UserMessage(request.Text) { Id = iteration.ToString() });

            // Check if the response includes a suitable candidate itinerary
            if (response is CandidateItineraryChatItem newCandidate)
            {
                candidate = newCandidate;
            }

            if (response.ToChatMessage() is { } responseMessage)
            {
                messages.Add(responseMessage);
            }

            // While the response does not include suitable candidate itineraries (matching the preferences & plans), iterate with the travel agency agent.
            // Post the candidate itineraries to the chat history for the user and return them to the LLM as well.
        }

        if (candidate is not null)
        {
            AddStatusMessage(candidate);
            return $"The best candidate itineraries are:\n{JsonSerializer.Serialize(candidate.Options, JsonSerializerOptions.Web)}";
        }
        else
        {
            return "No suitable candidate itinerary was found.";
        }
    }

    [Tool, Description("Updates trip parameters based on user travel plans.")]
    public async Task UpdateTripRequestAsync(
        [Description("City where the trip originates")] string originCity,
        [Description("State/province where the trip originates (optional)")] string? originState,
        [Description("Country where the trip originates")] string originCountry,
        [Description("Airport code for origin (optional)")] string? originAirportCode,
        [Description("City of the destination")] string destinationCity,
        [Description("State/province of the destination (optional)")] string? destinationState,
        [Description("Country of the destination")] string destinationCountry,
        [Description("Airport code for destination (optional)")] string? destinationAirportCode,
        [Description("Start date of the trip")] DateTime startDate,
        [Description("End date of the trip")] DateTime endDate,
        [Description("Whether flight booking is needed")] bool needsFlight = true,
        [Description("Whether hotel booking is needed")] bool needsHotel = true,
        [Description("Whether car rental is needed")] bool needsCarRental = false,
        [Description("Number of travelers")] int numberOfTravelers = 1,
        [Description("A user-friendly message describing this update")] string message = "Trip details updated",
        CancellationToken cancellationToken = default)
    {
        var parameters = new TripParameters(
            new Location(originCity, originState, originCountry, originAirportCode),
            new Location(destinationCity, destinationState, destinationCountry, destinationAirportCode),
            startDate,
            endDate,
            new TravelRequirements(needsFlight, needsHotel, needsCarRental, numberOfTravelers)
        );

        // Update the trip request
        tripRequest.Value = parameters;

        // Add a status message to inform the user
        AddStatusMessage(new TripRequestUpdated(message) { Id = Guid.NewGuid().ToString("N") });

        // Persist the changes
        await WriteStateAsync(cancellationToken);
    }

    [Tool, Description("Retrieves the current trip request details.")]
    public async Task<TripParameters?> GetTripRequestAsync(CancellationToken cancellationToken)
    {
        return tripRequest.Value;
    }
}

internal interface IUserLiaisonAgent : IGrainWithStringKey
{
    ValueTask PostMessageAsync(ChatItem message, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex, CancellationToken cancellationToken = default);
}

