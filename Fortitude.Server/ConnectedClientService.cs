using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Fortitude.Server;

public class ConnectedClientService
{
    private readonly ILogger<ConnectedClientService> _logger;

    /// <summary>
    /// Thread-safe dictionary of connected clients.
    /// Key and value are ConnectionId. Could hold metadata later.
    /// </summary>
    public ConcurrentDictionary<string, string> Clients { get; } = new();

    public int Count => Clients.Count;

    public event Action? OnChanged;

    public ConnectedClientService(ILogger<ConnectedClientService> logger)
    {
        _logger = logger;
    }

    public void Add(string connectionId)
    {
        Clients[connectionId] = connectionId;

        _logger.LogInformation("Client connected: {ConnectionId}. Total connected: {Count}",
            connectionId, Count);

        OnChanged?.Invoke();
    }

    public void Remove(string connectionId)
    {
        Clients.TryRemove(connectionId, out _);

        _logger.LogInformation("Client disconnected: {ConnectionId}. Total connected: {Count}",
            connectionId, Count);

        OnChanged?.Invoke();
    }
}