namespace Accede.Service.Utilities;

internal sealed class AsyncManualResetEvent
{
    private readonly object _lock = new();
    private TaskCompletionSource _event = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (_lock)
        {
            completion = _event;
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    public void SignalAndReset()
    {
        TaskCompletionSource completion;

        lock (_lock)
        {
            completion = _event;
            _event = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        completion.TrySetResult();
    }

    public void Cancel()
    {
        TaskCompletionSource completion;

        lock (_lock)
        {
            completion = _event;
        }

        completion.TrySetCanceled();
    }
}
