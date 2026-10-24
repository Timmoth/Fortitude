using System.Net;
using System.Net.Http.Json;
using Fortitude.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     In-process intercept mode, where outgoing <see cref="HttpClient" /> calls are answered
///     by Fortitude without a server.
/// </summary>
/// <remarks>
///     This is one of the two headline modes but had a single test. The matcher surface is
///     shared with server mode, so what matters here is that the interception path itself
///     behaves the same: matching, bodies, headers, status codes and failures.
/// </remarks>
public class InterceptModeTests(ITestOutputHelper output)
{
    private static Task<HttpResponseMessage> Send(
        FortitudeClient client, HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (content != null) request.Content = content;

        return client.TryHandle(request, CancellationToken.None);
    }

    // ---------------------------------------------------------------- matching

    [Fact]
    public async Task AMatchingRouteIsAnswered()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/users").Returns((_, res) => res.Ok("users"));

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/users");

        Assert.Equal("users", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheMethodIsHonoured()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Post().HttpRoute("/users").Returns((_, res) => res.Created(new { id = 1 }));

        Assert.Equal(HttpStatusCode.NotImplemented,
            (await Send(client, HttpMethod.Get, "https://api.example.com/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Send(client, HttpMethod.Post, "https://api.example.com/users")).StatusCode);
    }

    [Fact]
    public async Task RouteTemplatesMatch()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/users/{id}")
            .Returns((req, res) => res.Ok(req.Route.Split('/').Last()));

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/users/42");

        Assert.Equal("42", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task QueryParametersAreMatchable()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/search").QueryParam("q", "widgets")
            .Returns((_, res) => res.Ok("found"));

        Assert.Equal(HttpStatusCode.OK,
            (await Send(client, HttpMethod.Get, "https://api.example.com/search?q=widgets")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await Send(client, HttpMethod.Get, "https://api.example.com/search?q=sprockets")).StatusCode);
    }

    [Fact]
    public async Task HeadersAreMatchable()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/secure").BearerToken("secret")
            .Returns((_, res) => res.Ok("granted"));

        var withToken = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/secure");
        withToken.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

        Assert.Equal(HttpStatusCode.OK,
            (await client.TryHandle(withToken, CancellationToken.None)).StatusCode);

        Assert.Equal(HttpStatusCode.NotImplemented,
            (await Send(client, HttpMethod.Get, "https://api.example.com/secure")).StatusCode);
    }

    [Fact]
    public async Task JsonBodyPredicatesAreEvaluated()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Post().HttpRoute("/orders")
            .JsonBody<Order>(o => o.Total > 100)
            .Returns((_, res) => res.Created(new { id = 1 }));

        var big = JsonContent.Create(new Order(250));
        var small = JsonContent.Create(new Order(5));

        Assert.Equal(HttpStatusCode.Created,
            (await Send(client, HttpMethod.Post, "https://api.example.com/orders", big)).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await Send(client, HttpMethod.Post, "https://api.example.com/orders", small)).StatusCode);
    }

    [Fact]
    public async Task TheRequestBodyReachesTheResponder()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Post().HttpRoute("/echo")
            .Returns((req, res) => res.SetTextBody(req.Body.FromMessageBody() ?? string.Empty));

        var response = await Send(client, HttpMethod.Post, "https://api.example.com/echo",
            new StringContent("round trip"));

        Assert.Equal("round trip", await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- responses

    [Fact]
    public async Task ResponseHeadersReachTheCaller()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/x")
            .Returns((_, res) => res.Ok("body").WithHeader("X-Trace", "abc").WithETag("\"v1\""));

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/x");

        Assert.Equal("abc", response.Headers.GetValues("X-Trace").Single());
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task TheInterceptorAttachesTheRequestToTheResponse()
    {
        // Resilience libraries and logging handlers read response.RequestMessage. TryHandle
        // does not set it; the DelegatingHandler is what must, so test that directly.
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/x").Returns((_, res) => res.Ok());

        using var interceptor = new HttpClientInterceptorMiddleware(client)
        {
            InnerHandler = new HttpClientHandler()
        };

        using var invoker = new HttpMessageInvoker(interceptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/x");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Same(request, response.RequestMessage);
    }

    [Fact]
    public async Task StatusCodesAndReasonPhrasesAreSet()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/gone").Returns((_, res) => res.NotFound("vanished"));

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/gone");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
        Assert.Equal("vanished", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BinaryResponsesSurvive()
    {
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i % 256)).ToArray();

        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/blob").Returns((_, res) => res.SetBinary(payload));

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/blob");

        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnAsyncResponderIsAwaited()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/async").Returns(async (_, res) =>
        {
            await Task.Delay(50);
            res.Ok("eventually");
        });

        var response = await Send(client, HttpMethod.Get, "https://api.example.com/async");

        Assert.Equal("eventually", await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- wiring

    [Fact]
    public async Task RegisteredClientsAreInterceptedThroughDi()
    {
        // The real integration: a typed HttpClient built by the container never leaves the
        // process.
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/external").Returns((_, res) => res.Ok("intercepted"));

        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://unreachable.invalid"));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var http = factory.CreateClient("api");
        var body = await http.GetStringAsync("/external");

        Assert.Equal("intercepted", body);
    }

    [Fact]
    public async Task AnUnmatchedCallThroughDiReturns501()
    {
        var client = FortitudeClient.Create(output);
        client.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok());

        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://unreachable.invalid"));

        await using var provider = services.BuildServiceProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");

        var response = await http.GetAsync("/unknown");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Single(client.UnmatchedRequests);
    }

    [Fact]
    public async Task TheSameHandlerFeaturesWorkAsInServerMode()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/flaky").Returns((_, res) => res.Ok("recovered"));
        client.Accepts().Get().HttpRoute("/flaky").Once().Returns((_, res) => res.InternalServerError());

        Assert.Equal(HttpStatusCode.InternalServerError,
            (await Send(client, HttpMethod.Get, "https://api.example.com/flaky")).StatusCode);
        Assert.Equal("recovered",
            await (await Send(client, HttpMethod.Get, "https://api.example.com/flaky"))
                .Content.ReadAsStringAsync());
    }

    private sealed record Order(decimal Total);
}
