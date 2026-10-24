using Fortitude.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

/// <summary>
///     Covers port reservation and request routing — the mechanism behind Fortitude's claim that
///     concurrent tests can share one server without interfering.
/// </summary>
public class ConnectedClientServiceTests
{
    private static ConnectedClientService Create(bool broadcast, params int[] boundPorts)
    {
        var ports = new PortReservationService(NullLogger<PortReservationService>.Instance);
        ports.Initialize(new StubServer(boundPorts));

        return new ConnectedClientService(
            NullLogger<ConnectedClientService>.Instance,
            ports,
            Options.Create(new Settings { Broadcast = broadcast }));
    }

    [Fact]
    public void Broadcast_IsOffByDefault()
    {
        // The documented behaviour — and the whole point of port reservation — is per-port
        // routing. A default of true silently disables it.
        Assert.False(new Settings().Broadcast);
    }

    [Fact]
    public void EachClient_ReservesADistinctPort()
    {
        var clients = Create(broadcast: false, 5001, 5002, 5003);

        clients.Add("a");
        clients.Add("b");
        clients.Add("c");

        var assigned = new[] { "a", "b", "c" }.Select(c => clients.GetPortForClient(c)).ToList();

        Assert.All(assigned, p => Assert.Contains(p, new int?[] { 5001, 5002, 5003 }));
        Assert.Equal(3, assigned.Distinct().Count());
    }

    [Fact]
    public void RequestsOnAReservedPort_RouteOnlyToThatPortsOwner()
    {
        var clients = Create(broadcast: false, 5001, 5002);

        clients.Add("a");
        clients.Add("b");

        var portA = clients.GetPortForClient("a")!.Value;
        var portB = clients.GetPortForClient("b")!.Value;

        Assert.Equal("a", clients.GetClientByPort(portA));
        Assert.Equal("b", clients.GetClientByPort(portB));

        // Neither client is eligible for the other's traffic.
        Assert.Empty(clients.UnroutedClients);
    }

    [Fact]
    public void ExhaustingThePortPool_DoesNotThrow_AndLeavesTheClientUnrouted()
    {
        // Regression: Add() used to call ReservePort(), which threw once the pool was empty.
        // The exception escaped Hub.OnConnectedAsync and killed the client's connection.
        var clients = Create(broadcast: false, 5001);

        clients.Add("a");

        var ex = Record.Exception(() => clients.Add("b"));

        Assert.Null(ex);
        Assert.Equal(5001, clients.GetPortForClient("a"));
        Assert.Equal(-1, clients.GetPortForClient("b"));
        Assert.Equal(["b"], clients.UnroutedClients);
    }

    [Fact]
    public void WithNoBoundPorts_EveryClientIsUnrouted()
    {
        // The in-memory TestServer case: there are no real ports to hand out, so clients must
        // still connect and share incoming traffic.
        var clients = Create(broadcast: false);

        clients.Add("a");
        clients.Add("b");

        Assert.Equal(-1, clients.GetPortForClient("a"));
        Assert.Equal(-1, clients.GetPortForClient("b"));
        Assert.Equal(2, clients.UnroutedClients.Count);
    }

    [Fact]
    public void Broadcast_ForcesEveryClientUnrouted_EvenWhenPortsAreAvailable()
    {
        var clients = Create(broadcast: true, 5001, 5002);

        clients.Add("a");
        clients.Add("b");

        Assert.Equal(-1, clients.GetPortForClient("a"));
        Assert.Equal(-1, clients.GetPortForClient("b"));
        Assert.Equal(2, clients.UnroutedClients.Count);
        Assert.Null(clients.GetClientByPort(5001));
    }

    [Fact]
    public void DisconnectingAClient_ReturnsItsPortToThePool()
    {
        var clients = Create(broadcast: false, 5001);

        clients.Add("a");
        Assert.Equal(5001, clients.GetPortForClient("a"));

        clients.Remove("a");
        clients.Add("b");

        Assert.Equal(5001, clients.GetPortForClient("b"));
        Assert.Equal("b", clients.GetClientByPort(5001));
    }

    [Fact]
    public void DisconnectingAnUnroutedClient_DoesNotDisturbReservations()
    {
        var clients = Create(broadcast: false, 5001);

        clients.Add("a"); // takes 5001
        clients.Add("b"); // unrouted

        clients.Remove("b");

        Assert.Equal("a", clients.GetClientByPort(5001));
        Assert.Equal(1, clients.Count);
    }

    [Fact]
    public void GetClientByPort_IgnoresTheUnroutedSentinel()
    {
        // Unrouted clients are stored as -1. Looking that up must not match them, or a request
        // arriving on a nonsense port would be handed to an arbitrary client.
        var clients = Create(broadcast: false);

        clients.Add("a");

        Assert.Null(clients.GetClientByPort(-1));
        Assert.Null(clients.GetClientByPort(0));
    }

    /// <summary>Minimal <see cref="IServer" /> exposing a fixed set of bound addresses.</summary>
    private sealed class StubServer : IServer
    {
        public StubServer(params int[] ports)
        {
            var addresses = new AddressesFeature();
            foreach (var port in ports)
                addresses.Addresses.Add($"http://localhost:{port}");

            Features = new FeatureCollection();
            Features.Set<IServerAddressesFeature>(addresses);
        }

        public IFeatureCollection Features { get; }

        public void Dispose()
        {
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken ct)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        private sealed class AddressesFeature : IServerAddressesFeature
        {
            public ICollection<string> Addresses { get; } = new List<string>();
            public bool PreferHostingUrls { get; set; }
        }
    }
}
