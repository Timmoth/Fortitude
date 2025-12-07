using System.Collections.Concurrent;
using Fortitude.Client;

namespace Fortitude.Server;

public class RequestTracker
{
    public int TotalRequests => Requests.Count;

    public ConcurrentQueue<FortitudeRequest> Requests { get; } = new();

    public event Action? OnUpdate;

    public void Add(FortitudeRequest req)
    {
        Requests.Enqueue(req);

        // limit queue
        while (Requests.Count > 1000)
            Requests.TryDequeue(out _);

        OnUpdate?.Invoke();
    }
}