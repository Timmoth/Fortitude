using Fortitude.Client;
using Fortitude.Server;
using Xunit;

namespace Tests;

/// <summary>
///     Covers the bounded request/response buffer behind the live UI.
/// </summary>
public class RequestTrackerTests
{
    private static FortitudeRequest Request(Guid? id = null) => new()
    {
        RequestId = id ?? Guid.NewGuid(),
        Method = "GET",
        BaseUrl = "http://localhost",
        Route = "/thing",
        Url = "http://localhost/thing",
        Protocol = "HTTP/1.1"
    };

    [Fact]
    public void ARequestAndItsResponse_AreRetrievable()
    {
        var tracker = new RequestTracker();
        var request = Request();

        tracker.Add(request);
        tracker.Add(new FortitudeResponse(request.RequestId).Ok("hi"));

        Assert.Single(tracker.Requests);
        Assert.NotNull(tracker.GetResponse(request.RequestId));
    }

    [Fact]
    public void TotalRequests_CountsEverythingSeen_NotJustWhatIsRetained()
    {
        // Regression: TotalRequests returned the retained count, so the UI stopped counting
        // at the buffer capacity and reported a total that was simply wrong.
        var tracker = new RequestTracker();

        for (var i = 0; i < RequestTracker.Capacity + 250; i++)
            tracker.Add(Request());

        Assert.Equal(RequestTracker.Capacity + 250, tracker.TotalRequests);
        Assert.Equal(RequestTracker.Capacity, tracker.RetainedRequests);
    }

    [Fact]
    public void EvictingARequest_AlsoDiscardsItsResponse()
    {
        // Regression: responses lived in a dictionary that was never pruned, so a long-running
        // server accumulated them without bound.
        var tracker = new RequestTracker();

        var first = Request();
        tracker.Add(first);
        tracker.Add(new FortitudeResponse(first.RequestId).Ok("gone soon"));

        Assert.NotNull(tracker.GetResponse(first.RequestId));

        for (var i = 0; i < RequestTracker.Capacity; i++)
            tracker.Add(Request());

        Assert.Null(tracker.GetResponse(first.RequestId));
        Assert.Equal(RequestTracker.Capacity, tracker.RetainedRequests);
    }

    [Fact]
    public void AResponseForAnUnknownRequest_IsNotRetained()
    {
        var tracker = new RequestTracker();

        tracker.Add(new FortitudeResponse(Guid.NewGuid()).Ok("orphan"));

        Assert.Empty(tracker.Requests);
    }

    [Fact]
    public void Clear_EmptiesTheBufferButKeepsTheRunningTotal()
    {
        var tracker = new RequestTracker();

        tracker.Add(Request());
        tracker.Add(Request());
        tracker.Clear();

        Assert.Empty(tracker.Requests);
        Assert.Equal(0, tracker.RetainedRequests);
        Assert.Equal(2, tracker.TotalRequests);
    }

    [Fact]
    public void RequestsAreReturnedOldestFirst()
    {
        var tracker = new RequestTracker();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
            tracker.Add(Request(id));

        Assert.Equal(ids, tracker.Requests.Select(r => r.RequestId));
    }

    [Fact]
    public async Task ConcurrentWriters_DoNotCorruptTheBuffer()
    {
        var tracker = new RequestTracker();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                var request = Request();
                tracker.Add(request);
                tracker.Add(new FortitudeResponse(request.RequestId).Ok());
                _ = tracker.Requests.Count;
            }
        })));

        Assert.Equal(16 * 300, tracker.TotalRequests);
        Assert.Equal(RequestTracker.Capacity, tracker.RetainedRequests);
    }

    [Fact]
    public void AddingNull_IsIgnored()
    {
        var tracker = new RequestTracker();

        tracker.Add((FortitudeRequest)null!);
        tracker.Add((FortitudeResponse)null!);

        Assert.Empty(tracker.Requests);
        Assert.Equal(0, tracker.TotalRequests);
    }
}
