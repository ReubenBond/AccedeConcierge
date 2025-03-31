// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Collections.ObjectModel;
using System.Distributed.DurableTasks;
using System.Reflection;
using System.Text.Json;
using Accede.Service.Utilities;
using Accede.Service.Utilities.Functions;
using Microsoft.Extensions.AI;
using Orleans.DurableTasks;

namespace System.Distributed.AI.Agents.Tools;

internal sealed class AgentAIFunction : AIFunction
{
    private static readonly SubscribeOrPollOptions SubscribeOrPollOptions = new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromSeconds(20) };
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
            var task = new Task<Task<object?>>(async () =>
            {
                var request = new ToolCallRequest
                {
                    Arguments = arguments?.ToArray() ?? [],
                    Context = new DurableTaskRequestContext
                    {
                        // We removed the implementation of push callbacks in Orleans.DurableTask impl during a refactor,
                        // Can uncomment this when it's re-implemented.
                        /*CallerId = GrainContext.GrainId, */
                        TargetId = GrainContext.GrainId
                    },
                    ToolName = FunctionDescriptor.Name
                };

                var taskId = TaskId.Create(DurableFunctionInvokingChatClient.CurrentContext!.CallContent.CallId);

                var scheduler = GrainContext.GetComponent<IDurableTaskGrainExtension>()!;

                // TODO: Use Polly for RPC resilience
                var response = await scheduler.ScheduleAsync(taskId, request, cancellationToken);
                while (!response.IsCompleted)
                {
                    // TODO: Use Polly for RPC resilience
                    response = await scheduler.SubscribeOrPollAsync(taskId, SubscribeOrPollOptions, cancellationToken);
                }

                return response.Result;
            });
            GrainContext.Scheduler.QueueTask(task);
            returnValue = await task.Unwrap();
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
