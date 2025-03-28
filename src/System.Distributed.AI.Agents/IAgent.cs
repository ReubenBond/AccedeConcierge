using System.Distributed.DurableTasks;

namespace System.Distributed.AI.Agents;

public interface IAgent : IGrainWithStringKey;

public interface IChatAgent : IGrainWithStringKey
{
    DurableTask<ChatItem> SendRequestAsync(ChatItem chatItem, CancellationToken cancellationToken);
}
