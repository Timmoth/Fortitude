using System.Net;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Proxy passthrough and recording.
/// </summary>
/// <remarks>
///     The "real" upstream in these tests is itself a Fortitude server, which keeps them
///     hermetic while still exercising genuine HTTP between the two.
/// </remarks>
public sealed class ProxyAndRecordTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = CreateTempDirectory();

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortitude-proxy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>Stands up a stand-in for the real service.</summary>
    private async Task<(LiveServer Server, string BaseUrl)> StartUpstreamAsync()
    {
        var upstream = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await upstream.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}")
            .Returns((req, res) => res.Ok(new { id = req.Route.Split('/').Last(), name = "Alice" })
                .WithHeader("X-Upstream", "yes"));

        client.Accepts().Post().HttpRoute("/users")
            .Returns((req, res) => res.Created(new { id = 99, echoed = req.Body.FromMessageBody() }));

        client.Accepts().Get().HttpRoute("/text")
            .Returns((_, res) => res.Ok("plain words"));

        client.Accepts().Get().HttpRoute("/missing")
            .Returns((_, res) => res.NotFound("no such thing"));

        return (upstream, $"http://localhost:{port}");
    }

    // ---------------------------------------------------------------- proxying

    [Fact]
    public async Task AProxiedRequestReturnsTheRealResponse()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl);

        using var http = LiveServer.HttpClientFor(port);
        var body = await http.GetStringAsync("/users/7");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("7", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("Alice", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpstreamStatusAndHeadersAreCopied()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl);
        client.Accepts().Get().HttpRoute("/missing").ProxiesTo(upstreamUrl);

        using var http = LiveServer.HttpClientFor(port);

        var ok = await http.GetAsync("/users/1");
        Assert.Equal("yes", ok.Headers.GetValues("X-Upstream").Single());

        var missing = await http.GetAsync("/missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task RequestBodiesAreForwarded()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Post().HttpRoute("/users").ProxiesTo(upstreamUrl);

        using var http = LiveServer.HttpClientFor(port);
        var response = await http.PostAsync("/users",
            new StringContent("""{"name":"Bob"}""", System.Text.Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Bob", body);
    }

    [Fact]
    public async Task AnUnreachableUpstreamBecomesA502()
    {
        // The mock is working fine; the thing behind it is not. 502 says which.
        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/x").ProxiesTo("http://localhost:1");

        using var http = LiveServer.HttpClientFor(port);
        var response = await http.GetAsync("/x");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("could not reach", await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- recording

    [Fact]
    public async Task AProxiedExchangeIsRecordedAsAHandler()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl, recordTo: _dir);

        using var http = LiveServer.HttpClientFor(port);
        await http.GetStringAsync("/users/7");

        var recorded = Directory.GetFiles(_dir, "*.yaml").Single();
        Assert.Equal("get-users-7.yaml", Path.GetFileName(recorded));
    }

    [Fact]
    public async Task ARecordingReplaysWithoutTheUpstream()
    {
        // The whole point: capture once, then run with the real service gone.
        var (upstream, upstreamUrl) = await StartUpstreamAsync();

        await using (var recorder = await LiveServer.StartAsync(portCount: 1))
        {
            var (client, port) = await recorder.ConnectClientAsync(output);
            client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl, recordTo: _dir);

            using var http = LiveServer.HttpClientFor(port);
            await http.GetStringAsync("/users/7");
        }

        // Upstream goes away entirely.
        await upstream.DisposeAsync();

        await using var replay = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"));

        using var replayHttp = LiveServer.HttpClientFor(replay.Ports[0]);
        var body = await replayHttp.GetStringAsync("/users/7");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("7", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("Alice", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ReplayingTheSameCallOverwritesItsOwnRecording()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl, recordTo: _dir);

        using var http = LiveServer.HttpClientFor(port);
        await http.GetStringAsync("/users/7");
        await http.GetStringAsync("/users/7");
        await http.GetStringAsync("/users/8");

        // Two distinct calls, not three recordings.
        Assert.Equal(2, Directory.GetFiles(_dir, "*.yaml").Length);
    }

    [Fact]
    public async Task AFailedUpstreamCallIsNotRecorded()
    {
        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/x").ProxiesTo("http://localhost:1", recordTo: _dir);

        using var http = LiveServer.HttpClientFor(port);
        await http.GetAsync("/x");

        Assert.Empty(Directory.GetFiles(_dir, "*.yaml"));
    }

    [Fact]
    public async Task ATextResponseIsRecordedAsText()
    {
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/text").ProxiesTo(upstreamUrl, recordTo: _dir);

        using var http = LiveServer.HttpClientFor(port);
        await http.GetStringAsync("/text");

        var yaml = File.ReadAllText(Directory.GetFiles(_dir, "*.yaml").Single());

        Assert.Contains("text:", yaml);
        Assert.Contains("plain words", yaml);
    }

    [Fact]
    public async Task AJsonResponseIsRecordedAsStructuredYaml()
    {
        // Structured rather than a blob of escaped text, so the fixture stays editable.
        var (upstream, upstreamUrl) = await StartUpstreamAsync();
        await using var _ = upstream;

        await using var mock = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await mock.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/users/{id}").ProxiesTo(upstreamUrl, recordTo: _dir);

        using var http = LiveServer.HttpClientFor(port);
        await http.GetStringAsync("/users/7");

        var yaml = File.ReadAllText(Directory.GetFiles(_dir, "*.yaml").Single());

        Assert.Contains("json:", yaml);
        Assert.Contains("name: Alice", yaml);
    }

    [Fact]
    public void ARecordedExchangeIsValidLoadableYaml()
    {
        var request = new FortitudeRequest
        {
            Method = "GET",
            BaseUrl = "http://localhost",
            Route = "/things",
            RawQuery = "?page=2",
            Url = "http://localhost/things?page=2",
            Protocol = "HTTP/1.1",
            Query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["page"] = ["2"] }
        };

        var response = new FortitudeResponse(request.RequestId)
            .Ok(new { message = "a: value with yaml syntax" })
            .WithHeader("X-Trace", "abc");

        var yaml = FortitudeYamlGenerator.ExchangeToHandlerYaml(request, response);
        var handler = FortitudeYamlLoader.FromYamlMulti(yaml).Single();

        Assert.True(handler.Matches(request));
    }

    [Fact]
    public void RecordingRequiresADirectory()
    {
        var request = new FortitudeRequest
        {
            Method = "GET", BaseUrl = "http://x", Route = "/a",
            Url = "http://x/a", Protocol = "HTTP/1.1"
        };

        Assert.Throws<ArgumentException>(() =>
            FortitudeProxy.Record(request, new FortitudeResponse(Guid.NewGuid()).Ok(), "  "));
    }
}
