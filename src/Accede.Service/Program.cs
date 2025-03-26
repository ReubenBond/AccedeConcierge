using Orleans.Runtime.DurableTasks;
using Orleans.Journaling;
using Orleans.Journaling.DurableTasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.AI;
using Accede.Service.Utilities;
using Orleans.Serialization;
using Accede.Service.Api;
using Accede.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureBlobClient("state");
builder.AddKeyedAzureTableClient("clustering");
builder.AddKeyedAzureBlobClient("uploads");

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddDurableTasks();
    siloBuilder.AddStateMachineStorage();
    siloBuilder.AddJournaledDurableTaskStorage();
    siloBuilder.Services.AddSerializer(s => s.AddJsonSerializer(t => t.Assembly.Equals(typeof(ChatMessage).Assembly)));
    siloBuilder.AddAzureAppendBlobStateMachineStorage(optionsBuilder =>
    {
        optionsBuilder.Configure((AzureAppendBlobStateMachineStorageOptions options, IServiceProvider serviceProvider) =>
        {
            options.ConfigureBlobServiceClient(ct => Task.FromResult(serviceProvider.GetRequiredKeyedService<BlobServiceClient>("state")));
        });
    });
});

builder.AddKeyedChatClient("reasoning").UseDurableFunctionInvocation(configure: c => c.AllowConcurrentInvocation = true);
builder.AddKeyedChatClient("large").UseDurableFunctionInvocation(configure: c => c.AllowConcurrentInvocation = true);
builder.AddKeyedChatClient("small").UseDurableFunctionInvocation(configure: c => c.AllowConcurrentInvocation = true);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.MapTravelAgencyApi();

app.MapDefaultEndpoints();

app.Run();
