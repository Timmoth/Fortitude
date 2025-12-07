using System.Collections.Concurrent;
using Fortitude.Client;

namespace Fortitude.Server;

public class RequestTracker
{
    // Limit how many requests we keep in memory
    private const int MaxRequests = 1000;

    // Queue of requests
    public ConcurrentQueue<FortitudeRequest> Requests { get; } = new();

    // Map requestId -> response
    private readonly ConcurrentDictionary<Guid, FortitudeResponse> _responses = new();

    public event Action? OnUpdate;

    // Total requests seen
    public int TotalRequests => Requests.Count;

    // Add a request
    public void Add(FortitudeRequest req)
    {
        Requests.Enqueue(req);

        // Limit queue size
        while (Requests.Count > MaxRequests)
            Requests.TryDequeue(out _);

        OnUpdate?.Invoke();
    }

    // Add a response
    public void Add(FortitudeResponse response)
    {
        if (response == null) return;

        // Store response in dictionary keyed by RequestId
        _responses[response.RequestId] = response;

        OnUpdate?.Invoke();
    }

    // Get response for a request (null if not yet received)
    public FortitudeResponse? GetResponse(Guid requestId)
    {
        _responses.TryGetValue(requestId, out var response);
        return response;
    }

    public void Clear()
    {
        Requests.Clear();
        _responses.Clear();
    }
}