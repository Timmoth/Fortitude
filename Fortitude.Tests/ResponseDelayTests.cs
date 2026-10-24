using System.Diagnostics;
using System.Net;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Simulated latency: the feature tests reach for when exercising timeouts, retries and
///     circuit breakers in the system under test.
/// </summary>
public class ResponseDelayTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fortitude-delay", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // ---------------------------------------------------------------- builder

    [Fact]
    public void ANegativeDelay_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FortitudeHandler.Accepts().WithDelay(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AnInvertedRandomRange_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FortitudeHandler.Accepts().WithRandomDelay(500, 100));
    }

    [Fact]
    public void ADelayIsCarriedOntoTheHandler()
    {
        var handler = FortitudeHandler.Accepts().Get().WithDelay(250).Returns((_, res) => res.Ok());

        Assert.Equal(TimeSpan.FromMilliseconds(250), handler.Delay);
        Assert.Null(handler.DelayMax);
    }

    [Fact]
    public void ARandomDelayRangeIsCarriedOntoTheHandler()
    {
        var handler = FortitudeHandler.Accepts().Get().WithRandomDelay(100, 400).Returns((_, res) => res.Ok());

        Assert.Equal(TimeSpan.FromMilliseconds(100), handler.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(400), handler.DelayMax);
    }

    // ---------------------------------------------------------------- behaviour

    [Fact]
    public async Task AHandlerWithADelay_TakesAtLeastThatLong()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/slow")
            .WithDelay(400)
            .Returns((_, res) => res.Ok("done"));

        var stopwatch = Stopwatch.StartNew();
        await handler.HandleRequestAsync(Request("/slow"));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 350,
            $"Expected at least ~400ms of delay, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task AHandlerWithoutADelay_IsImmediate()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/fast").Returns((_, res) => res.Ok());

        var stopwatch = Stopwatch.StartNew();
        await handler.HandleRequestAsync(Request("/fast"));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 150,
            $"Expected no delay, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task ARandomDelay_StaysWithinItsRange()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/jitter")
            .WithRandomDelay(100, 250)
            .Returns((_, res) => res.Ok());

        for (var i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await handler.HandleRequestAsync(Request("/jitter"));
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds >= 80,
                $"Delay {stopwatch.ElapsedMilliseconds}ms fell below the 100ms minimum.");
            Assert.True(stopwatch.ElapsedMilliseconds < 2000,
                $"Delay {stopwatch.ElapsedMilliseconds}ms far exceeded the 250ms maximum.");
        }
    }

    [Fact]
    public async Task AWaiterIsNotifiedBeforeTheDelayElapses()
    {
        // A test asserting "the SUT called the dependency" should not have to sit through
        // the latency being simulated.
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/slow")
            .WithDelay(2000)
            .Returns((_, res) => res.Ok());

        var waiting = handler.WaitForRequestAsync(timeoutMs: 5000);

        var stopwatch = Stopwatch.StartNew();
        var handling = handler.HandleRequestAsync(Request("/slow"));

        var received = await waiting;
        stopwatch.Stop();

        Assert.NotNull(received);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"The waiter should complete immediately, but took {stopwatch.ElapsedMilliseconds}ms.");

        await handling;
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task ADelayedResponse_ArrivesOverRealHttp()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/slow")
            .WithDelay(400)
            .Returns((_, res) => res.Ok("eventually"));

        using var http = LiveServer.HttpClientFor(port);

        var stopwatch = Stopwatch.StartNew();
        var body = await http.GetStringAsync("/slow");
        stopwatch.Stop();

        Assert.Equal("eventually", body);
        Assert.True(stopwatch.ElapsedMilliseconds >= 350,
            $"Expected the delay to apply end to end, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task ADelayLongerThanTheServerTimeout_Returns504()
    {
        // The documented interaction: latency beyond Settings:ResponseTimeoutSeconds is cut
        // short by a gateway timeout. This is how you test a client's timeout handling.
        await using var server = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ResponseTimeoutSeconds", "1"));

        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/too-slow")
            .WithDelay(TimeSpan.FromSeconds(10))
            .Returns((_, res) => res.Ok("never arrives"));

        using var http = LiveServer.HttpClientFor(port);

        var response = await http.GetAsync("/too-slow");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task ADelayWorksInInterceptMode()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/slow")
            .WithDelay(300)
            .Returns((_, res) => res.Ok("intercepted"));

        var stopwatch = Stopwatch.StartNew();
        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/slow"), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal("intercepted", await response.Content.ReadAsStringAsync());
        Assert.True(stopwatch.ElapsedMilliseconds >= 250,
            $"Expected the delay to apply in intercept mode, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    // ---------------------------------------------------------------- yaml

    [Fact]
    public async Task AYamlDelayMs_IsApplied()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "slow.yaml"), """
                                                           handlers:
                                                             - match:
                                                                 methods: [GET]
                                                                 route: /slow
                                                               response:
                                                                 status: 200
                                                                 delayMs: 400
                                                                 body:
                                                                   text: late
                                                           """);

        await using var server = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"));

        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var stopwatch = Stopwatch.StartNew();
        var body = await http.GetStringAsync("/slow");
        stopwatch.Stop();

        Assert.Equal("late", body);
        Assert.True(stopwatch.ElapsedMilliseconds >= 350,
            $"Expected the YAML delay to apply, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public void AYamlDelayRange_IsApplied()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle("""
                                                         match:
                                                           route: /jitter
                                                         response:
                                                           status: 200
                                                           delay:
                                                             minMs: 100
                                                             maxMs: 500
                                                         """);

        Assert.Equal(TimeSpan.FromMilliseconds(100), handler.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(500), handler.DelayMax);
    }

    [Fact]
    public void AYamlResponseWithoutADelay_HasNone()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle("""
                                                         match:
                                                           route: /plain
                                                         response:
                                                           status: 200
                                                         """);

        Assert.Equal(TimeSpan.Zero, handler.Delay);
        Assert.Null(handler.DelayMax);
    }

    private static FortitudeRequest Request(string route) => new()
    {
        Method = "GET",
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };
}
