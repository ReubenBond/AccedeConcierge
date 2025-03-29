using Microsoft.Extensions.DependencyInjection;
using System.Distributed.DurableTasks;

namespace System.Distributed.AI.Agents;

public interface IAgent : IGrainWithStringKey;

public interface IChatAgent : IGrainWithStringKey
{
    DurableTask<ChatItem> SendRequestAsync(ChatItem chatItem);
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MemoryAttribute(string name) : FromKeyedServicesAttribute(name);
