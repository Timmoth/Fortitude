using System.Globalization;
using System.Net;
using System.Text.Json;
using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     Built-in template functions and file-backed response bodies.
/// </summary>
public sealed class TemplateFunctionAndFileBodyTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fortitude-fixtures", Guid.NewGuid().ToString("N"));

    public TemplateFunctionAndFileBodyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static async Task<string> RenderText(string yaml, string route = "/thing")
    {
        var handler = FortitudeYamlLoader.FromYamlSingle(yaml);
        var response = await handler.HandleRequestAsync(Request(route));
        return System.Text.Encoding.UTF8.GetString(response.Body ?? []);
    }

    private static FortitudeRequest Request(string route) => new()
    {
        Method = "GET",
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- functions

    [Fact]
    public async Task Uuid_RendersAFreshGuidEachTime()
    {
        const string yaml = """
                            match:
                              route: /id
                            response:
                              status: 200
                              body:
                                text: "{{uuid}}"
                            """;

        var first = await RenderText(yaml, "/id");
        var second = await RenderText(yaml, "/id");

        Assert.True(Guid.TryParse(first, out _), $"'{first}' is not a GUID.");
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Guid_IsAnAliasForUuid()
    {
        var rendered = await RenderText("""
                                        match:
                                          route: /id
                                        response:
                                          status: 200
                                          body:
                                            text: "{{guid}}"
                                        """, "/id");

        Assert.True(Guid.TryParse(rendered, out _));
    }

    [Fact]
    public async Task Now_RendersAnIso8601Timestamp()
    {
        var rendered = await RenderText("""
                                        match:
                                          route: /t
                                        response:
                                          status: 200
                                          body:
                                            text: "{{now}}"
                                        """, "/t");

        var parsed = DateTimeOffset.Parse(rendered, CultureInfo.InvariantCulture);

        Assert.True((DateTimeOffset.UtcNow - parsed).Duration() < TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("now.unix")]
    [InlineData("now.unixms")]
    public async Task Now_RendersUnixTimestamps(string expression)
    {
        var yaml = "match:\n  route: /t\nresponse:\n  status: 200\n  body:\n    text: \"{{"
                   + expression + "}}\"\n";

        var rendered = await RenderText(yaml, "/t");

        Assert.True(long.TryParse(rendered, out var value), $"'{rendered}' is not a number.");
        Assert.True(value > 0);
    }

    [Fact]
    public async Task Now_RendersDateAndTimeParts()
    {
        var date = await RenderText("""
                                    match:
                                      route: /t
                                    response:
                                      status: 200
                                      body:
                                        text: "{{now.date}}"
                                    """, "/t");

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", date);
    }

    [Fact]
    public async Task RandomInt_StaysWithinItsBounds()
    {
        for (var i = 0; i < 20; i++)
        {
            var rendered = await RenderText("""
                                            match:
                                              route: /n
                                            response:
                                              status: 200
                                              body:
                                                text: "{{randomInt 5 10}}"
                                            """, "/n");

            var value = int.Parse(rendered, CultureInfo.InvariantCulture);
            Assert.InRange(value, 5, 10);
        }
    }

    [Fact]
    public async Task AnUnknownFunction_RendersEmpty()
    {
        // Consistent with how an unresolvable path behaves, rather than leaking the template.
        var rendered = await RenderText("""
                                        match:
                                          route: /x
                                        response:
                                          status: 200
                                          body:
                                            text: "[{{notAThing}}]"
                                        """, "/x");

        Assert.Equal("[]", rendered);
    }

    [Fact]
    public async Task FunctionsWorkInsideJsonBodies()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle("""
                                                         match:
                                                           route: /users/{id}
                                                         response:
                                                           status: 200
                                                           body:
                                                             json:
                                                               id: "{{route.id}}"
                                                               traceId: "{{uuid}}"
                                                               createdAt: "{{now}}"
                                                         """);

        var response = await handler.HandleRequestAsync(Request("/users/7"));
        using var json = JsonDocument.Parse(response.Body!);

        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
        Assert.True(Guid.TryParse(json.RootElement.GetProperty("traceId").GetString(), out _));
        Assert.NotEmpty(json.RootElement.GetProperty("createdAt").GetString()!);
    }

    [Fact]
    public async Task FunctionsWorkInResponseHeaders()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle("""
                                                         match:
                                                           route: /x
                                                         response:
                                                           status: 200
                                                           headers:
                                                             X-Request-Id: "{{uuid}}"
                                                         """);

        var response = await handler.HandleRequestAsync(Request("/x"));

        Assert.True(Guid.TryParse(response.Headers["X-Request-Id"], out _));
    }

    // ---------------------------------------------------------------- file bodies

    [Fact]
    public async Task AFileBody_IsServedWithAnInferredContentType()
    {
        File.WriteAllText(Path.Combine(_dir, "users.json"), """[{"id":1,"name":"Alice"}]""");
        var yaml = Path.Combine(_dir, "handlers.yaml");
        File.WriteAllText(yaml, """
                                handlers:
                                  - match:
                                      route: /users
                                    response:
                                      status: 200
                                      body:
                                        file: users.json
                                """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/users"));

        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("""[{"id":1,"name":"Alice"}]""", System.Text.Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task AFilePath_ResolvesRelativeToItsHandlerFile()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "apis"));
        Directory.CreateDirectory(Path.Combine(_dir, "apis", "fixtures"));

        File.WriteAllText(Path.Combine(_dir, "apis", "fixtures", "payload.txt"), "from the fixture");
        File.WriteAllText(Path.Combine(_dir, "apis", "handlers.yaml"), """
                                                                      handlers:
                                                                        - match:
                                                                            route: /p
                                                                          response:
                                                                            status: 200
                                                                            body:
                                                                              file: fixtures/payload.txt
                                                                      """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/p"));

        Assert.Equal("from the fixture", System.Text.Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task AFileBody_IsTemplated()
    {
        File.WriteAllText(Path.Combine(_dir, "user.json"), """{"id":"{{route.id}}","trace":"{{uuid}}"}""");
        File.WriteAllText(Path.Combine(_dir, "handlers.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /users/{id}
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       file: user.json
                                                               """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/users/42"));

        using var json = JsonDocument.Parse(response.Body!);

        Assert.Equal("42", json.RootElement.GetProperty("id").GetString());
        Assert.True(Guid.TryParse(json.RootElement.GetProperty("trace").GetString(), out _));
    }

    [Fact]
    public async Task ABinaryFile_IsServedVerbatim()
    {
        var bytes = Enumerable.Range(0, 512).Select(i => (byte)(i % 256)).ToArray();
        File.WriteAllBytes(Path.Combine(_dir, "blob.png"), bytes);
        File.WriteAllText(Path.Combine(_dir, "handlers.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /image
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       file: blob.png
                                                               """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/image"));

        Assert.Equal("image/png", response.ContentType);
        Assert.Equal(bytes, response.Body);
    }

    [Fact]
    public async Task AnExplicitContentType_OverridesTheExtension()
    {
        File.WriteAllText(Path.Combine(_dir, "data.txt"), "id,name");
        File.WriteAllText(Path.Combine(_dir, "handlers.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /d
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       file: data.txt
                                                                       contentType: text/csv
                                                               """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/d"));

        Assert.Equal("text/csv", response.ContentType);
    }

    [Fact]
    public async Task AMissingFile_Returns500NamingTheFile()
    {
        File.WriteAllText(Path.Combine(_dir, "handlers.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /gone
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       file: nowhere.json
                                                               """);

        var handler = FortitudeYamlLoader.LoadHandlers(_dir).Single();
        var response = await handler.HandleRequestAsync(Request("/gone"));

        Assert.Equal(500, response.Status);
        Assert.Contains("nowhere.json", System.Text.Encoding.UTF8.GetString(response.Body!));
    }

    [Fact]
    public async Task AFileBody_IsServedOverRealHttp()
    {
        File.WriteAllText(Path.Combine(_dir, "big.json"),
            JsonSerializer.Serialize(Enumerable.Range(0, 500).Select(i => new { id = i, name = $"user-{i}" })));

        File.WriteAllText(Path.Combine(_dir, "handlers.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /users
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       file: big.json
                                                               """);

        await using var server = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"));

        using var http = LiveServer.HttpClientFor(server.Ports[0]);
        var response = await http.GetAsync("/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, json.RootElement.GetArrayLength());
    }
}
