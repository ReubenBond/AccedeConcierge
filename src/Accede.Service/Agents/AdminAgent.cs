using Accede.Service.Grains;
using Accede.Service.Models;
using Orleans.DurableTasks;
using Orleans.Journaling;
using System.Distributed.AI.Agents;
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
    [FromKeyedServices("completed-requests")] IDurableSet<string> processedRequests) : Agent, IAdminAgent
{
    public async DurableTask<TripRequestResult> RequestApproval(TripRequest request)
    {
        // If this request has not been processed yet, add it to the set of incoming requests. This
        // check means that any subsequent requests with the same ID can just wait for the result,
        // since we know that the request has been durably added to the processedRequests set.
        if (processedRequests.Add(request.RequestId))
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
        if (await completion.TrySetResult(result) && requests.Remove(requestId, out _))
        {
            await WriteStateAsync(cancellationToken);
        }
    }

    public ValueTask<List<TripRequest>> GetRequestsAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(requests.Values.ToList());
    }
}
