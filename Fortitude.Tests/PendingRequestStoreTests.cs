using System.Diagnostics;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests;

/// <summary>
///     The rendezvous between an incoming HTTP request and the client's answer to it.
/// </summary>
public class PendingRequestStoreTests
{
    private static PendingRequestStore Create() => new(NullLogger<PendingRequestStore>.Instance);

    [Fact]
    public async Task AResponseCompletesItsWaiter()
    {
        var store = Create();
        var id = Guid.NewGuid();

        var waiting = store.WaitForResponse(id, TimeSpan.FromSeconds(5));
        store.Complete(new FortitudeResponse(id).Ok("done"));

        var response = await waiting;

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task NoResponseMeansATimeout()
    {
        var store = Create();

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(
            () => store.WaitForResponse(Guid.NewGuid(), TimeSpan.FromMilliseconds(200)));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"The wait should end at the timeout, but took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task TheTimeoutMessageNamesTheRequest()
    {
        var store = Create();
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => store.WaitForResponse(id, TimeSpan.FromMilliseconds(100)));

        Assert.Contains(id.ToString(), ex.Message);
    }

    [Fact]
    public async Task AnEmptyRequestIdIsRejected()
    {
        var store = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => store.WaitForResponse(Guid.Empty));
    }

    [Fact]
    public void CompletingANullResponseIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Create().Complete(null!));
    }

    [Fact]
    public void AResponseForAnUnknownRequestIsIgnored()
    {
        // A late answer, after the request already timed out, must not throw.
        var ex = Record.Exception(() => Create().Complete(new FortitudeResponse(Guid.NewGuid()).Ok()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task AResponseArrivingAfterATimeoutIsHarmless()
    {
        var store = Create();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<TimeoutException>(
            () => store.WaitForResponse(id, TimeSpan.FromMilliseconds(100)));

        var ex = Record.Exception(() => store.Complete(new FortitudeResponse(id).Ok()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ManyConcurrentRequestsEachGetTheirOwnResponse()
    {
        var store = Create();
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();

        var waits = ids.Select(id => store.WaitForResponse(id, TimeSpan.FromSeconds(10))).ToList();

        // Answer out of order, to be sure responses are matched by id rather than arrival.
        foreach (var id in ids.AsEnumerable().Reverse())
            store.Complete(new FortitudeResponse(id).Ok(id.ToString()));

        var responses = await Task.WhenAll(waits);

        Assert.Equal(ids, responses.Select(r => r.RequestId));
    }

    [Fact]
    public async Task ADuplicateWaitForTheSameIdDoesNotDeadlock()
    {
        var store = Create();
        var id = Guid.NewGuid();

        var first = store.WaitForResponse(id, TimeSpan.FromMilliseconds(300));
        var second = store.WaitForResponse(id, TimeSpan.FromSeconds(5));

        store.Complete(new FortitudeResponse(id).Ok("answered"));

        // The second registration replaces the first, which then times out. Neither hangs.
        var secondResult = await second;
        Assert.Equal(200, secondResult.Status);

        await Assert.ThrowsAsync<TimeoutException>(() => first);
    }
}
