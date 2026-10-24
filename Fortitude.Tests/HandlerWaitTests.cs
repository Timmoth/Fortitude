using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     Covers request tracking and <see cref="FortitudeHandler.WaitForRequestAsync" />, the API
///     tests use to block until the system under test has called a dependency.
/// </summary>
public class HandlerWaitTests
{
    private static FortitudeRequest Request(string route = "/thing", string method = "GET", string? body = null) =>
        new()
        {
            Method = method,
            BaseUrl = "http://localhost",
            Route = route,
            Url = "http://localhost" + route,
            Protocol = "HTTP/1.1",
            Body = body == null ? null : System.Text.Encoding.UTF8.GetBytes(body)
        };

    private static FortitudeHandler Handler() =>
        FortitudeHandler.Accepts().Get().HttpRoute("/thing").Returns((_, res) => res.Ok());

    [Fact]
    public async Task WaitingForAnAlreadyReceivedRequest_ReturnsImmediately()
    {
        var handler = Handler();
        await handler.HandleRequestAsync(Request());

        var received = await handler.WaitForRequestAsync(timeoutMs: 1000);

        Assert.NotNull(received);
    }

    [Fact]
    public async Task WaitingForARequestThatArrivesLater_Completes()
    {
        var handler = Handler();

        var waiting = handler.WaitForRequestAsync(timeoutMs: 5000);
        await handler.HandleRequestAsync(Request());

        Assert.NotNull(await waiting);
    }

    [Fact]
    public async Task WaitingWithNoRequest_TimesOutAndReturnsNull()
    {
        var handler = Handler();

        Assert.Null(await handler.WaitForRequestAsync(timeoutMs: 200));
    }

    [Fact]
    public async Task APredicatedWait_IgnoresRequestsThatDoNotSatisfyIt()
    {
        // Regression: NotifyWaiters re-checked the handler's own Matches() rather than the
        // waiter's predicate, so any request at all would satisfy a predicated wait.
        var handler = Handler();

        var waiting = handler.WaitForRequestAsync(
            timeoutMs: 600,
            predicate: r => r.Body != null && System.Text.Encoding.UTF8.GetString(r.Body).Contains("wanted"));

        await handler.HandleRequestAsync(Request(body: "not the one"));

        Assert.Null(await waiting);
    }

    [Fact]
    public async Task APredicatedWait_CompletesOnTheRequestThatSatisfiesIt()
    {
        var handler = Handler();

        var waiting = handler.WaitForRequestAsync(
            timeoutMs: 5000,
            predicate: r => r.Body != null && System.Text.Encoding.UTF8.GetString(r.Body).Contains("wanted"));

        await handler.HandleRequestAsync(Request(body: "not the one"));
        await handler.HandleRequestAsync(Request(body: "this is wanted"));

        var received = await waiting;

        Assert.NotNull(received);
        Assert.Contains("wanted", System.Text.Encoding.UTF8.GetString(received!.Body!));
    }

    [Fact]
    public async Task SeveralWaiters_AreAllNotified()
    {
        var handler = Handler();

        var first = handler.WaitForRequestAsync(timeoutMs: 5000);
        var second = handler.WaitForRequestAsync(timeoutMs: 5000);

        await handler.HandleRequestAsync(Request());

        Assert.NotNull(await first);
        Assert.NotNull(await second);
    }

    [Fact]
    public async Task AWaiterThatTimesOut_DoesNotLeakIntoLaterNotifications()
    {
        var handler = Handler();

        Assert.Null(await handler.WaitForRequestAsync(timeoutMs: 150));

        // The timed-out waiter must have been removed; completing it again would throw
        // inside NotifyWaiters if it were still registered.
        var ex = await Record.ExceptionAsync(() => handler.HandleRequestAsync(Request()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ReceivedRequests_RecordsEveryRequestInOrder()
    {
        var handler = Handler();

        await handler.HandleRequestAsync(Request(body: "one"));
        await handler.HandleRequestAsync(Request(body: "two"));
        await handler.HandleRequestAsync(Request(body: "three"));

        var bodies = handler.ReceivedRequests
            .Select(r => System.Text.Encoding.UTF8.GetString(r.Body!))
            .ToList();

        Assert.Equal(["one", "two", "three"], bodies);
    }

    [Fact]
    public async Task ConcurrentRequests_AreAllRecorded()
    {
        var handler = Handler();

        await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(i => handler.HandleRequestAsync(Request(body: i.ToString()))));

        Assert.Equal(100, handler.ReceivedRequests.Count);
    }

    [Fact]
    public async Task HasReceived_RespectsItsPredicate()
    {
        var handler = Handler();
        await handler.HandleRequestAsync(Request(body: "alpha"));

        Assert.True(handler.HasReceived());
        Assert.True(handler.HasReceived(r => System.Text.Encoding.UTF8.GetString(r.Body!) == "alpha"));
        Assert.False(handler.HasReceived(r => System.Text.Encoding.UTF8.GetString(r.Body!) == "beta"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveTimeout_IsRejected(int timeoutMs)
    {
        var handler = Handler();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.WaitForRequestAsync(timeoutMs));
    }
}
