using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     The assertion surface: counting what a handler received, and catching requests that
///     matched nothing.
/// </summary>
public class VerificationTests(ITestOutputHelper output)
{
    private static FortitudeRequest Request(string route = "/thing", string method = "GET") => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- counts

    [Fact]
    public async Task ShouldHaveReceived_PassesOnTheExpectedCount()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request());
        await handler.HandleRequestAsync(Request());

        handler.ShouldHaveReceived(2);
    }

    [Fact]
    public async Task ShouldHaveReceived_FailsWithTheHandlerAndTheTrafficItSaw()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request("/thing"));

        var ex = Assert.Throws<FortitudeVerificationException>(() => handler.ShouldHaveReceived(3));

        // The message has to be useful on its own, in a CI log with no debugger.
        Assert.Contains("Expected 3 requests, but saw 1 request", ex.Message);
        Assert.Contains("[GET] /thing", ex.Message);
        Assert.Contains("GET /thing", ex.Message);
    }

    [Fact]
    public void ShouldNeverHaveBeenCalled_PassesWhenUntouched()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        handler.ShouldNeverHaveBeenCalled();
    }

    [Fact]
    public async Task ShouldNeverHaveBeenCalled_FailsWhenItWas()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());
        await handler.HandleRequestAsync(Request());

        var ex = Assert.Throws<FortitudeVerificationException>(() => handler.ShouldNeverHaveBeenCalled());

        Assert.Contains("Expected 0 requests, but saw 1 request", ex.Message);
    }

    [Fact]
    public async Task ShouldHaveReceivedOnce_IsExact()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request());
        handler.ShouldHaveReceivedOnce();

        await handler.HandleRequestAsync(Request());
        Assert.Throws<FortitudeVerificationException>(() => handler.ShouldHaveReceivedOnce());
    }

    [Fact]
    public async Task AtLeastAndAtMost_BoundTheCount()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        for (var i = 0; i < 3; i++)
            await handler.HandleRequestAsync(Request());

        handler.ShouldHaveReceivedAtLeast(2);
        handler.ShouldHaveReceivedAtLeast(3);
        handler.ShouldHaveReceivedAtMost(3);
        handler.ShouldHaveReceivedAtMost(10);

        Assert.Throws<FortitudeVerificationException>(() => handler.ShouldHaveReceivedAtLeast(4));
        Assert.Throws<FortitudeVerificationException>(() => handler.ShouldHaveReceivedAtMost(2));
    }

    [Fact]
    public async Task APredicate_NarrowsWhatIsCounted()
    {
        var handler = FortitudeHandler.Accepts().HttpRoute("/thing").Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request(method: "GET"));
        await handler.HandleRequestAsync(Request(method: "POST"));
        await handler.HandleRequestAsync(Request(method: "POST"));

        handler.ShouldHaveReceived(3);
        handler.ShouldHaveReceived(2, r => r.Method == "POST");
        handler.ShouldHaveReceived(1, r => r.Method == "GET");
        handler.ShouldNeverHaveBeenCalled(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task TheFailureMessage_TruncatesALongHistory()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        for (var i = 0; i < 25; i++)
            await handler.HandleRequestAsync(Request());

        var ex = Assert.Throws<FortitudeVerificationException>(() => handler.ShouldHaveReceived(1));

        Assert.Contains("and 15 more", ex.Message);
    }

    [Fact]
    public void TheAssertions_Chain()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

        handler.ShouldNeverHaveBeenCalled().ShouldHaveReceivedAtMost(0);
    }

    // ---------------------------------------------------------------- unmatched requests

    [Fact]
    public async Task ARequestMatchingNothing_IsRecordedAsUnmatched()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok());

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/unknown"), CancellationToken.None);

        var unmatched = Assert.Single(client.UnmatchedRequests);
        Assert.Equal("/unknown", unmatched.Route);
    }

    [Fact]
    public async Task AMatchedRequest_IsNotRecordedAsUnmatched()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok());

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/known"), CancellationToken.None);

        Assert.Empty(client.UnmatchedRequests);
        client.VerifyNoUnmatchedRequests();
    }

    [Fact]
    public async Task VerifyNoUnmatchedRequests_NamesTheTrafficAndTheHandlers()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/users").Returns((_, res) => res.Ok());
        client.Accepts().Post().HttpRoute("/orders").Returns((_, res) => res.Ok());

        await client.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/accounts?id=7"), CancellationToken.None);

        var ex = Assert.Throws<FortitudeVerificationException>(() => client.VerifyNoUnmatchedRequests());

        // What arrived...
        Assert.Contains("GET /accounts?id=7", ex.Message);
        // ...and what was on offer, so the mismatch is obvious without a debugger.
        Assert.Contains("/users", ex.Message);
        Assert.Contains("/orders", ex.Message);
    }

    [Fact]
    public async Task VerifyNoUnmatchedRequests_SaysWhenNothingWasRegistered()
    {
        var client = FortitudeClient.Create(output);

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/any"), CancellationToken.None);

        var ex = Assert.Throws<FortitudeVerificationException>(() => client.VerifyNoUnmatchedRequests());

        Assert.Contains("No handlers were registered", ex.Message);
    }

    [Fact]
    public async Task AnExhaustedHandler_IsFlaggedInTheMessage()
    {
        // The confusing case: a handler that looks like it should match, but has used up its
        // allowance. Saying so turns a baffling 501 into an obvious one.
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/once").Once().Returns((_, res) => res.Ok());

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/once"), CancellationToken.None);
        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/once"), CancellationToken.None);

        var ex = Assert.Throws<FortitudeVerificationException>(() => client.VerifyNoUnmatchedRequests());

        Assert.Contains("(exhausted)", ex.Message);
    }

    [Fact]
    public async Task ClearUnmatchedRequests_ResetsTheRecord()
    {
        var client = FortitudeClient.Create(output);

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Get, "http://x/any"), CancellationToken.None);
        Assert.NotEmpty(client.UnmatchedRequests);

        client.ClearUnmatchedRequests();

        Assert.Empty(client.UnmatchedRequests);
        client.VerifyNoUnmatchedRequests();
    }

    [Fact]
    public async Task UnmatchedRequests_AreRecordedOverRealHttpToo()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok());

        using var http = LiveServer.HttpClientFor(port);
        await http.GetAsync("/typo");

        var unmatched = Assert.Single(client.UnmatchedRequests);
        Assert.Equal("/typo", unmatched.Route);
        Assert.Throws<FortitudeVerificationException>(() => client.VerifyNoUnmatchedRequests());
    }

    [Fact]
    public async Task TheMessage_TruncatesManyUnmatchedRequests()
    {
        var client = FortitudeClient.Create(output);

        for (var i = 0; i < 15; i++)
            await client.TryHandle(
                new HttpRequestMessage(HttpMethod.Get, $"http://x/missing-{i}"), CancellationToken.None);

        var ex = Assert.Throws<FortitudeVerificationException>(() => client.VerifyNoUnmatchedRequests());

        Assert.Contains("and 5 more", ex.Message);
    }
}
