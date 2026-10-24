using System.Net;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Call limits, response sequences and explicit precedence — the features behind
///     "fail twice then succeed" style tests.
/// </summary>
public class CallLimitAndPriorityTests(ITestOutputHelper output)
{
    private static FortitudeRequest Request(string route = "/thing") => new()
    {
        Method = "GET",
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- call limits

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveCallCount_IsRejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FortitudeHandler.Accepts().Times(count));
    }

    [Fact]
    public async Task AHandlerLimitedToOnce_StopsMatchingAfterOneCall()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Once()
            .Returns((_, res) => res.Ok());

        Assert.True(handler.Matches(Request()));

        await handler.HandleRequestAsync(Request());

        Assert.False(handler.Matches(Request()));
        Assert.True(handler.IsExhausted);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task AHandlerLimitedToTwice_AnswersExactlyTwice()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Twice()
            .Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request());
        Assert.True(handler.Matches(Request()));

        await handler.HandleRequestAsync(Request());
        Assert.False(handler.Matches(Request()));
    }

    [Fact]
    public async Task AnUnlimitedHandler_NeverExhausts()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        for (var i = 0; i < 50; i++)
            await handler.HandleRequestAsync(Request());

        Assert.True(handler.Matches(Request()));
        Assert.False(handler.IsExhausted);
        Assert.Equal(50, handler.CallCount);
    }

    [Fact]
    public async Task AnExhaustedHandler_YieldsToAnEarlierOne()
    {
        // The retry scenario: the first two calls fail, the third succeeds.
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/flaky").Returns((_, res) => res.Ok("recovered"));
        client.Accepts().Get().HttpRoute("/flaky").Twice().Returns((_, res) => res.InternalServerError("down"));

        var results = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.TryHandle(
                new HttpRequestMessage(HttpMethod.Get, "http://x/flaky"), CancellationToken.None);
            results.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK],
            results);
    }

    [Fact]
    public async Task AnExhaustedHandlerWithNoFallback_Returns501()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/once").Once().Returns((_, res) => res.Ok());

        var first = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/once"), CancellationToken.None);
        var second = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/once"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, second.StatusCode);
    }

    // ---------------------------------------------------------------- sequences

    [Fact]
    public void AnEmptySequence_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FortitudeHandler.Accepts().Get().ReturnsInSequence());
    }

    [Fact]
    public async Task ASequence_ReturnsEachResponseInTurn()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/seq").ReturnsInSequence(
            (_, res) => res.SetText(500, "first"),
            (_, res) => res.SetText(503, "second"),
            (_, res) => res.Ok("third"));

        var statuses = new List<int>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await handler.HandleRequestAsync(Request("/seq"))).Status);

        Assert.Equal([500, 503, 200], statuses);
    }

    [Fact]
    public async Task ASequence_RepeatsItsFinalResponseOnceExhausted()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/seq").ReturnsInSequence(
            (_, res) => res.SetText(500, "first"),
            (_, res) => res.Ok("steady"));

        await handler.HandleRequestAsync(Request("/seq"));

        for (var i = 0; i < 5; i++)
        {
            var response = await handler.HandleRequestAsync(Request("/seq"));
            Assert.Equal(200, response.Status);
        }
    }

    [Fact]
    public async Task ASequenceOfOne_BehavesLikeAPlainResponder()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/seq")
            .ReturnsInSequence((_, res) => res.Ok("always"));

        for (var i = 0; i < 3; i++)
            Assert.Equal(200, (await handler.HandleRequestAsync(Request("/seq"))).Status);
    }

    // ---------------------------------------------------------------- priority

    [Fact]
    public async Task WithoutPriority_TheMostRecentlyRegisteredHandlerWins()
    {
        // The pre-existing default, pinned so it cannot drift.
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok("first"));
        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok("second"));

        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);

        Assert.Equal("second", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AHigherPriorityHandlerWins_EvenIfRegisteredFirst()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x").Priority(10).Returns((_, res) => res.Ok("important"));
        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok("ordinary"));

        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);

        Assert.Equal("important", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AmongEqualPriorities_TheLatestStillWins()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x").Priority(5).Returns((_, res) => res.Ok("earlier"));
        client.Accepts().Get().HttpRoute("/x").Priority(5).Returns((_, res) => res.Ok("later"));

        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);

        Assert.Equal("later", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ANegativePriority_LosesToTheDefault()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok("default"));
        client.Accepts().Get().HttpRoute("/x").Priority(-1).Returns((_, res) => res.Ok("fallback"));

        var response = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);

        Assert.Equal("default", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnExhaustedHighPriorityHandler_YieldsToALowerOne()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok("steady"));
        client.Accepts().Get().HttpRoute("/x").Priority(10).Once().Returns((_, res) => res.Ok("one-off"));

        var first = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);
        var second = await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/x"), CancellationToken.None);

        Assert.Equal("one-off", await first.Content.ReadAsStringAsync());
        Assert.Equal("steady", await second.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task TheRetryScenario_WorksOverRealHttp()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/flaky").Returns((_, res) => res.Ok("recovered"));
        var failing = client.Accepts().Get().HttpRoute("/flaky").Twice()
            .Returns((_, res) => res.InternalServerError("down"));

        using var http = LiveServer.HttpClientFor(port);

        Assert.Equal(HttpStatusCode.InternalServerError, (await http.GetAsync("/flaky")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await http.GetAsync("/flaky")).StatusCode);
        Assert.Equal("recovered", await http.GetStringAsync("/flaky"));

        Assert.Equal(2, failing.CallCount);
        Assert.True(failing.IsExhausted);
    }

    [Fact]
    public async Task ASequence_WorksOverRealHttp()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/poll").ReturnsInSequence(
            (_, res) => res.Ok("pending"),
            (_, res) => res.Ok("pending"),
            (_, res) => res.Ok("complete"));

        using var http = LiveServer.HttpClientFor(port);

        Assert.Equal("pending", await http.GetStringAsync("/poll"));
        Assert.Equal("pending", await http.GetStringAsync("/poll"));
        Assert.Equal("complete", await http.GetStringAsync("/poll"));
        Assert.Equal("complete", await http.GetStringAsync("/poll"));
    }
}
