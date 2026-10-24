using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Fortitude.Server;

/// <summary>
///     Tracks connected clients and manages per-client port reservations.
///     When a client connects, it is automatically assigned an available port.
///     When the client disconnects, the port is released.
/// </summary>
public class ConnectedClientService
{
    /// <summary>
    ///     Thread-safe dictionary of connected clients.
    ///     Key = Connection ID
    ///     Value = Reserved port number
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _clientPorts = new();

    private readonly ILogger<ConnectedClientService> _logger;
    private readonly PortReservationService _portService;
    private readonly Settings _settings;

    /// <summary>
    ///     Initializes a new instance of <see cref="ConnectedClientService" />.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="portService">Port reservation service.</param>
    /// <param name="settings"></param>
    public ConnectedClientService(
        ILogger<ConnectedClientService> logger,
        PortReservationService portService,
        IOptions<Settings> settings)
    {
        _logger = logger;
        _portService = portService;
        _settings = settings.Value;
    }

    /// <summary>
    ///     Returns the number of connected clients.
    /// </summary>
    public int Count => _clientPorts.Count;

    /// <summary>
    ///     Returns a snapshot of connected clients and their reserved ports.
    /// </summary>
    public IReadOnlyDictionary<string, int> Clients =>
        new Dictionary<string, int>(_clientPorts);

    /// <summary>
    ///     Invoked whenever the client list changes (connect/disconnect).
    /// </summary>
    public event Action? OnChanged;

    /// <summary>
    ///     Connection IDs of clients that hold no port reservation, and therefore receive
    ///     any request that did not arrive on a reserved port.
    /// </summary>
    public IReadOnlyList<string> UnroutedClients =>
        _clientPorts.Where(kvp => kvp.Value <= 0).Select(kvp => kvp.Key).ToList();

    /// <summary>
    ///     Adds a connected client and reserves a port for them.
    /// </summary>
    /// <param name="connectionId">The unique connection identifier.</param>
    /// <remarks>
    ///     Never throws when a port cannot be reserved. The client is still registered, with a
    ///     port of -1, and will receive unrouted traffic instead. See <see cref="UnroutedClients" />.
    /// </remarks>
    public void Add(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        var port = ReservePortFor(connectionId);

        _clientPorts[connectionId] = port;

        _logger.LogInformation(
            "Client connected: {ConnectionId}. Assigned port: {Port}. Total connected: {Count}",
            connectionId, port, Count);

        OnChanged?.Invoke();
    }

    /// <summary>
    ///     Resolves the port a newly connected client should own, or -1 if it should be unrouted.
    /// </summary>
    private int ReservePortFor(string connectionId)
    {
        // Broadcast is an explicit opt out of per-port routing: every client stays unrouted
        // and therefore sees every request.
        if (_settings.Broadcast)
            return -1;

        if (_portService.TryReservePort(out var port))
            return port;

        _logger.LogWarning(
            "No port was available to reserve for client {ConnectionId}; it will receive " +
            "unrouted traffic. Concurrent clients are NOT isolated from one another in this " +
            "mode. Start the server with PORTS=<range> to give each client its own port.",
            connectionId);

        return -1;
    }

    /// <summary>
    ///     Removes a connected client and releases their assigned port.
    /// </summary>
    /// <param name="connectionId">The connection ID to remove.</param>
    public void Remove(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return;

        if (_clientPorts.TryRemove(connectionId, out var port))
        {
            // Unrouted clients hold no reservation, so there is nothing to give back.
            if (port > 0)
                _portService.ReleasePort(port);

            _logger.LogInformation(
                "Client disconnected: {ConnectionId}. Released port: {Port}. Total connected: {Count}",
                connectionId, port, Count);

            OnChanged?.Invoke();
        }
        else
        {
            _logger.LogWarning(
                "Attempted to remove unknown client: {ConnectionId}. Nothing to release.",
                connectionId);
        }
    }

    /// <summary>
    ///     Gets the port assigned to a specific client.
    /// </summary>
    /// <param name="connectionId">The client's connection ID.</param>
    /// <returns>
    ///     The port number if assigned; otherwise <c>null</c>.
    /// </returns>
    public int? GetPortForClient(string connectionId)
    {
        if (_clientPorts.TryGetValue(connectionId, out var port))
            return port;

        return null;
    }

    /// <summary>
    ///     Returns connectionId for the client assigned to a given reserved port.
    /// </summary>
    public string? GetClientByPort(int port)
    {
        if (port <= 0)
            return null;

        return _clientPorts.FirstOrDefault(kvp => kvp.Value == port).Key;
    }
}