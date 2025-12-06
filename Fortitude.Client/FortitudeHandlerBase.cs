using System.Collections.Concurrent;

namespace Fortitude.Client;

public abstract class FortitudeHandlerBase
{
    public abstract bool Matches(FortitudeRequest request);
    public abstract Task<FortitudeResponse> BuildResponse(FortitudeRequest req);

    // New abstract methods for testing/assertions
    public abstract IReadOnlyList<FortitudeRequest> ReceivedRequests { get; }

    /// <summary>
    /// Waits asynchronously until a matching request has been received or times out.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <param name="predicate">Optional predicate to filter the requests</param>
    /// <returns>The matching request, or null if timeout</returns>
    public abstract Task<FortitudeRequest?> WaitForRequestAsync(int timeoutMs = 5000, Func<FortitudeRequest, bool>? predicate = null);
}
