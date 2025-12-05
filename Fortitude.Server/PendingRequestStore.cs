using System.Collections.Concurrent;
using Fortitude.Client;

namespace Fortitude.Server;

public class PendingRequestStore
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FortitudeResponse>> _waiting 
        = new();

    /// <summary>
    /// Waits for the response for a given request id, with optional timeout.
    /// </summary>
    /// <param name="id">Request ID</param>
    /// <param name="timeout">Timeout for waiting. Default 30 seconds.</param>
    public async Task<FortitudeResponse> WaitForResponse(Guid id, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<FortitudeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = tcs;

        timeout ??= TimeSpan.FromSeconds(30);

        var delayTask = Task.Delay(timeout.Value);
        var completedTask = await Task.WhenAny(tcs.Task, delayTask);

        // Remove the TCS from dictionary to prevent memory leak
        _waiting.TryRemove(id, out _);

        if (completedTask == delayTask)
        {
            throw new TimeoutException($"Response for request {id} timed out after {timeout.Value.TotalSeconds} seconds.");
        }

        return await tcs.Task; // already completed
    }

    /// <summary>
    /// Completes the pending request with the given response
    /// </summary>
    public void Complete(FortitudeResponse response)
    {
        if (_waiting.TryRemove(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }
}