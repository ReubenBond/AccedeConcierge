// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Collections.ObjectModel;
using System.Distributed.AI.Agents;
using System.Distributed.DurableTasks;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Orleans.DurableTasks;

namespace Accede.Service.Utilities.Functions;

internal sealed class AgentAIFunction : AIFunction
{
    public static AgentAIFunction Create(IGrainContext grainContext, AgentToolDescriptor functionDescriptor, AgentToolOptions options)
    {
        return new(functionDescriptor, grainContext, options);
    }

    public static AgentAIFunction Create(IGrainContext grainContext, MethodInfo method, string? name, AgentToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainsGenericParameters)
        {
            throw new ArgumentException("Open generic methods are not supported", nameof(method));
        }

        AgentToolDescriptor functionDescriptor = AgentToolDescriptor.GetOrCreate(method, name, options);

        // Discover all descriptors
        // Register all descriptors in the grain context's AgentToolRegistry

        // If the descriptor describes a DurableTask, create a DurableTaskAIFunction descriptor which takes that descriptor

        return new(functionDescriptor, grainContext, options);
    }

    private AgentAIFunction(AgentToolDescriptor functionDescriptor, IGrainContext grainContext, AgentToolOptions options)
    {
        FunctionDescriptor = functionDescriptor;
        GrainContext = grainContext;
        _options = options;
        AdditionalProperties = ReadOnlyDictionary<string, object?>.Empty;
    }

    public AgentToolDescriptor FunctionDescriptor { get; }
    public IGrainContext GrainContext { get; }
    private readonly AgentToolOptions _options;
    public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
    public override string Name => FunctionDescriptor.Name;
    public override string Description => FunctionDescriptor.Description;
    public override MethodInfo UnderlyingMethod => FunctionDescriptor.Method;
    public override JsonElement JsonSchema => FunctionDescriptor.JsonSchema;
    public override JsonSerializerOptions JsonSerializerOptions => FunctionDescriptor.JsonSerializerOptions;
    protected override async Task<object?> InvokeCoreAsync(
        IEnumerable<KeyValuePair<string, object?>>? arguments,
        CancellationToken cancellationToken)
    {
        object? returnValue;
        if (FunctionDescriptor.IsDurableTask)
        {
            var request = new ToolCallRequest
            {
                Arguments = arguments?.ToArray() ?? [],
                Context = new DurableTaskRequestContext { CallerId = GrainContext.GrainId, TargetId = GrainContext.GrainId },
                ToolName = FunctionDescriptor.Name
            };

            var taskId = TaskId.Create(DurableFunctionInvokingChatClient.CurrentContext!.CallContent.CallId);

            var scheduler = GrainContext.GetComponent<IDurableTaskGrainExtension>()!;
            var response = await scheduler.ScheduleAsync(taskId, request, cancellationToken);
            var pollOptions = new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromSeconds(20) };
            while (true) {

                if (response.IsCompleted)
                {
                    returnValue = response.Result;
                    break;
                }

                response = await scheduler.SubscribeOrPollAsync(taskId, pollOptions, cancellationToken);
            }
        }
        else
        {
            object?[] args = FunctionDescriptor.MarshalParameters(arguments, cancellationToken);
            var task = new Task<object?>(() => AgentToolFactory.ReflectionInvoke(FunctionDescriptor.Method, GrainContext.GrainInstance, args));
            GrainContext.Scheduler.QueueTask(task);
            returnValue = await task.ConfigureAwait(false);
        }

        return await FunctionDescriptor.ReturnParameterMarshaller(returnValue, cancellationToken);
    }
}
