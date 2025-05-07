using Accede.Service.Models;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Journaling;
using System.ComponentModel;
using System.Distributed.AI.Agents;
using System.Distributed.DurableTasks;
using System.Text.Json;
#pragma warning disable 1998

namespace Accede.Service.Agents;

[GenerateSerializer]
internal readonly record struct UserLiaisonState(string? LastReceiptProcessedMessageId);

[Reentrant]
internal sealed partial class UserLiaisonAgent(
    ILogger<UserLiaisonAgent> logger,
    [FromKeyedServices("large")] IChatClient chatClient,
    [Memory("state")] IDurableValue<UserLiaisonState> state,
    [Memory("user-preferences")] IDurableDictionary<string, string> userPreferences,
    [Memory("trip-parameters")] IDurableValue<TripParameters> tripParameters,
    [Memory("trip-request")] IDurableValue<TripRequest> selectedItinerary,
    [Memory("trip-approval-status")] IDurableValue<TripRequestResult> tripApprovalStatus,
    [Memory("receipts")] IDurableList<ReceiptData> receipts)
    : ChatAgent(logger, chatClient), IUserLiaisonAgent
{
    private readonly IChatClient _chatClient = chatClient;

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

                    ## Receipt management
                    When the user mentions expense reports or receipts:
                    - Use 'GetReceipts' to retrieve existing receipts
                    - Use 'ProcessReceiptUpload' when the user uploads an image of a receipt

                    When an image is uploaded, use 'ProcessReceiptUpload' to extract the receipt data from the image.                    
                    Be proactive in identifying receipt information from conversations, such as "I spent $50 on dinner at Restaurant X yesterday."
                    If the user mentions an expense they made but has not provided any images, tell them that they need to upload a picture of the receipt in order for it to be expensed.
                    Use the phrase "pics or it didn't happen" when doing so.
                    
                    ## Greeting the user
                    Use the 'SayHello' tool to greet the user.
                    """)
            ];
    }

    [Tool, Description("Creates a greeting message for the user")]
    public async Task<string> SayHello([Description("The user's name.")] string userName)
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
        AddMessage(new UserPreferenceUpdated(message) { Id = Guid.NewGuid().ToString("N") });

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
                Continue until you have found the a suitable itinerary for your customer.
                Your customer is not too fussy, and it's ok if the itinerary does not match all of their needs.
                The customer has expressed the following preferences:

                {JsonSerializer.Serialize(userPreferences, JsonSerializerOptions.Web)}

                ---
                The customer's travel plans are as follows:

                {JsonSerializer.Serialize(tripParameters, JsonSerializerOptions.Web)}

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
        AddMessage(new TripRequestUpdated("Finding candidate itineraries....") { Id = Guid.NewGuid().ToString() });
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
                AddMessage(new TripRequestUpdated("Candidate found, validating it against your preferences and plans...") { Id = Guid.NewGuid().ToString() });
                candidate = newCandidate;
            }

            if (response.ToChatMessage() is { } responseMessage)
            {
                if (responseMessage.Role != ChatRole.User)
                {
                    responseMessage = new ChatMessage(ChatRole.User, responseMessage.Contents);
                }

                messages.Add(responseMessage);
            }

            // While the response does not include suitable candidate itineraries (matching the preferences & plans), iterate with the travel agency agent.
            // Post the candidate itineraries to the chat history for the user and return them to the LLM as well.
        }

        if (candidate is not null)
        {
            AddMessage(candidate);
            return
                $"""
                The best candidate itineraries are:
                {JsonSerializer.Serialize(candidate.Options, JsonSerializerOptions.Web)}.";
                ---
                The customer has just been sent these candidate itineraries - do NOT reiterate them to the customer.
                Instead, provide a cheery message asking them to select an option if it is suitable or to let you know what they'd like to modify.
                """;
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
        tripParameters.Value = parameters;

        // Add a status message to inform the user
        AddMessage(new TripRequestUpdated(message) { Id = Guid.NewGuid().ToString("N") });

        // Persist the changes
        await WriteStateAsync(cancellationToken);
    }

    [Tool, Description("Retrieves the current trip request details.")]
    public async Task<TripParameters?> GetTripRequestAsync(CancellationToken cancellationToken)
    {
        return tripParameters.Value;
    }

    [Tool, Description("Gets the trip itinerary which the user has selected.")]
    public async Task<TripOption?> GetSelectedItinerary(CancellationToken cancellationToken)
    {
        return selectedItinerary.Value?.TripOption;
    }

    [Tool, Description("Retrieves all stored receipts for the user.")]
    public async Task<IReadOnlyList<ReceiptData>> GetReceiptsAsync(CancellationToken cancellationToken)
    {
        return receipts.AsReadOnly();
    }

    [Tool, Description("Extract expense reporting information from images of receipts which the user submitted in their most recent request.")]
    public async DurableTask<string?> ProcessReceiptUploadAsync(
        [Description("Whether all receipts should be cleared and reprocessed.")] bool retryAll,
        CancellationToken cancellationToken = default)
    {
        var index = 0;
        var lastReceiptMessageId = state.Value.LastReceiptProcessedMessageId;
        if (!retryAll && lastReceiptMessageId is not null)
        {
            foreach (var (i, item) in ConversationHistory.Index())
            {
                if (item.Id == lastReceiptMessageId)
                {
                    index = i + 1;
                    break;
                }
            }
        }

        List<ReceiptData> foundReceipts = [];

        // Scan through recent user messages to find attachments.
        for (var i = index; i < ConversationHistory.Count; i++)
        {
            // For each image, process the image for receipt data.
            var item = ConversationHistory[i];
            if (item is UserMessage { Attachments.Count: > 0 } userMessage)
            {
                var chatMessage = userMessage.ToChatMessage();
                if (chatMessage is null)
                {
                    continue;
                }

                foreach (var content in chatMessage.Contents)
                {
                    if (content is TextContent)
                    {
                        continue;
                    }

                    AddMessage(new TripRequestUpdated("Analyzing receipt...") { Id = Guid.NewGuid().ToString() });
                    var attemptsRemaining = 2;
                    while (attemptsRemaining > 0)
                    {
                        // Add the receipt to memory and continue until all images are processed.
                        var chatResponse = await _chatClient.GetResponseAsync<ReceiptData?>(
                            new ChatMessage(ChatRole.User, [new TextContent("Process this receipt image and extract the relevant data for reimbursement."), content]),
                            cancellationToken: cancellationToken);

                        if (chatResponse.TryGetResult(out var receiptData) && receiptData is not null)
                        {
                            // Make sure there is a receipt id in case we want to delete by id later on.
                            if (receiptData.ReceiptId is null)
                            {
                                receiptData = receiptData with { ReceiptId = Guid.NewGuid().ToString("N") };
                            }

                            AddMessage(new TripRequestUpdated("Success. Saving receipt details...") { Id = Guid.NewGuid().ToString() });
                            foundReceipts.Add(receiptData);
                            break;
                        }

                        --attemptsRemaining;
                    }
                }
            }

            lastReceiptMessageId = item.Id;
        }

        string result;
        if (foundReceipts.Count > 0)
        {
            result = foundReceipts.Count > 1 ? $"Processed {foundReceipts.Count} receipts." : "Processed receipt.";
            AddMessage(new ReceiptsProcessedChatItem(result, foundReceipts)
            {
                Id = Guid.NewGuid().ToString("N"),
            });

            if (retryAll)
            {
                receipts.Clear();
            }

            foreach (var receipt in foundReceipts)
            {
                receipts.Add(receipt);
            }
        }
        else
        {
            result = "The uploaded image contained no receipt data.";
        }

        state.Value = state.Value with { LastReceiptProcessedMessageId = lastReceiptMessageId };
        await WriteStateAsync(cancellationToken);
        return result;
    }

    public async DurableTask SelectItineraryAsync(string messageId, string optionId)
    {
        logger.LogInformation("Selecting itinerary with ID {OptionId} from message {MessageId}", optionId, messageId);

        TripOption? option = await DurableTask.Run(_ => FindTripOption(messageId, optionId));
        var request = selectedItinerary.Value = new($"req-{messageId}", option);

        // Add a message to inform the system about the selection
        AddMessage(new ItinerarySelectedChatItem($"Itinerary option {optionId} selected. Requesting admin approval.")
        {
            Id = Guid.NewGuid().ToString("N"),
            MessageId = messageId,
            OptionId = optionId
        });

        var admin = GrainFactory.GetGrain<IAdminAgent>("admin");
        var result = await admin.RequestApproval(request);
        tripApprovalStatus.Value = result;
        AddMessage(new TripRequestDecisionChatItem(result) { Id = $"approval-{result.RequestId}" });
    }

    private TripOption FindTripOption(string messageId, string optionId)
    {
        TripOption? option = null;
        foreach (var message in ConversationHistory)
        {
            if (message is CandidateItineraryChatItem candidateItinerary && candidateItinerary.Id == messageId)
            {
                option = candidateItinerary.Options.FirstOrDefault(option => option.OptionId == optionId);
                break;
            }
        }

        if (option is null)
        {
            throw new InvalidOperationException("Selected itinerary option not found");
        }

        return option;
    }
}

internal interface IUserLiaisonAgent : IGrainWithStringKey
{
    ValueTask PostMessageAsync(ChatItem message, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatItem> WatchChatHistoryAsync(int startIndex, CancellationToken cancellationToken = default);
    DurableTask SelectItineraryAsync(string messageId, string optionId);
}
