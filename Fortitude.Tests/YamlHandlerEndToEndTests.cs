using System.Net;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Exercises YAML-defined handlers through the full stack: real Kestrel, real HTTP, real
///     files on disk.
/// </summary>
public sealed class YamlHandlerEndToEndTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fortitude-yaml-e2e", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteFile(string name, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, name))!);
        File.WriteAllText(Path.Combine(_dir, name), content);
    }

    private Task<LiveServer> StartAsync(bool hotReload = false) =>
        LiveServer.StartAsync(
            portCount: 1,
            ("Settings:ConfigDirectory", _dir),
            ("Settings:HotReload", hotReload ? "true" : "false"));

    [Fact]
    public async Task AYamlHandler_ServesRealHttpTraffic()
    {
        WriteFile("ping.yaml", """
                               handlers:
                                 - match:
                                     methods: [GET]
                                     route: /ping
                                   response:
                                     status: 200
                                     body:
                                       text: pong
                               """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AYamlHandler_IsServedWithoutAnyConnectedClient()
    {
        // YAML handlers are matched server-side, before a request is forwarded anywhere, so
        // they answer even though nothing is connected over SignalR.
        WriteFile("standalone.yaml", """
                                     handlers:
                                       - match:
                                           methods: [GET]
                                           route: /standalone
                                         response:
                                           status: 204
                                     """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.GetAsync("/standalone");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AYmlFileInASubdirectory_IsServed()
    {
        WriteFile(Path.Combine("apis", "users.yml"), """
                                                     handlers:
                                                       - match:
                                                           methods: [GET]
                                                           route: /users/{id}
                                                         response:
                                                           status: 200
                                                           body:
                                                             json:
                                                               id: {{route.id}}
                                                               name: Alice
                                                     """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var body = await http.GetStringAsync("/users/42");

        Assert.Contains("\"id\":42", body);
        Assert.Contains("\"name\":\"Alice\"", body);
    }

    [Fact]
    public async Task EditingAYamlFile_ChangesTheResponseWithoutARestart()
    {
        WriteFile("greeting.yaml", """
                                   handlers:
                                     - match:
                                         methods: [GET]
                                         route: /greeting
                                       response:
                                         status: 200
                                         body:
                                           text: hello
                                   """);

        await using var server = await StartAsync(hotReload: true);
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        Assert.Equal("hello", await http.GetStringAsync("/greeting"));

        WriteFile("greeting.yaml", """
                                   handlers:
                                     - match:
                                         methods: [GET]
                                         route: /greeting
                                       response:
                                         status: 200
                                         body:
                                           text: goodbye
                                   """);

        // Poll rather than sleep a fixed amount: the watcher is debounced.
        var observed = await Poll(http, "/greeting", expected: "goodbye");

        Assert.Equal("goodbye", observed);
    }

    [Fact]
    public async Task AMalformedYamlFile_DoesNotStopTheServerStarting()
    {
        WriteFile("broken.yaml", "handlers: [ unbalanced: yaml: here");
        WriteFile("working.yaml", """
                                  handlers:
                                    - match:
                                        methods: [GET]
                                        route: /works
                                      response:
                                        status: 200
                                        body:
                                          text: still here
                                  """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        Assert.Equal("still here", await http.GetStringAsync("/works"));
    }

    [Fact]
    public async Task ABodyThatIsNotJson_DoesNotMatchAJsonPredicate_AndDoesNotError()
    {
        // Regression: EvaluateJsonPredicate parsed the body unguarded, so a non-JSON body
        // threw out of Matches() and surfaced as a 500 instead of simply not matching.
        WriteFile("json-match.yaml", """
                                     handlers:
                                       - match:
                                           methods: [POST]
                                           route: /orders
                                           body:
                                             json: total > 100
                                         response:
                                           status: 201
                                           body:
                                             text: accepted
                                     """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.PostAsync("/orders",
            new StringContent("this is definitely not json"));

        // No YAML handler matched and no client is connected, so the request goes unhandled.
        // The important part is that it is not a 500.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task AJsonPredicate_StillMatchesWhenTheBodyQualifies()
    {
        WriteFile("json-match.yaml", """
                                     handlers:
                                       - match:
                                           methods: [POST]
                                           route: /orders
                                           body:
                                             json: total > 100
                                         response:
                                           status: 201
                                           body:
                                             text: accepted
                                     """);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var accepted = await http.PostAsync("/orders",
            new StringContent("""{"total": 250}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal("accepted", await accepted.Content.ReadAsStringAsync());

        var rejected = await http.PostAsync("/orders",
            new StringContent("""{"total": 5}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.Created, rejected.StatusCode);
    }

    [Fact]
    public async Task AYamlHandler_TakesPrecedenceOverAConnectedClient()
    {
        // Documented behaviour: YAML is matched first, so it wins for the same route.
        WriteFile("precedence.yaml", """
                                     handlers:
                                       - match:
                                           methods: [GET]
                                           route: /contested
                                         response:
                                           status: 200
                                           body:
                                             text: from-yaml
                                     """);

        await using var server = await StartAsync();

        var (client, port) = await server.ConnectClientAsync(output);
        var handler = client.Accepts().Get().HttpRoute("/contested")
            .Returns((_, res) => res.Ok("from-client"));

        using var http = LiveServer.HttpClientFor(port);

        Assert.Equal("from-yaml", await http.GetStringAsync("/contested"));

        // The client never saw the request at all.
        Assert.Empty(handler.ReceivedRequests);
    }

    private static async Task<string> Poll(HttpClient http, string route, string expected)
    {
        var last = string.Empty;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            last = await http.GetStringAsync(route);
            if (last == expected) return last;
            await Task.Delay(100);
        }

        return last;
    }
}
