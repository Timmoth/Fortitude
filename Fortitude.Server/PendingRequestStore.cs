using System.Collections.Concurrent;
using Fortitude.Client;

namespace Fortitude.Server
{
    /// <summary>
    /// Stores pending Fortitude requests and allows waiting for and completing responses.
    /// Thread-safe and suitable for concurrent use.
    /// </summary>
    public class PendingRequestStore
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FortitudeResponse>> _waiting 
            = new();

        private readonly ILogger<PendingRequestStore>? _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="PendingRequestStore"/>.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic messages.</param>
        public PendingRequestStore(ILogger<PendingRequestStore>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Waits asynchronously for the response for a given request ID, with optional timeout.
        /// </summary>
        /// <param name="id">The request ID.</param>
        /// <param name="timeout">Optional timeout for waiting (default: 30 seconds).</param>
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

            timeout ??= TimeSpan.FromSeconds(30);

            var delayTask = Task.Delay(timeout.Value);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            // Remove the TCS to prevent memory leak
            _waiting.TryRemove(id, out _);

            if (completedTask == delayTask)
            {
                _logger?.LogWarning("Timeout waiting for response for request {RequestId}", id);
                throw new TimeoutException($"Response for request {id} timed out after {timeout.Value.TotalSeconds} seconds.");
            }

            // Return the completed response
            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Completes the pending request with the given response.
        /// </summary>
        /// <param name="response">The response to complete the request.</param>
        public void Complete(FortitudeResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

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
}
