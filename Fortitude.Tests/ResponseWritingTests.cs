using System.Net;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     How a handler's response reaches the wire: status, headers, content type and body.
/// </summary>
public class ResponseWritingTests(ITestOutputHelper output)
{
    private async Task<(LiveServer Server, HttpClient Http, FortitudeClient Client)> StartAsync()
    {
        var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        return (server, LiveServer.HttpClientFor(port), client);
    }

    [Fact]
    public async Task AHandlerCanOverrideContentType()
    {
        // Regression: headers were only applied if not already present, so a handler could
        // not replace a header the server had set.
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/xml")
            .Returns((_, res) => res.SetTextBody("<ok/>").WithHeader("Content-Type", "application/xml"));

        var response = await http.GetAsync("/xml");

        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CustomHeadersReachTheClient()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/headers")
            .Returns((_, res) => res.Ok("body")
                .WithHeader("X-Trace", "abc123")
                .WithETag("\"v1\"")
                .WithCacheControl("no-store"));

        var response = await http.GetAsync("/headers");

        Assert.Equal("abc123", response.Headers.GetValues("X-Trace").Single());
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AContentTypeIsSentEvenWithAnEmptyBody()
    {
        // Regression: ContentType was only applied when a body was present.
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/empty")
            .Returns((_, res) =>
            {
                res.Status = 200;
                res.ClearBody();
                res.ContentType = "application/json";
            });

        var response = await http.GetAsync("/empty");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ContentLengthIsNotTakenFromTheHandler()
    {
        // A handler-supplied Content-Length would contradict the body Kestrel actually writes.
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/lies")
            .Returns((_, res) => res.Ok("twelve chars").WithHeader("Content-Length", "99999"));

        var response = await http.GetAsync("/lies");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("twelve chars", body);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task StatusCodesAreForwardedVerbatim()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/teapot").Returns((_, res) => res.SetText(418, "short and stout"));

        var response = await http.GetAsync("/teapot");

        Assert.Equal(418, (int)response.StatusCode);
        Assert.Equal("short and stout", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoContent_HasNoBody()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Delete().HttpRoute("/gone").Returns((_, res) => res.NoContent());

        var response = await http.DeleteAsync("/gone");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BinaryBodiesSurviveIntact()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

        client.Accepts().Get().HttpRoute("/blob")
            .Returns((_, res) => res.SetBinary(payload, "application/octet-stream"));

        var received = await http.GetByteArrayAsync("/blob");

        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task AFileResponse_SetsContentDisposition()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/report")
            .Returns((_, res) => res.File("data"u8.ToArray(), "report.pdf", "application/pdf"));

        var response = await http.GetAsync("/report");

        Assert.Equal("report.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ARedirect_SetsLocation()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/old").Returns((_, res) => res.Redirect("/new"));

        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = http.BaseAddress
        };

        var response = await noRedirect.GetAsync("/old");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/new", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ARequestMatchingNoHandler_Returns501()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok());

        var response = await http.GetAsync("/unknown");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task AHandlerThatThrows_Returns500RatherThanHanging()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        client.Accepts().Get().HttpRoute("/boom")
            .Returns((_, _) => throw new InvalidOperationException("handler exploded"));

        var response = await http.GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ALargeBody_IsRoundTripped()
    {
        var (server, http, client) = await StartAsync();
        await using var _ = server;
        using var __ = http;

        var big = new string('x', 2 * 1024 * 1024);

        client.Accepts().Post().HttpRoute("/echo")
            .Returns((req, res) => res.SetTextBody(
                System.Text.Encoding.UTF8.GetString(req.Body ?? [])));

        var response = await http.PostAsync("/echo", new StringContent(big));

        Assert.Equal(big, await response.Content.ReadAsStringAsync());
    }
}
