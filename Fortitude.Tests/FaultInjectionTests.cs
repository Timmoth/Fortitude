using System.Net;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Transport-level failures: what a client sees when a dependency misbehaves rather than
///     simply returning an error status.
/// </summary>
public class FaultInjectionTests(ITestOutputHelper output)
{
    // ---------------------------------------------------------------- response shaping

    [Fact]
    public void AnEmptyBodyFault_ClearsTheBody()
    {
        var response = new FortitudeResponse(Guid.NewGuid()).Ok("content").WithFault(HttpFault.EmptyBody);

        Assert.Equal(HttpFault.EmptyBody, response.Fault);
        Assert.True(response.Body is null or { Length: 0 });
    }

    [Fact]
    public void AMalformedJsonFault_ProducesUnparseableJson()
    {
        var response = new FortitudeResponse(Guid.NewGuid()).WithFault(HttpFault.MalformedJson);

        Assert.Equal("application/json", response.ContentType);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(response.Body!));
    }

    [Fact]
    public void ARandomBytesFault_ProducesUnparseableJson()
    {
        var response = new FortitudeResponse(Guid.NewGuid()).WithFault(HttpFault.RandomBytes);

        Assert.Equal("application/json", response.ContentType);
        Assert.NotEmpty(response.Body!);
    }

    [Fact]
    public void AConnectionResetFault_ThrowsWhenConvertedForTheIntercept()
    {
        var response = new FortitudeResponse(Guid.NewGuid()).WithFault(HttpFault.ConnectionReset);

        Assert.Throws<HttpRequestException>(() => response.ToHttpResponseMessage());
    }

    [Fact]
    public void NoFault_LeavesTheResponseAlone()
    {
        var response = new FortitudeResponse(Guid.NewGuid()).Ok("fine");

        Assert.Equal(HttpFault.None, response.Fault);
        Assert.NotNull(response.ToHttpResponseMessage());
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task AConnectionReset_SurfacesAsASocketErrorOverRealHttp()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/dead").ReturnsFault(HttpFault.ConnectionReset);

        using var http = LiveServer.HttpClientFor(port);

        // No status code reaches the caller at all — the connection simply goes away.
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync("/dead"));
    }

    [Fact]
    public async Task AnEmptyBodyFault_ReturnsASuccessWithNoContent()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/hollow").ReturnsFault(HttpFault.EmptyBody);

        using var http = LiveServer.HttpClientFor(port);
        var response = await http.GetAsync("/hollow");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMalformedJsonFault_ReachesTheClientAsBrokenJson()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/garbled").ReturnsFault(HttpFault.MalformedJson);

        using var http = LiveServer.HttpClientFor(port);
        var response = await http.GetAsync("/garbled");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(body));
    }

    [Fact]
    public async Task AConnectionReset_SurfacesAsAnExceptionInInterceptMode()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/dead").ReturnsFault(HttpFault.ConnectionReset);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/dead"), CancellationToken.None));
    }

    // ---------------------------------------------------------------- random failure

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void AProbabilityOutsideZeroToOne_IsRejected(double probability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FortitudeHandler.Accepts().FailsRandomly(probability));
    }

    [Fact]
    public async Task AProbabilityOfOne_FailsEveryCall()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x")
            .FailsRandomly(1.0)
            .Returns((_, res) => res.Ok("never"));

        for (var i = 0; i < 10; i++)
            Assert.Equal(500, (await handler.HandleRequestAsync(Request())).Status);
    }

    [Fact]
    public async Task AProbabilityOfZero_NeverFails()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x")
            .FailsRandomly(0)
            .Returns((_, res) => res.Ok("always"));

        for (var i = 0; i < 10; i++)
            Assert.Equal(200, (await handler.HandleRequestAsync(Request())).Status);
    }

    [Fact]
    public async Task ACustomFailureResponse_IsUsed()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x")
            .FailsRandomly(1.0, (_, res) => res.TooManyRequests("slow down", retryAfterSeconds: 30))
            .Returns((_, res) => res.Ok());

        var response = await handler.HandleRequestAsync(Request());

        Assert.Equal(429, response.Status);
        Assert.Equal("30", response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task APartialFailureRate_ProducesBothOutcomes()
    {
        // Statistical, so give it enough room that a flake is vanishingly unlikely: at p=0.5
        // over 200 calls, seeing zero of either outcome has probability ~2^-199.
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x")
            .FailsRandomly(0.5)
            .Returns((_, res) => res.Ok());

        var statuses = new List<int>();
        for (var i = 0; i < 200; i++)
            statuses.Add((await handler.HandleRequestAsync(Request())).Status);

        Assert.Contains(200, statuses);
        Assert.Contains(500, statuses);
    }

    [Fact]
    public async Task RandomFailure_CombinesWithTheOtherModifiers()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/x")
            .Once()
            .FailsRandomly(1.0)
            .Returns((_, res) => res.Ok());

        Assert.Equal(500, (await handler.HandleRequestAsync(Request())).Status);
        Assert.True(handler.IsExhausted);
    }

    private static FortitudeRequest Request() => new()
    {
        Method = "GET",
        BaseUrl = "http://localhost",
        Route = "/x",
        Url = "http://localhost/x",
        Protocol = "HTTP/1.1"
    };
}
