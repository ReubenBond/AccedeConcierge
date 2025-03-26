using Accede.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var azModelsEndpoint = builder.AddParameterFromConfiguration("AzureAIInferenceEndpoint", "AzureAIInference:Endpoint");
var azModelsKey = builder.AddParameterFromConfiguration("AzureAIInferenceKey", "AzureAIInference:Key", secret: true);

var azOpenAiResource = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");

var reasoningModel = builder.AddAIModel("reasoning").AsAzureAIInference("DeepSeek-R1", azModelsEndpoint, azModelsKey);
var smallModel = builder.AddAIModel("small").AsAzureAIInference("Phi-4", azModelsEndpoint, azModelsKey);
var largeModel = builder.AddAIModel("large").AsAzureOpenAI("gpt-4o", o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var azureStorage = builder.AddAzureStorage("storage").RunAsEmulator(c =>
{
    c.WithDataBindMount();
    c.WithLifetime(ContainerLifetime.Persistent);
});

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering();

var backend = builder.AddProject<Projects.Accede_Service>("service")
    .WithReference(reasoningModel)
    .WithReference(smallModel)
    .WithReference(largeModel)
    .WaitFor(reasoningModel)
    .WaitFor(smallModel)
    .WaitFor(largeModel)
    .WithReference(orleans)
    .WithReference(azureStorage.AddBlobs("state"))
    .WithReference(azureStorage.AddBlobs("uploads"));

builder.AddNpmApp("webui", "../webui")
       .WithNpmPackageInstallation()
       .WithHttpEndpoint(env: "PORT", port: 35_369)
       .WithReverseProxy(backend.GetEndpoint("http"))
       .WithExternalHttpEndpoints()
       .WithOtlpExporter();

builder.Build().Run();
