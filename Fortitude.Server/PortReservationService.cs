using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

namespace Fortitude.Server;

/// <summary>
///     Manages the reservation and release of ports that the ASP.NET Core server is
///     currently bound to. This service discovers the active ports at startup
///     and provides safe methods to temporarily reserve and release them.
/// </summary>
/// <remarks>
///     Intended to be registered as a singleton via dependency injection.
/// </remarks>
public class PortReservationService
{
    private readonly ILogger<PortReservationService> _logger;

    /// <summary>
    ///     Set of all ports that the server is currently bound to.
    /// </summary>
    private readonly HashSet<int> _activePorts = new();

    /// <summary>
    ///     Ports currently reserved via <see cref="ReservePort"/>.
    /// </summary>
    private readonly ConcurrentDictionary<int, byte> _reservedPorts = new();

    private readonly object _initializationLock = new();
    private bool _initialized;

    /// <summary>
    ///     Creates a new instance of <see cref="PortReservationService"/>.
    /// </summary>
    /// <param name="server">ASP.NET Core server instance used to inspect bound endpoints.</param>
    /// <param name="logger">Logger instance.</param>
    public PortReservationService(IServer server, ILogger<PortReservationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Gets a snapshot of all ports the server is currently bound to.
    /// </summary>
    public IReadOnlyCollection<int> ActivePorts => _activePorts.ToList().AsReadOnly();

    /// <summary>
    ///     Gets a snapshot of all ports currently reserved.
    /// </summary>
    public IReadOnlyCollection<int> ReservedPorts => _reservedPorts.Keys.ToList().AsReadOnly();

    /// <summary>
    ///     Attempts to reserve a free port from the server's bound ports.
    /// </summary>
    /// <returns>
    ///     The reserved port number.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no free ports are available for reservation.
    /// </exception>
    public int ReservePort()
    {
        foreach (var port in _activePorts)
        {
            if (_reservedPorts.ContainsKey(port))
                continue;

            if (_reservedPorts.TryAdd(port, 0))
            {
                _logger.LogInformation("Port reserved: {Port}", port);
                return port;
            }
        }

        _logger.LogError("No available ports to reserve.");
        throw new InvalidOperationException("No available ports to reserve.");
    }

    /// <summary>
    ///     Releases a previously reserved port.
    /// </summary>
    /// <param name="port">The reserved port to release.</param>
    /// <returns>
    ///     True if the port was successfully released; otherwise false.
    /// </returns>
    public bool ReleasePort(int port)
    {
        if (_reservedPorts.TryRemove(port, out _))
        {
            _logger.LogInformation("Port released: {Port}", port);
            return true;
        }

        _logger.LogWarning("Attempted to release a port that was not reserved: {Port}", port);
        return false;
    }

    /// <summary>
    ///     Initializes the service by detecting all ports the web server is listening on.
    /// </summary>
    public void Initialize(IServer server)
    {
        lock (_initializationLock)
        {
            if (_initialized) return;

            var addressesFeature = server.Features.Get<IServerAddressesFeature>();

            if (addressesFeature == null || !addressesFeature.Addresses.Any())
            {
                _logger.LogWarning(
                    "PortReservationService: No server addresses were discovered. " +
                    "ActivePorts will remain empty until Kestrel binds."
                );
                _initialized = true;
                return;
            }

            foreach (var address in addressesFeature.Addresses)
            {
                try
                {
                    var uri = new Uri(address);

                    if (uri.Port > 0)
                    {
                        _activePorts.Add(uri.Port);
                        _logger.LogInformation("Detected active port: {Port}", uri.Port);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse server address: {Address}", address);
                }
            }

            _initialized = true;

            if (!_activePorts.Any())
            {
                _logger.LogWarning("PortReservationService: No valid ports extracted from server addresses.");
            }
        }
    }
}
