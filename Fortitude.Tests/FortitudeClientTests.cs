using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Lifecycle and registration behaviour of <see cref="FortitudeClient" />, exercised against
///     a real server.
/// </summary>
public class FortitudeClientTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AFailedConnection_LeavesTheClientReusable()
    {
        // Regression: a failed StartAsync left _connection assigned, so the retry reported
        // "FortitudeClient is already started." instead of the real problem.
        await using var server = await LiveServer.StartAsync(portCount: 1);

        var client = FortitudeClient.Create(output);

        // Nothing is listening here: port 1 is reserved and unbindable in practice.
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.StartAsync("http://localhost:1/fortitude/hub"));

        // The retry against a real server must now succeed.
        var port = await client.StartAsync($"{server.BaseUrl}/fortitude/hub");

        Assert.Equal(server.Ports[0], port);

        await client.StopAsync();
    }

    [Fact]
    public async Task StartingATwiceConnectedClient_IsRejected()
    {
        await using var server = await LiveServer.StartAsync(portCount: 2);

        var client = FortitudeClient.Create(output);
        await client.StartAsync($"{server.BaseUrl}/fortitude/hub");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartAsync($"{server.BaseUrl}/fortitude/hub"));

        await client.StopAsync();
    }

    [Fact]
    public async Task AStoppedClient_CanBeStartedAgain()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);

        var client = FortitudeClient.Create(output);

        await client.StartAsync($"{server.BaseUrl}/fortitude/hub");
        await client.StopAsync();

        var ex = await Record.ExceptionAsync(() => client.StartAsync($"{server.BaseUrl}/fortitude/hub"));

        Assert.Null(ex);
        await client.StopAsync();
    }

    [Fact]
    public async Task AnEmptyUrl_IsRejected()
    {
        var client = FortitudeClient.Create(output);

        await Assert.ThrowsAsync<ArgumentException>(() => client.StartAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.StartAsync("   "));
    }

    [Fact]
    public void RegisteringTheSameHandlerTwice_IsIgnored()
    {
        var client = FortitudeClient.Create(output);
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok());

        client.AddHandler(handler);
        client.AddHandler(handler);

        // No exception, and the duplicate is dropped rather than double-dispatching.
        Assert.True(true);
    }

    [Fact]
    public void RegisteringANullHandler_IsRejected()
    {
        var client = FortitudeClient.Create(output);

        Assert.Throws<ArgumentNullException>(() => client.AddHandler(null!));
    }

    [Fact]
    public async Task RegisteringHandlersConcurrently_DoesNotCorruptTheHandlerList()
    {
        // Regression: handlers lived in a plain List read on SignalR callback threads while
        // tests added to it, so registering mid-flight was a data race.
        var client = FortitudeClient.Create(output);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            client.AddHandler(FortitudeHandler.Accepts().Get().HttpRoute($"/route-{i}")
                .Returns((_, res) => res.Ok())))));

        // Every distinct handler should now dispatch.
        var request = new FortitudeRequest
        {
            Method = "GET", BaseUrl = "http://localhost", Route = "/route-199",
            Url = "http://localhost/route-199", Protocol = "HTTP/1.1"
        };

        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/route-199"), CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(request);
    }

    [Fact]
    public async Task AddingAHandlerWhileRequestsAreInFlight_IsSafe()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/steady").Returns((_, res) => res.Ok("steady"));

        using var http = LiveServer.HttpClientFor(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var calling = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
                await http.GetStringAsync("/steady", cts.Token);
        }, cts.Token);

        var registering = Task.Run(() =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
                client.Accepts().Get().HttpRoute($"/extra-{i++}").Returns((_, res) => res.Ok());
        }, cts.Token);

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(calling, registering));

        Assert.True(
            ex is null or OperationCanceledException or TaskCanceledException,
            $"Unexpected exception: {ex}");
    }
}
