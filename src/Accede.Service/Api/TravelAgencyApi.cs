using Accede.Service.Agents;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;

namespace Accede.Service.Api;

public static class TravelAgencyApi
{
    private const string UserId = "tiny-terry";

    public static void MapTravelAgencyApi(this WebApplication app)
    {
        app.MapChatApi();
    }

    // Maps the API which the Web UI uses to manage chat with the liaison.
    private static void MapChatApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat");

        group.MapGet("/stream", async (int? startIndex, IGrainFactory grains, HttpResponse response, CancellationToken cancellationToken) =>
        {
            var bufferingFeature = response.HttpContext.Features.Get<IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            response.Headers.Append("Content-Type", "text/event-stream");
            response.Headers.Append("Cache-Control", "no-cache, no-store");
            response.Headers.Append("Connection", "keep-alive");
            
            try
            {
                await response.WriteAsync($"event: connected\ndata: {{\"connected\": true}}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                
                var grain = grains.GetGrain<IUserLiaisonAgent>(UserId);
                await foreach (var message in grain.SubscribeAsync(startIndex ?? 0, cancellationToken))
                {
                    string serializedMessage = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
                    await response.WriteAsync($"data: {serializedMessage}\n\n", cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
                
                await response.WriteAsync($"event: complete\ndata: {{}}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when client disconnects, no action needed
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error in SSE stream.");
                
                try
                {
                    await response.WriteAsync($"event: error\ndata: {{\"message\":\"An error occurred\"}}\n\n", cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
                catch
                {
                    // Ignore write errors at this point
                }
            }
        });

        group.MapPost("/messages", async (Prompt prompt, IGrainFactory grains) =>
        {
            var id = UserId;
            await grains.GetGrain<IUserLiaisonAgent>(id).AddUserMessageAsync(new UserMessage(prompt.Text));

            return Results.Ok();
        });

        group.MapPost("/stream/cancel", async (IGrainFactory grains) =>
        {
            var id = UserId;
            await grains.GetGrain<IUserLiaisonAgent>(id).CancelAsync();

            return Results.Ok();
        });
    }
}

public record Prompt(string Text);
