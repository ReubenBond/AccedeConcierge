using Accede.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var azModelsEndpoint = builder.AddParameterFromConfiguration("AzureAIInferenceEndpoint", "AzureAIInference:Endpoint");
var azModelsKey = builder.AddParameterFromConfiguration("AzureAIInferenceKey", "AzureAIInference:Key", secret: true);

var azOpenAiResource = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");

var reasoningModel = builder.AddAIModel("reasoning").AsAzureAIInference("DeepSeek-R1", azModelsEndpoint, azModelsKey);
var smallModel = builder.AddAIModel("small").AsAzureAIInference("Phi-4", azModelsEndpoint, azModelsKey);
var largeModelName = "gpt-4o";
IResourceBuilder<IResourceWithConnectionString> largeModel;
switch (builder.ExecutionContext.IsPublishMode)
{
    case true:
        var aoai = builder.AddAzureOpenAI("large-ai");
        aoai.AddDeployment(largeModelName, largeModelName, "2024-11-20");
        largeModel = aoai;
        break;
    case false:
        largeModel = builder.AddAIModel("large").AsAzureOpenAI(largeModelName, o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));
        break;
}

var azureStorage = builder.AddAzureStorage("storage").RunAsEmulator(c =>
{
    c.WithDataBindMount();
    c.WithLifetime(ContainerLifetime.Persistent);
});

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering();

var mcpServer = builder.AddProject<Projects.MCPServer>("mcpserver");

var backend = builder.AddProject<Projects.Accede_Service>("service")
    .WithReference(mcpServer)
    .WithReference(reasoningModel)
    .WithReference(smallModel)
    .WithReference(largeModel)
    .WithEnvironment("large__DeploymentName", largeModelName)
    .WithEnvironment("large__ProviderName", "AzureOpenAI")
    .WaitFor(mcpServer)
    .WaitFor(reasoningModel)
    .WaitFor(smallModel)
    .WaitFor(largeModel)
    .WithReference(orleans)
    .WithReference(azureStorage.AddBlobs("state"))
    .WithReference(azureStorage.AddBlobs("uploads"));

builder.AddNpmApp("webui", "../webui")
       .WithNpmPackageInstallation()
       .WithHttpEndpoint(env: "PORT", port: 35_369, isProxied: false)
       .WithEnvironment("BACKEND_URL", backend.GetEndpoint("http"))
       .WithExternalHttpEndpoints()
       .WithOtlpExporter()
       .PublishAsDockerFile();

builder.Build().Run();
