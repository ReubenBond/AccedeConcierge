using Accede.Service.Agents;

namespace System.Distributed.AI.Agents;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute : Attribute
{
    public string? Name { get; }
}
