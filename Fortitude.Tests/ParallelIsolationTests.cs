using Fortitude.Client;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     End-to-end coverage of the claim the README leads with: several clients can share one
///     server without seeing each other's traffic, because each reserves its own port.
/// </summary>
/// <remarks>
///     These run against real Kestrel sockets. The in-memory TestServer binds no ports, so it
///     cannot distinguish routed from unrouted delivery and would pass regardless.
/// </remarks>
public class ParallelIsolationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EachClient_ReservesADifferentPort()
    {
        await using var server = await LiveServer.StartAsync(portCount: 2);

        var (_, portA) = await server.ConnectClientAsync(output);
        var (_, portB) = await server.ConnectClientAsync(output);

        Assert.Contains(portA, server.Ports);
        Assert.Contains(portB, server.Ports);
        Assert.NotEqual(portA, portB);
    }

    [Fact]
    public async Task AClientOnlyReceivesRequestsSentToItsOwnPort()
    {
        await using var server = await LiveServer.StartAsync(portCount: 2);

        var (clientA, portA) = await server.ConnectClientAsync(output);
        var (clientB, portB) = await server.ConnectClientAsync(output);

        // Both clients register a handler for the same route, returning different bodies.
        var handlerA = clientA.Accepts().Get().HttpRoute("/whoami")
            .Returns((_, res) => res.Ok("A"));
        var handlerB = clientB.Accepts().Get().HttpRoute("/whoami")
            .Returns((_, res) => res.Ok("B"));

        using var httpToA = LiveServer.HttpClientFor(portA);
        using var httpToB = LiveServer.HttpClientFor(portB);

        var fromA = await httpToA.GetStringAsync("/whoami");
        var fromB = await httpToB.GetStringAsync("/whoami");

        // Each port is answered by its own client...
        Assert.Equal("A", fromA);
        Assert.Equal("B", fromB);

        // ...and neither client observed the other's request. This is the assertion that
        // actually fails under broadcast routing: both handlers would see both requests.
        Assert.Single(handlerA.ReceivedRequests);
        Assert.Single(handlerB.ReceivedRequests);
    }

    [Fact]
    public async Task ConcurrentTrafficToBothPorts_StaysIsolated()
    {
        await using var server = await LiveServer.StartAsync(portCount: 2);

        var (clientA, portA) = await server.ConnectClientAsync(output);
        var (clientB, portB) = await server.ConnectClientAsync(output);

        var handlerA = clientA.Accepts().Get().HttpRoute("/load").Returns((_, res) => res.Ok("A"));
        var handlerB = clientB.Accepts().Get().HttpRoute("/load").Returns((_, res) => res.Ok("B"));

        using var httpToA = LiveServer.HttpClientFor(portA);
        using var httpToB = LiveServer.HttpClientFor(portB);

        const int perClient = 15;

        var toA = Enumerable.Range(0, perClient).Select(_ => httpToA.GetStringAsync("/load"));
        var toB = Enumerable.Range(0, perClient).Select(_ => httpToB.GetStringAsync("/load"));

        var results = await Task.WhenAll(toA.Concat(toB));

        Assert.Equal(perClient, results.Count(r => r == "A"));
        Assert.Equal(perClient, results.Count(r => r == "B"));

        Assert.Equal(perClient, handlerA.ReceivedRequests.Count);
        Assert.Equal(perClient, handlerB.ReceivedRequests.Count);
    }

    [Fact]
    public async Task APortWithNoConnectedClient_Returns503()
    {
        await using var server = await LiveServer.StartAsync(portCount: 2);

        var (_, claimed) = await server.ConnectClientAsync(output);
        var unclaimed = server.Ports.Single(p => p != claimed);

        using var http = LiveServer.HttpClientFor(unclaimed);
        var response = await http.GetAsync("/anything");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AClientWithNoReservedPort_ReceivesTrafficForUnclaimedPorts()
    {
        // One port, two clients: the second cannot reserve anything and becomes unrouted.
        // It must still connect, and must still be able to serve traffic.
        await using var server = await LiveServer.StartAsync(portCount: 1);

        var (clientA, portA) = await server.ConnectClientAsync(output);
        var (clientB, portB) = await server.ConnectClientAsync(output);

        Assert.Equal(server.Ports[0], portA);
        Assert.Equal(-1, portB);

        var handlerA = clientA.Accepts().Get().HttpRoute("/owned").Returns((_, res) => res.Ok("A"));
        clientB.Accepts().Get().HttpRoute("/owned").Returns((_, res) => res.Ok("B"));

        using var http = LiveServer.HttpClientFor(portA);

        // The port is owned by A, so A answers it and B never sees the request.
        Assert.Equal("A", await http.GetStringAsync("/owned"));
        Assert.Single(handlerA.ReceivedRequests);
    }

    [Fact]
    public async Task DisconnectingAClient_FreesItsPortForTheNextOne()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);

        var (clientA, portA) = await server.ConnectClientAsync(output);
        Assert.Equal(server.Ports[0], portA);

        await clientA.StopAsync();

        // The server releases the port on disconnect; give SignalR a moment to notice.
        var reclaimed = -1;
        for (var attempt = 0; attempt < 20 && reclaimed != server.Ports[0]; attempt++)
        {
            await Task.Delay(100);
            var (client, port) = await server.ConnectClientAsync(output);
            reclaimed = port;
            if (reclaimed != server.Ports[0]) await client.StopAsync();
        }

        Assert.Equal(server.Ports[0], reclaimed);
    }

    [Fact]
    public async Task BroadcastSetting_DeliversEveryRequestToEveryClient()
    {
        await using var server = await LiveServer.StartAsync(
            portCount: 2, ("Settings:Broadcast", "true"));

        var (clientA, portA) = await server.ConnectClientAsync(output);
        var (clientB, portB) = await server.ConnectClientAsync(output);

        // Broadcast forgoes reservation entirely.
        Assert.Equal(-1, portA);
        Assert.Equal(-1, portB);

        var handlerA = clientA.Accepts().Get().HttpRoute("/shared").Returns((_, res) => res.Ok("A"));
        var handlerB = clientB.Accepts().Get().HttpRoute("/shared").Returns((_, res) => res.Ok("B"));

        using var http = LiveServer.HttpClientFor(server.Ports[0]);
        await http.GetAsync("/shared");

        // Both clients saw it — which is exactly why broadcast is not the default.
        Assert.Single(handlerA.ReceivedRequests);
        Assert.Single(handlerB.ReceivedRequests);
    }
}
