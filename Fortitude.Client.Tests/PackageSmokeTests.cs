using System.Diagnostics;
using System.Net;
using System.Text;
using Fortitude.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Client;

/// <summary>
///     The shipped package, exercised with nothing but the package referenced.
/// </summary>
/// <remarks>
///     Every other test project references Fortitude.Server as well as Fortitude.Client, so
///     none of them would notice the package picking up a dependency that only the server
///     supplies. This one references the client alone, which is what consumers install.
///     <para>
///         It is a smoke suite, not a second copy of the main one: one path through each
///         public capability, enough to catch the package being unusable on its own.
///     </para>
/// </remarks>
public class PackageSmokeTests
{
    private static FortitudeRequest Request(
        string method = "GET",
        string route = "/users",
        string query = "",
        string? body = null) => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        RawQuery = query,
        Url = "http://localhost" + route + query,
        Protocol = "HTTP/1.1",
        Body = body == null ? null : Encoding.UTF8.GetBytes(body)
    };

    // ---------------------------------------------------------------- matching

    [Fact]
    public void AHandlerMatchesTheMethodAndRoute()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/users").Returns((_, res) => res.Ok());

        Assert.True(handler.Matches(Request()));
        Assert.False(handler.Matches(Request("POST")));
        Assert.False(handler.Matches(Request(route: "/orders")));
    }

    [Fact]
    public void ARouteTemplateCapturesASegment()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/users/{id}").Returns((_, res) => res.Ok());

        Assert.True(handler.Matches(Request(route: "/users/42")));
        Assert.False(handler.Matches(Request(route: "/users/42/orders")));
    }

    [Fact]
    public async Task AHandlerRecordsWhatItReceived()
    {
        var handler = FortitudeHandler.Accepts().Post().HttpRoute("/users").Returns((_, res) => res.Created(new { id = 1 }));

        await handler.HandleRequestAsync(Request("POST"));

        handler.ShouldHaveReceivedOnce();
        Assert.Single(handler.ReceivedRequests);
    }

    [Fact]
    public async Task ACallLimitRetiresAHandler()
    {
        var handler = FortitudeHandler.Accepts().Get().HttpRoute("/users").Once().Returns((_, res) => res.Ok());

        await handler.HandleRequestAsync(Request());

        Assert.True(handler.IsExhausted);
        Assert.False(handler.Matches(Request()));
    }

    // ---------------------------------------------------------------- responses

    [Fact]
    public async Task AJsonResponseIsSerialised()
    {
        var handler = FortitudeHandler.Accepts().Get().Returns((_, res) => res.Ok(new { id = 7 }));

        var response = await handler.HandleRequestAsync(Request());

        Assert.Equal(200, response.Status);
        Assert.Contains("\"id\":7", Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task AResponseConvertsToAnHttpResponseMessage()
    {
        var handler = FortitudeHandler.Accepts().Get().Returns((_, res) => res.NotFound());

        var response = await handler.HandleRequestAsync(Request());

        using var message = response.ToHttpResponseMessage();

        Assert.Equal(HttpStatusCode.NotFound, message.StatusCode);
    }

    // ---------------------------------------------------------------- intercept mode

    [Fact]
    public async Task InterceptModeAnswersWithoutLeavingTheProcess()
    {
        var client = FortitudeClient.Create(_ => { });
        client.Accepts().Get().HttpRoute("/users/{id}").Returns((_, res) => res.Ok(new { id = 1 }));

        using var http = InterceptedClient(client);

        var response = await http.GetAsync("/users/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InterceptModeAnswersAnUnmatchedCallWith501()
    {
        var client = FortitudeClient.Create(_ => { });
        client.Accepts().Get().HttpRoute("/users").Returns((_, res) => res.Ok());

        using var http = InterceptedClient(client);

        var response = await http.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Single(client.UnmatchedRequests);
        Assert.Throws<FortitudeVerificationException>(client.VerifyNoUnmatchedRequests);
    }

    [Fact]
    public async Task InterceptModeHonoursTheCallersTimeout()
    {
        var client = FortitudeClient.Create(_ => { });

        client.Accepts().Get().HttpRoute("/slow")
            .WithDelay(TimeSpan.FromSeconds(30))
            .Returns((_, res) => res.Ok());

        using var http = InterceptedClient(client, TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync("/slow"));

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The call took {stopwatch.ElapsedMilliseconds}ms despite a 300ms timeout.");
    }

    private static HttpClient InterceptedClient(FortitudeClient client, TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddFortitudeClient(client);
        services.AddHttpClient("sut", c =>
        {
            c.BaseAddress = new Uri("http://example.com");
            if (timeout is { } value) c.Timeout = value;
        });

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("sut");
    }

    // ---------------------------------------------------------------- yaml

    [Fact]
    public async Task AYamlHandlerLoadsAndServes()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle("""
                                                         match:
                                                           methods: [GET]
                                                           route: /users/{id}
                                                         response:
                                                           status: 200
                                                           body:
                                                             json:
                                                               id: "{{route.id}}"
                                                         """);

        Assert.True(handler.Matches(Request(route: "/users/42")));

        var response = await handler.HandleRequestAsync(Request(route: "/users/42"));

        Assert.Contains("42", Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task ABase64YamlBodyServesExactBytes()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE];

        var handler = FortitudeYamlLoader.FromYamlSingle($"""
                                                          match:
                                                            methods: [GET]
                                                            route: /logo
                                                          response:
                                                            status: 200
                                                            body:
                                                              base64: {Convert.ToBase64String(bytes)}
                                                              contentType: image/png
                                                          """);

        var response = await handler.HandleRequestAsync(Request(route: "/logo"));

        Assert.Equal(bytes, response.Body);
    }

    // ---------------------------------------------------------------- generators

    [Fact]
    public void ToCurlQuotesABodyContainingAnApostrophe()
    {
        var curl = Request("POST", body: """{"name":"O'Brien"}""").ToCurl();

        Assert.Contains("""O'\''Brien""", curl);
        Assert.Contains("/users", FortitudeYamlGenerator.CurlToMatchYaml(curl));
    }

    [Fact]
    public void StrictUtf8DecodingSeparatesTextFromBinary()
    {
        Assert.True(FortitudeText.TryDecodeUtf8(Encoding.UTF8.GetBytes("hello"), out var text));
        Assert.Equal("hello", text);

        Assert.False(FortitudeText.TryDecodeUtf8([0xFF, 0xFE, 0x00], out _));
    }
}
