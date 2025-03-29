using Accede.Service.Agents;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
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
                await foreach (var message in grain.WatchChatHistoryAsync(startIndex ?? 0, cancellationToken))
                {
                    string serializedMessage = JsonSerializer.Serialize(message, message.GetType(), JsonSerializerOptions.Web);
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

        group.MapPost("/messages", async (HttpRequest request, [FromKeyedServices("uploads")] BlobServiceClient blobServiceClient, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var id = UserId;
            var uploads = await GetFileUploads(id, request, blobServiceClient, cancellationToken);
            var grain = grains.GetGrain<IUserLiaisonAgent>(id);
            var text = request.Form["Text"].FirstOrDefault() ?? "";
            await grain.PostMessageAsync(new UserMessage(text)
            {
                Attachments = uploads,
                Id = Guid.NewGuid().ToString()
            },
            cancellationToken);
            return Results.Ok();
        });

        group.MapPost("/stream/cancel", async (IGrainFactory grains) =>
        {
            var id = UserId;
            await grains.GetGrain<IUserLiaisonAgent>(id).CancelAsync();

            return Results.Ok();
        });

        group.MapPost("/select-itinerary", async (SelectItineraryRequest request, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            try 
            {
                var id = UserId;
                var grain = grains.GetGrain<IUserLiaisonAgent>(id);
                await grain.SelectItineraryAsync(request.MessageId, request.OptionId, cancellationToken);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error selecting itinerary");
                return Results.Problem("Error selecting itinerary", statusCode: 500);
            }
        });
    }

    private static async Task<List<UriAttachment>> GetFileUploads(string userId, HttpRequest request, BlobServiceClient blobServiceClient, CancellationToken cancellationToken)
    {
        List<UriAttachment> results = [];
        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            foreach (var file in request.Form.Files)
            {
                if (blobServiceClient.AccountName == "devstoreaccount1")
                {
                    results.Add(new(await ConvertToDataUri(file.OpenReadStream(), file.ContentType, cancellationToken), file.ContentType));
                }
                else
                {
                    results.Add(await UploadToBlobContainerAsync(userId, blobServiceClient, file, cancellationToken));
                }
            }
        }

        return results;
    }

    private static async Task<UriAttachment> UploadToBlobContainerAsync(string userId, BlobServiceClient blobServiceClient, IFormFile file, CancellationToken cancellationToken)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient("user-content");
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var extension = Path.GetExtension(file.FileName) ?? "jpg";
        var blobClient = containerClient.GetBlobClient($"{userId}/{Guid.CreateVersion7():N}.{extension}");
        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerClient.Name,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.MaxValue,
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return new(blobClient.GenerateSasUri(sasBuilder).ToString(), file.ContentType);
    }

    private static async ValueTask<string> ConvertToDataUri(Stream stream, string contentType, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        var base64 = Convert.ToBase64String(memoryStream.ToArray());
        return $"data:{contentType};base64,{base64}";
    }

    public record SelectItineraryRequest(string MessageId, string OptionId);
}
