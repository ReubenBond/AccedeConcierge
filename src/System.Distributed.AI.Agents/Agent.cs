using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableTasks;
using Orleans.Journaling;

namespace System.Distributed.AI.Agents;
public abstract class Agent : DurableGrain
{
    protected Agent()
    {
        // HACK: A current limitation of journaled storage is that it does not support registering state machines after construction.
        // This is a workaround to ensure that the storage provider is registered before any state machines are created.
        _ = ServiceProvider.GetService<IDurableTaskGrainStorage>();
    }
}
