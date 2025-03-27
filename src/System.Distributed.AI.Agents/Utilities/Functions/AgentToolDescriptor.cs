// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Distributed.DurableTasks;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Accede.Service.Utilities.Functions;

/// <summary>
/// A descriptor for a .NET method-backed AIFunction that precomputes its marshalling delegates and JSON schema.
/// </summary>
internal sealed class AgentToolDescriptor
{
    private const int InnerCacheSoftLimit = 512;
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<DescriptorKey, AgentToolDescriptor>> _descriptorCache = new();

    /// <summary>A boxed <see cref="CancellationToken.None"/>.</summary>
    private static readonly object? _boxedDefaultCancellationToken = default(CancellationToken);

    /// <summary>
    /// Gets or creates a descriptors using the specified method and options.
    /// </summary>
    public static AgentToolDescriptor GetOrCreate(MethodInfo method, string? name, AgentToolOptions options)
    {
        JsonSerializerOptions serializerOptions = options.SerializerOptions ?? AIJsonUtilities.DefaultOptions;
        AIJsonSchemaCreateOptions schemaOptions = options.JsonSchemaCreateOptions ?? AIJsonSchemaCreateOptions.Default;
        serializerOptions.MakeReadOnly();
        ConcurrentDictionary<DescriptorKey, AgentToolDescriptor> innerCache = _descriptorCache.GetOrCreateValue(serializerOptions);

        DescriptorKey key = new(method, name, schemaOptions);
        if (innerCache.TryGetValue(key, out AgentToolDescriptor? descriptor))
        {
            return descriptor;
        }

        descriptor = new(key, serializerOptions);
        return innerCache.Count < InnerCacheSoftLimit
            ? innerCache.GetOrAdd(key, descriptor)
            : descriptor;
    }

    private AgentToolDescriptor(DescriptorKey key, JsonSerializerOptions serializerOptions)
    {
        // Get marshaling delegates for parameters.
        ParameterInfo[] parameters = key.Method.GetParameters();
        ParameterMarshallers = new Func<IReadOnlyDictionary<string, object?>, CancellationToken, object?>[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterMarshallers[i] = GetParameterMarshaller(serializerOptions, parameters[i]);
        }

        // Get a marshaling delegate for the return value.
        ReturnParameterMarshaller = GetReturnParameterMarshaller(key.Method, serializerOptions);

        Method = key.Method;
        Name = key.Name ?? GetFunctionName(key.Method);
        Description = key.Method.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description ?? string.Empty;
        JsonSerializerOptions = serializerOptions;
        JsonSchema = AIJsonUtilities.CreateFunctionJsonSchema(
            key.Method,
            Name,
            Description,
            serializerOptions,
            key.SchemaOptions);
        IsDurableTask = IsDurableTaskMethod(key.Method);
    }

    public string Name { get; }
    public string Description { get; }
    public bool IsDurableTask { get; }
    public MethodInfo Method { get; }
    public JsonSerializerOptions JsonSerializerOptions { get; }
    public JsonElement JsonSchema { get; }
    public Func<IReadOnlyDictionary<string, object?>, CancellationToken, object?>[] ParameterMarshallers { get; }
    public Func<object?, CancellationToken, Task<object?>> ReturnParameterMarshaller { get; }
    public AgentAIFunction? CachedDefaultInstance { get; set; }

    public object?[] MarshalParameters(IEnumerable<KeyValuePair<string, object?>>? arguments, CancellationToken cancellationToken)
    {
        var paramMarshallers = ParameterMarshallers;
        object?[] args = paramMarshallers.Length != 0 ? new object?[paramMarshallers.Length] : [];

        IReadOnlyDictionary<string, object?> argDict =
            arguments is null || args.Length == 0 ? ReadOnlyDictionary<string, object?>.Empty :
            arguments as IReadOnlyDictionary<string, object?> ??
            arguments.
#if NET8_0_OR_GREATER
                ToDictionary();
#else
                    ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
#endif
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = paramMarshallers[i](argDict, cancellationToken);
        }

        return args;
    }

    private static string GetFunctionName(MethodInfo method)
    {
        // Get the function name to use.
        string name = AgentToolFactory.SanitizeMemberName(method.Name);

        const string AsyncSuffix = "Async";
        if (IsAsyncMethod(method) &&
            name.EndsWith(AsyncSuffix, StringComparison.Ordinal) &&
            name.Length > AsyncSuffix.Length)
        {
            name = name.Substring(0, name.Length - AsyncSuffix.Length);
        }

        return name;

        static bool IsAsyncMethod(MethodInfo method)
        {
            Type t = method.ReturnType;

            if (t == typeof(Task) || t == typeof(ValueTask) || t == typeof(DurableTask))
            {
                return true;
            }

            if (t.IsGenericType)
            {
                t = t.GetGenericTypeDefinition();
                if (t == typeof(Task<>) || t == typeof(ValueTask<>) || t == typeof(DurableTask<>) || t == typeof(IAsyncEnumerable<>))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool IsDurableTaskMethod(MethodInfo method)
    {
        Type t = method.ReturnType;

        if (t == typeof(DurableTask))
        {
            return true;
        }

        if (t.IsGenericType)
        {
            t = t.GetGenericTypeDefinition();
            if (t == typeof(DurableTask<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a delegate for handling the marshaling of a parameter.
    /// </summary>
    private static Func<IReadOnlyDictionary<string, object?>, CancellationToken, object?> GetParameterMarshaller(
        JsonSerializerOptions serializerOptions,
        ParameterInfo parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter.Name))
        {
            throw new ArgumentException("Parameter is missing a name.", nameof(parameter));
        }

        // Resolve the contract used to marshal the value from JSON -- can throw if not supported or not found.
        Type parameterType = parameter.ParameterType;
        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(parameterType);

        // For CancellationToken parameters, we always bind to the token passed directly to InvokeAsync.
        if (parameterType == typeof(CancellationToken))
        {
            return static (_, cancellationToken) =>
                cancellationToken == default ? _boxedDefaultCancellationToken : // optimize common case of a default CT to avoid boxing
                cancellationToken;
        }

        // For all other parameters, create a marshaller that tries to extract the value from the arguments dictionary.
        return (arguments, _) =>
        {
            // If the parameter has an argument specified in the dictionary, return that argument.
            if (arguments.TryGetValue(parameter.Name, out object? value))
            {
                return value switch
                {
                    null => null, // Return as-is if null -- if the parameter is a struct this will be handled by MethodInfo.Invoke
                    _ when parameterType.IsInstanceOfType(value) => value, // Do nothing if value is assignable to parameter type
                    JsonElement element => JsonSerializer.Deserialize(element, typeInfo),
                    JsonDocument doc => JsonSerializer.Deserialize(doc, typeInfo),
                    JsonNode node => JsonSerializer.Deserialize(node, typeInfo),
                    _ => MarshallViaJsonRoundtrip(value),
                };

                object? MarshallViaJsonRoundtrip(object value)
                {
#pragma warning disable CA1031 // Do not catch general exception types
                    try
                    {
                        string json = JsonSerializer.Serialize(value, serializerOptions.GetTypeInfo(value.GetType()));
                        return JsonSerializer.Deserialize(json, typeInfo);
                    }
                    catch
                    {
                        // Eat any exceptions and fall back to the original value to force a cast exception later on.
                        return value;
                    }
#pragma warning restore CA1031
                }
            }

            // There was no argument for the parameter in the dictionary.
            // Does it have a default value?
            if (parameter.HasDefaultValue)
            {
                return parameter.DefaultValue;
            }

            // Leave it empty.
            return null;
        };
    }

    /// <summary>
    /// Gets a delegate for handling the result value of a method, converting it into the <see cref="Task{FunctionResult}"/> to return from the invocation.
    /// </summary>
    private static Func<object?, CancellationToken, Task<object?>> GetReturnParameterMarshaller(MethodInfo method, JsonSerializerOptions serializerOptions)
    {
        Type returnType = method.ReturnType;
        JsonTypeInfo returnTypeInfo;

        // Void
        if (returnType == typeof(void))
        {
            return static (_, _) => Task.FromResult<object?>(null);
        }

        // Task
        if (returnType == typeof(Task))
        {
            return async static (result, _) =>
            {
                await ((Task)ThrowIfNullResult(result)).ConfigureAwait(false);
                return null;
            };
        }

        // ValueTask
        if (returnType == typeof(ValueTask))
        {
            return async static (result, _) =>
            {
                await ((ValueTask)ThrowIfNullResult(result)).ConfigureAwait(false);
                return null;
            };
        }

        // DurableTask
        if (returnType == typeof(DurableTask))
        {
            return static (result, cancellationToken) =>
            {
                return Task.FromResult<object?>(null);
            };
        }

        if (returnType.IsGenericType)
        {
            // Task<T>
            if (returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                MethodInfo taskResultGetter = GetMethodFromGenericMethodDefinition(returnType, _taskGetResult);
                returnTypeInfo = serializerOptions.GetTypeInfo(taskResultGetter.ReturnType);
                return async (taskObj, cancellationToken) =>
                {
                    await ((Task)ThrowIfNullResult(taskObj)).ConfigureAwait(false);
                    object? result = AgentToolFactory.ReflectionInvoke(taskResultGetter, taskObj, null);
                    return await SerializeResultAsync(result, returnTypeInfo, cancellationToken).ConfigureAwait(false);
                };
            }

            // ValueTask<T>
            if (returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                MethodInfo valueTaskAsTask = GetMethodFromGenericMethodDefinition(returnType, _valueTaskAsTask);
                MethodInfo asTaskResultGetter = GetMethodFromGenericMethodDefinition(valueTaskAsTask.ReturnType, _taskGetResult);
                returnTypeInfo = serializerOptions.GetTypeInfo(asTaskResultGetter.ReturnType);
                return async (taskObj, cancellationToken) =>
                {
                    var task = (Task)AgentToolFactory.ReflectionInvoke(valueTaskAsTask, ThrowIfNullResult(taskObj), null)!;
                    await task.ConfigureAwait(false);
                    object? result = AgentToolFactory.ReflectionInvoke(asTaskResultGetter, task, null);
                    return await SerializeResultAsync(result, returnTypeInfo, cancellationToken).ConfigureAwait(false);
                };
            }

            // DurableTask<T>
            if (returnType.GetGenericTypeDefinition() == typeof(DurableTask<>))
            {
                returnTypeInfo = serializerOptions.GetTypeInfo(returnType.GetGenericArguments()[0]);
                return async (result, cancellationToken) =>
                {
                    return await SerializeResultAsync(result, returnTypeInfo, cancellationToken).ConfigureAwait(false);
                };
            }
        }

        // For everything else, just serialize the result as-is.
        returnTypeInfo = serializerOptions.GetTypeInfo(returnType);
        return (result, cancellationToken) => SerializeResultAsync(result, returnTypeInfo, cancellationToken);

        static async Task<object?> SerializeResultAsync(object? result, JsonTypeInfo returnTypeInfo, CancellationToken cancellationToken)
        {
            if (returnTypeInfo.Kind is JsonTypeInfoKind.None)
            {
                // Special-case trivial contracts to avoid the more expensive general-purpose serialization path.
                return JsonSerializer.SerializeToElement(result, returnTypeInfo);
            }

            // Serialize asynchronously to support potential IAsyncEnumerable responses.
            using AgentToolFactory.PooledMemoryStream stream = new();
            await JsonSerializer.SerializeAsync(stream, result, returnTypeInfo, cancellationToken).ConfigureAwait(false);
            Utf8JsonReader reader = new(stream.GetBuffer());
            return JsonElement.ParseValue(ref reader);
        }

        // Throws an exception if a result is found to be null unexpectedly
        static object ThrowIfNullResult(object? result) => result ?? throw new InvalidOperationException("Function returned null unexpectedly.");
    }

    private static readonly MethodInfo _taskGetResult = typeof(Task<>).GetProperty(nameof(Task<int>.Result), BindingFlags.Instance | BindingFlags.Public)!.GetMethod!;
    private static readonly MethodInfo _valueTaskAsTask = typeof(ValueTask<>).GetMethod(nameof(ValueTask<int>.AsTask), BindingFlags.Instance | BindingFlags.Public)!;

    private static MethodInfo GetMethodFromGenericMethodDefinition(Type specializedType, MethodInfo genericMethodDefinition)
    {
        Debug.Assert(specializedType.IsGenericType && specializedType.GetGenericTypeDefinition() == genericMethodDefinition.DeclaringType, "generic member definition doesn't match type.");
#if NET
        return (MethodInfo)specializedType.GetMemberWithSameMetadataDefinitionAs(genericMethodDefinition);
#else
#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
#pragma warning restore S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
            return specializedType.GetMethods(All).First(m => m.MetadataToken == genericMethodDefinition.MetadataToken);
#endif
    }

    private record struct DescriptorKey(MethodInfo Method, string? Name, AIJsonSchemaCreateOptions SchemaOptions);
}
