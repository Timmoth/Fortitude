using System.Diagnostics;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     A simulated delay has to be abandonable by whoever is waiting on it.
/// </summary>
/// <remarks>
///     Delays exist so a test can prove its system under test handles a slow dependency —
///     that an <see cref="HttpClient.Timeout" /> fires, that a retry policy kicks in. If the
///     delay ignores the caller's token none of those assertions mean anything: the call
///     always runs to completion and the test measures the delay rather than the timeout.
/// </remarks>
public class CancellationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fortitude-cancellation", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static FortitudeRequest Request(string method = "GET", string route = "/slow") => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- handler

    [Fact]
    public async Task ADelayedHandlerStopsWaitingWhenTheCallerCancels()
    {
        var handler = FortitudeHandler.Accepts()
            .Get()
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleRequestAsync(Request(), cts.Token));

        stopwatch.Stop();
        output.WriteLine($"Gave up after {stopwatch.ElapsedMilliseconds}ms of a 30s delay.");

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Cancellation took {stopwatch.ElapsedMilliseconds}ms; the delay was not abandoned.");
    }

    [Fact]
    public async Task ACancelledRequestStillCountsAsHavingBeenMade()
    {
        // The system under test did call the dependency; it just gave up on the answer. An
        // assertion about what the SUT sent must still hold.
        var handler = FortitudeHandler.Accepts()
            .Get()
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleRequestAsync(Request(), cts.Token));

        Assert.Single(handler.ReceivedRequests);
        handler.ShouldHaveReceivedOnce();
    }

    [Fact]
    public async Task AWaiterIsStillNotifiedWhenTheCallerCancels()
    {
        var handler = FortitudeHandler.Accepts()
            .Get()
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        var waiting = handler.WaitForRequestAsync(5000);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleRequestAsync(Request(), cts.Token));

        Assert.NotNull(await waiting);
    }

    [Fact]
    public async Task AnUndelayedHandlerIgnoresAnAlreadyCancelledToken()
    {
        // Nothing is being waited on, so there is nothing to abandon. Throwing here would
        // make every handler fail the moment the caller's token was a cancelled one.
        var handler = FortitudeHandler.Accepts().Get().Returns((_, res) => res.Ok());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var response = await handler.HandleRequestAsync(Request(), cts.Token);

        Assert.Equal(200, response.Status);
    }

    // ---------------------------------------------------------------- intercept mode

    [Fact]
    public async Task InterceptMode_HonoursTheHttpClientTimeout()
    {
        var client = FortitudeClient.Create(output.WriteLine);

        client.Accepts()
            .Get()
            .HttpRoute("/slow")
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("sut", c =>
        {
            c.BaseAddress = new Uri("http://example.com");
            c.Timeout = TimeSpan.FromMilliseconds(300);
        });

        await using var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("sut");

        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync("/slow"));

        stopwatch.Stop();
        output.WriteLine($"HttpClient gave up after {stopwatch.ElapsedMilliseconds}ms (timeout was 300ms).");

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The call took {stopwatch.ElapsedMilliseconds}ms, so the 300ms timeout was not honoured.");
    }

    [Fact]
    public async Task InterceptMode_StillAnswersWhenTheTimeoutIsGenerous()
    {
        var client = FortitudeClient.Create(output.WriteLine);

        client.Accepts()
            .Get()
            .HttpRoute("/quick")
            .WithDelay(TimeSpan.FromMilliseconds(50))
            .Returns((_, res) => res.Ok(new { ok = true }));

        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("sut", c =>
        {
            c.BaseAddress = new Uri("http://example.com");
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        await using var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("sut");

        var response = await http.GetAsync("/quick");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InterceptMode_HonoursACallerSuppliedToken()
    {
        var client = FortitudeClient.Create(output.WriteLine);

        client.Accepts()
            .Get()
            .HttpRoute("/slow")
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("sut", c => c.BaseAddress = new Uri("http://example.com"));

        await using var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("sut");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => http.GetAsync("/slow", cts.Token));

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The call took {stopwatch.ElapsedMilliseconds}ms despite the token being cancelled at 200ms.");
    }

    // ---------------------------------------------------------------- yaml handlers

    [Fact]
    public async Task AYamlDelayIsAbandonedWhenTheRequestIsAborted()
    {
        Directory.CreateDirectory(_dir);

        File.WriteAllText(Path.Combine(_dir, "slow.yaml"), """
                                                           handlers:
                                                             - match:
                                                                 methods: [GET]
                                                                 route: /slow
                                                               response:
                                                                 status: 200
                                                                 delayMs: 30000
                                                                 body:
                                                                   text: eventually
                                                           """);

        using var store = new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance,
            Options.Create(new Settings { ConfigDirectory = _dir, HotReload = false }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.HandleIncomingAsync(Request(), cts.Token));

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The server waited {stopwatch.ElapsedMilliseconds}ms for a caller that had hung up.");
    }
}
