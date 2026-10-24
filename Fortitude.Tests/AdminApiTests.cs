using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     The admin REST API, exercised the way a non-.NET caller would use it: over real HTTP,
///     with no Fortitude client library involved.
/// </summary>
public sealed class AdminApiTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = CreateTempDirectory();

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortitude-admin-api", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<LiveServer> StartAsync(params (string, string)[] extra) =>
        LiveServer.StartAsync(
            portCount: 1,
            [("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"), .. extra]);

    private const string PingHandler = """
                                       handlers:
                                         - match:
                                             methods: [GET]
                                             route: /ping
                                           response:
                                             status: 200
                                             body:
                                               text: pong
                                       """;

    private static StringContent Yaml(string content) =>
        new(content, Encoding.UTF8, "application/yaml");

    // ---------------------------------------------------------------- handlers

    [Fact]
    public async Task ListingHandlers_ReportsTheDirectoryAndFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/handlers");

        Assert.Equal(_dir, json.GetProperty("directory").GetString());

        var file = json.GetProperty("files").EnumerateArray().Single();
        Assert.Equal("ping.yaml", file.GetProperty("fileName").GetString());
        Assert.Equal(1, file.GetProperty("handlers").GetInt32());
    }

    [Fact]
    public async Task AHandlerCanBeCreated_AndImmediatelyServesTraffic()
    {
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var created = await http.PutAsync("/fortitude/api/handlers/ping.yaml", Yaml(PingHandler));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // The point of the API: the mock is now live without restarting anything.
        Assert.Equal("pong", await http.GetStringAsync("/ping"));
    }

    [Fact]
    public async Task AHandlerCanBeReadBack()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var content = await http.GetStringAsync("/fortitude/api/handlers/ping.yaml");

        Assert.Contains("/ping", content);
    }

    [Fact]
    public async Task AHandlerCanBeReplaced()
    {
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        await http.PutAsync("/fortitude/api/handlers/ping.yaml", Yaml(PingHandler));
        Assert.Equal("pong", await http.GetStringAsync("/ping"));

        await http.PutAsync("/fortitude/api/handlers/ping.yaml", Yaml(
            PingHandler.Replace("text: pong", "text: pong-v2")));

        Assert.Equal("pong-v2", await http.GetStringAsync("/ping"));
    }

    [Fact]
    public async Task AHandlerCanBeDeleted()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        Assert.Equal("pong", await http.GetStringAsync("/ping"));

        var deleted = await http.DeleteAsync("/fortitude/api/handlers/ping.yaml");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/ping")).StatusCode);
    }

    [Fact]
    public async Task ReadingAMissingHandler_Returns404()
    {
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.GetAsync("/fortitude/api/handlers/nothing.yaml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SavingUnparseableYaml_Returns422_ButSaysWhy()
    {
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.PutAsync("/fortitude/api/handlers/broken.yaml",
            Yaml("handlers: [ unbalanced: yaml: here"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("could not be parsed", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("..%2F..%2Fescape.yaml")]
    public async Task AnUnacceptableFileName_IsRejected(string fileName)
    {
        // The traversal guard has to hold over HTTP too, not just in the UI.
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.PutAsync($"/fortitude/api/handlers/{fileName}", Yaml(PingHandler));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected the name to be rejected, got {(int)response.StatusCode}.");

        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(_dir, "..", "escape.yaml"))));
    }

    [Fact]
    public async Task ReloadPicksUpAFileWrittenOutsideTheApi()
    {
        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        File.WriteAllText(Path.Combine(_dir, "late.yaml"), PingHandler);

        // Hot reload is off in this fixture, so nothing has noticed yet.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/ping")).StatusCode);

        var reloaded = await http.PostAsync("/fortitude/api/handlers/reload", null);

        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        Assert.Equal("pong", await http.GetStringAsync("/ping"));
    }

    // ---------------------------------------------------------------- traffic

    [Fact]
    public async Task ObservedTrafficIsReadable()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        await http.GetAsync("/ping");

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/requests");

        Assert.True(json.GetProperty("total").GetInt64() >= 1);

        var request = json.GetProperty("requests").EnumerateArray().First();
        Assert.Equal("GET", request.GetProperty("method").GetString());
        Assert.Equal("/ping", request.GetProperty("route").GetString());
        Assert.Equal(200, request.GetProperty("response").GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task RequestBodiesAreIncluded()
    {
        await using var server = await StartAsync();
        var (client, port) = await server.ConnectClientAsync(output);
        client.Accepts().Post().HttpRoute("/orders").Returns((_, res) => res.Created(new { id = 1 }));

        using var http = LiveServer.HttpClientFor(port);
        await http.PostAsync("/orders", new StringContent("""{"item":"widget"}""",
            Encoding.UTF8, "application/json"));

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/requests");
        var request = json.GetProperty("requests").EnumerateArray()
            .First(r => r.GetProperty("route").GetString() == "/orders");

        Assert.Contains("widget", request.GetProperty("body").GetString());
    }

    [Fact]
    public async Task TrafficCanBeCleared()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        await http.GetAsync("/ping");

        var cleared = await http.DeleteAsync("/fortitude/api/requests");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/requests");
        Assert.Equal(0, json.GetProperty("retained").GetInt32());
    }

    [Fact]
    public async Task TheRequestLimitIsClamped()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync();
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        for (var i = 0; i < 5; i++) await http.GetAsync("/ping");

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/requests?limit=2");

        Assert.Equal(2, json.GetProperty("requests").GetArrayLength());
    }

    // ---------------------------------------------------------------- clients

    [Fact]
    public async Task ConnectedClientsAndTheirPortsAreVisible()
    {
        await using var server = await StartAsync();
        var (_, port) = await server.ConnectClientAsync(output);

        using var http = LiveServer.HttpClientFor(port);

        var json = await http.GetFromJsonAsync<JsonElement>("/fortitude/api/clients");

        Assert.Equal(1, json.GetProperty("count").GetInt32());
        Assert.Equal(port, json.GetProperty("clients").EnumerateArray().Single()
            .GetProperty("port").GetInt32());
    }

    // ---------------------------------------------------------------- switch

    [Fact]
    public async Task TheApiCanBeTurnedOff()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), PingHandler);

        await using var server = await StartAsync(("Settings:EnableAdminApi", "false"));
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        var response = await http.GetAsync("/fortitude/api/handlers");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        // Mocking still works; only the control surface is gone.
        Assert.Equal("pong", await http.GetStringAsync("/ping"));
    }

    [Fact]
    public async Task TheHealthEndpointStillAnswersWithTheApiOff()
    {
        await using var server = await StartAsync(("Settings:EnableAdminApi", "false"));
        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/fortitude/health")).StatusCode);
    }
}
