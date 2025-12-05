using System.Collections.Concurrent;
using Fortitude.Client;
using Microsoft.AspNetCore.SignalR;

namespace Fortitude.Server;

public class FortitudeHub(PendingRequestStore store, ILogger<FortitudeHub> logger) : Hub
{
    private readonly PendingRequestStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<FortitudeHub> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public static ConcurrentDictionary<string, string> ConnectedClients { get; } = new();

    public Task SubmitResponse(FortitudeResponse response)
    {
        _logger.LogInformation("Received response for RequestId {RequestId}", response.RequestId);
        _store.Complete(response);
        return Task.CompletedTask;
    }

    public override Task OnConnectedAsync()
    {
        ConnectedClients[Context.ConnectionId] = Context.ConnectionId;
        _logger.LogInformation("Client connected: {ConnectionId}. Total connected: {Count}",
            Context.ConnectionId, ConnectedClients.Count);

        _logger.LogDebug("Current clients: {Clients}", string.Join(", ", ConnectedClients.Keys));

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectedClients.TryRemove(Context.ConnectionId, out _);
        _logger.LogInformation("Client disconnected: {ConnectionId}. Total connected: {Count}",
            Context.ConnectionId, ConnectedClients.Count);

        if (exception != null)
        {
            _logger.LogWarning(exception, "Client {ConnectionId} disconnected due to error", Context.ConnectionId);
        }

        _logger.LogDebug("Current clients: {Clients}", string.Join(", ", ConnectedClients.Keys));

        return base.OnDisconnectedAsync(exception);
    }
}