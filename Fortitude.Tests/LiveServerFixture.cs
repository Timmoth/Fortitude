using Microsoft.AspNetCore.Builder;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Runs a real Fortitude server over real Kestrel sockets.
/// </summary>
/// <remarks>
///     The in-memory <c>TestServer</c> used elsewhere binds no ports, which means it cannot
///     exercise per-port routing at all — the mechanism behind Fortitude's parallel-execution
///     claim. Anything that depends on a request's local port must be tested through here.
/// </remarks>
public sealed class LiveServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<FortitudeClient> _clients = [];

    private LiveServer(WebApplication app, IReadOnlyList<int> ports)
    {
        _app = app;
        Ports = ports;
    }

    /// <summary>Ports Kestrel actually bound, resolved after start.</summary>
    public IReadOnlyList<int> Ports { get; }

    /// <summary>Base URL of the first bound port, for connecting clients.</summary>
    public string BaseUrl => $"http://localhost:{Ports[0]}";

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.StopAsync();

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     Starts a server bound to <paramref name="portCount" /> ephemeral ports.
    /// </summary>
    public static async Task<LiveServer> StartAsync(int portCount = 2, params (string Key, string Value)[] settings)
    {
        // Port 0 lets the OS pick a free port, so parallel test runs never collide.
        var args = settings.Select(s => $"--{s.Key}={s.Value}").ToArray();
        var app = Program.BuildApp(args, Enumerable.Repeat(0, portCount).ToArray());

        await app.StartAsync();

        var reservation = app.Services.GetRequiredService<PortReservationService>();
        var ports = reservation.ActivePorts.ToList();

        if (ports.Count != portCount)
        {
            await app.DisposeAsync();
            throw new InvalidOperationException(
                $"Expected Kestrel to bind {portCount} ephemeral port(s) and for "
                + $"PortReservationService to discover them, but found {ports.Count}: "
                + $"[{string.Join(", ", ports)}].");
        }

        return new LiveServer(app, ports);
    }

    /// <summary>
    ///     Connects a Fortitude client and returns it along with the port it reserved.
    /// </summary>
    public async Task<(FortitudeClient Client, int Port)> ConnectClientAsync(ITestOutputHelper output)
    {
        var client = FortitudeClient.Create(output);
        var port = await client.StartAsync($"{BaseUrl}/fortitude/hub");

        _clients.Add(client);
        return (client, port);
    }

    /// <summary>An <see cref="HttpClient" /> addressing one of the server's bound ports.</summary>
    public static HttpClient HttpClientFor(int port) =>
        new() { BaseAddress = new Uri($"http://localhost:{port}") };
}
