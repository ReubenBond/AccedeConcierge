using Accede.Service.Utilities.Functions;
using Microsoft.Extensions.AI;
using System.Distributed.AI.Agents;
using System.Distributed.AI.Agents.Tools;
using System.Reflection;

namespace Accede.Service.Agents;

internal sealed class AgentToolRegistry
{
    private readonly IGrainContext _grainContext;
    private readonly AgentToolOptions _options;
    private readonly Dictionary<string, AgentToolDescriptor> _toolDescriptors = [];
    private readonly List<AITool> _tools = [];
    private AgentToolRegistry(Type agentType, IGrainContext grainContext, AgentToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(grainContext);
        ArgumentNullException.ThrowIfNull(options);
        _grainContext = grainContext;
        _options = options;

        foreach (var method in agentType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AddTool(method, target: _grainContext.GrainInstance, toolName: null, attributeRequired: true);
        }
    }

    private void AddTool(MethodInfo method, object? target, string? toolName, bool attributeRequired)
    {
        var attribute = method.GetCustomAttribute<ToolAttribute>();
        if (attribute is null && attributeRequired)
        {
            return;
        }

        var name = toolName ?? attribute?.Name ?? method.Name;
        var descriptor = AgentToolDescriptor.GetOrCreate(method, name, _options);
        if (!_toolDescriptors.TryAdd(name, descriptor))
        {
            throw new InvalidOperationException($"A tool named '{name}' already exists on '{_grainContext}'. Tool names must be unique.");
        }

        _tools.Add(AgentToolFactory.Create(_grainContext, descriptor, _options));
    }

    public IList<AITool> Tools => _tools.AsReadOnly();

    public static AgentToolRegistry Create(Type agentType, IGrainContext grainContext, AgentToolOptions options) => new(agentType, grainContext, options);

    public void AddTool(Delegate toolFunc, string? toolName)
    {
        AddTool(toolFunc.Method, toolFunc.Target, toolName, attributeRequired: false);
    }

    internal AgentToolDescriptor GetToolDescriptor(string name)
    {
        if (!_toolDescriptors.TryGetValue(name, out var result))
        {
            throw new KeyNotFoundException($"No tool named '{name}' is registered.");
        }

        return result;
    }
}
