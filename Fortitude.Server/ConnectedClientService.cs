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
    ///     Adds a connected client and reserves a port for them.
    /// </summary>
    /// <param name="connectionId">The unique connection identifier.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if no ports are available for allocation.
    /// </exception>
    public void Add(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        var port = _settings.Broadcast ? -1 : _portService.ReservePort();

        _clientPorts[connectionId] = port;

        _logger.LogInformation(
            "Client connected: {ConnectionId}. Assigned port: {Port}. Total connected: {Count}",
            connectionId, port, Count);

        OnChanged?.Invoke();
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
        return _clientPorts.FirstOrDefault(kvp => kvp.Value == port).Key;
    }
}