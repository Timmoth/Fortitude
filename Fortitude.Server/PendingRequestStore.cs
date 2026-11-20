using System.Collections.Concurrent;
using Fortitude.Client;

namespace Fortitude.Server;

/// <summary>
///     Stores pending Fortitude requests and allows waiting for and completing responses.
///     Thread-safe and suitable for concurrent use.
/// </summary>
public class PendingRequestStore
{
    private readonly ILogger<PendingRequestStore>? _logger;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FortitudeResponse>> _waiting
        = new();

    /// <summary>
    ///     Initializes a new instance of <see cref="PendingRequestStore" />.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    public PendingRequestStore(ILogger<PendingRequestStore>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Waits asynchronously for the response for a given request ID, with optional timeout.
    /// </summary>
    /// <param name="id">The request ID.</param>
    /// <param name="timeout">
    ///     Optional timeout for waiting. Defaults to <see cref="Settings.DefaultResponseTimeoutSeconds" />;
    ///     callers on the request path pass the configured <see cref="Settings.ResponseTimeout" /> instead.
    /// </param>
    /// <returns>The response from the client.</returns>
    /// <exception cref="TimeoutException">Thrown if the response does not arrive within the timeout period.</exception>
    public async Task<FortitudeResponse> WaitForResponse(Guid id, TimeSpan? timeout = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Request ID cannot be empty.", nameof(id));

        var tcs = new TaskCompletionSource<FortitudeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_waiting.TryAdd(id, tcs))
        {
            _logger?.LogWarning("A pending request with ID {RequestId} already exists. Overwriting.", id);
            _waiting[id] = tcs;
        }

        timeout ??= TimeSpan.FromSeconds(Settings.DefaultResponseTimeoutSeconds);

        // The timer is cancelled as soon as the response arrives. Left running, every
        // completed request would keep a timer alive for the full timeout, so a server under
        // load carries one pending timer per request it has already answered.
        using var timeoutCts = new CancellationTokenSource();

        var delayTask = Task.Delay(timeout.Value, timeoutCts.Token);
        var completedTask = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

        // Remove the TCS to prevent memory leak
        _waiting.TryRemove(id, out _);

        if (completedTask == delayTask)
        {
            _logger?.LogWarning("Timeout waiting for response for request {RequestId}", id);
            throw new TimeoutException(
                $"Response for request {id} timed out after {timeout.Value.TotalSeconds} seconds.");
        }

        await timeoutCts.CancelAsync().ConfigureAwait(false);

        // Return the completed response
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    ///     Completes the pending request with the given response.
    /// </summary>
    /// <param name="response">The response to complete the request.</param>
    public void Complete(FortitudeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (_waiting.TryRemove(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
            _logger?.LogDebug("Completed pending request {RequestId}", response.RequestId);
        }
        else
        {
            _logger?.LogWarning("No pending request found for Response {RequestId}", response.RequestId);
        }
    }
}