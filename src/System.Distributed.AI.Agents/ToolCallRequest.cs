using Accede.Service.Agents;
using Accede.Service.Utilities.Functions;
using Orleans.CodeGeneration;
using Orleans.DurableTasks;
using Orleans.Serialization.Invocation;
using System.Distributed.DurableTasks;
using System.Reflection;

namespace System.Distributed.AI.Agents;

[GenerateSerializer]
internal sealed class ToolCallRequest : IDurableTaskRequest
{
    [Id(0)]
    public string? ToolName { get; set; }

    [Id(1)]
    public KeyValuePair<string, object?>[]? Arguments { get; set; }

    [NonSerialized]
    private IGrainContext? _grainContext;

    [NonSerialized]
    private object? _instance;

    [field: NonSerialized]
    public DurableTaskRequestContext? Context { get; set; }

    [field: NonSerialized]
    public InvokeMethodOptions Options { get; private set; }

    public void AddInvokeMethodOptions(InvokeMethodOptions options)
    {
        Options |= options;
    }

    public DurableTask CreateTask()
    {
        if (_grainContext is null)
        {
            throw new InvalidOperationException($"{nameof(SetTarget)} must be called before {nameof(CreateTask)}.");
        }

        var toolRegistry = _grainContext!.GetComponent<AgentToolRegistry>();
        if (toolRegistry is null)
        {
            throw new InvalidOperationException($"Could not get {nameof(AgentToolRegistry)} from grain context '{_grainContext}'.");
        }

        if (ToolName is null)
        {
            throw new InvalidOperationException($"{nameof(ToolName)} must be set before calling {nameof(CreateTask)}.");
        }

        // Call the method to get the DurableTask instance.
        var descriptor = toolRegistry.GetToolDescriptor(ToolName!);
        object?[] args = descriptor.MarshalParameters(Arguments!, CancellationToken.None);
        var task = AgentToolFactory.ReflectionInvoke(descriptor.Method, _instance, args);
        if (task is not DurableTask)
        {
            throw new InvalidOperationException($"Expected method '{descriptor.Method.Name}' to return a {nameof(DurableTask)}.");
        }

        return (DurableTask)task;
    }

    public void Dispose()
    {
    }

    public string GetActivityName() => ToolName! ?? nameof(ToolCallRequest);

    public object? GetArgument(int index) => Arguments![index];

    public int GetArgumentCount() => Arguments!.Length;

    public string GetInterfaceName() => nameof(ToolCallRequest);

    public Type GetInterfaceType() => null!;

    public MethodInfo GetMethod() => null!;

    public string GetMethodName() => ToolName!;

    public object? GetTarget() => _instance;

    public ValueTask<Response> Invoke()
    {
        throw new NotImplementedException();
    }

    public void SetArgument(int index, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Arguments![index] = (KeyValuePair<string, object?>)value;
    }

    public void SetTarget(ITargetHolder holder)
    {
        _grainContext = holder.GetComponent<IGrainContext>()!;
        if (_grainContext is null)
        {
            throw new InvalidOperationException($"Could not get {nameof(IGrainContext)} from target holder '{holder}'.");
        }

        _instance = _grainContext.GrainInstance;
        if (_instance is null)
        {
            throw new InvalidOperationException($"{nameof(IGrainContext)} has no grain instance set.");
        }
    }
}
