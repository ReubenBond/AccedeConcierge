using Accede.Service.Grains;
using Accede.Service.Models;
using Orleans.Journaling;
using System.Distributed.DurableTasks;

namespace Accede.Service.Agents;

internal interface IAdminAgent : IGrainWithStringKey
{
    ValueTask<List<TripRequest>> GetRequestsAsync(CancellationToken cancellationToken);
    ValueTask SubmitResultAsync(TripRequestResult result, CancellationToken cancellationToken);
    DurableTask<TripRequestResult> RequestApproval(TripRequest request);
}

public class AdminAgent(
    [FromKeyedServices("incoming-requests")] IDurableDictionary<string, TripRequest> requests,
    [FromKeyedServices("completed-requests")] IDurableSet<string> completedRequests) : DurableGrain, IAdminAgent
{
    public async DurableTask<TripRequestResult> RequestApproval(TripRequest request)
    {
        if (completedRequests.Add(request.RequestId))
        {
            requests.TryAdd(request.RequestId, request);
            await WriteStateAsync();
        }

        var completion = GrainFactory.GetGrain<IDurableTaskCompletionSourceGrain<TripRequestResult>>(request.RequestId);
        return await completion.GetResult();
    }

    public async ValueTask SubmitResultAsync(TripRequestResult result, CancellationToken cancellationToken)
    {
        var requestId = result.RequestId;
        cancellationToken.ThrowIfCancellationRequested();
        var completion = GrainFactory.GetGrain<IDurableTaskCompletionSourceGrain<TripRequestResult>>(requestId);
        if (await completion.TrySetResult(result))
        {
            if (!requests.Remove(requestId, out _))
            {
                return;
            }

            completedRequests.Add(requestId);
            await WriteStateAsync(cancellationToken);
        }
    }

    public ValueTask<List<TripRequest>> GetRequestsAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(requests.Values.ToList());
    }
}
