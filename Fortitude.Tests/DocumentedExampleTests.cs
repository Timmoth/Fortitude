using System.Net.Http.Json;
using Fortitude.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Executable versions of the examples in the README and guides.
/// </summary>
/// <remarks>
///     Code samples in documentation rot silently: nothing compiles them, so an API change
///     leaves behind instructions that no longer work. Anything shown to a reader as the way
///     to do something should have a counterpart here.
/// </remarks>
public class DocumentedExampleTests(ITestOutputHelper output)
{
    private sealed record User(int Id, string Name);

    /// <summary>The typed client the README's quick start stands in for.</summary>
    private sealed class UserClient(HttpClient http)
    {
        public Task<User?> GetAsync(int id) => http.GetFromJsonAsync<User>($"/users/{id}");
    }

    [Fact]
    public async Task ReadmeQuickStart()
    {
        // README: "Quick start"
        var fortitude = FortitudeClient.Create(output);

        var handler = fortitude.Accepts()
            .Get()
            .HttpRoute("/users/{id}")
            .Returns((req, res) => res.Ok(new User(1, "Alice")));

        var services = new ServiceCollection();
        services.AddFortitudeClient(fortitude);
        services.AddHttpClient<UserClient>(c => c.BaseAddress = new Uri("https://api.example.com"));

        var users = services.BuildServiceProvider().GetRequiredService<UserClient>();

        var user = await users.GetAsync(1);

        Assert.Equal("Alice", user!.Name);
        handler.ShouldHaveReceivedOnce();
        fortitude.VerifyNoUnmatchedRequests();
    }

    [Fact]
    public async Task ReadmeYamlHandlerExample()
    {
        // README: the YAML handler example, which returned a 500 before the unquoted
        // template form was supported. Pinned so it cannot regress again.
        var handler = FortitudeYamlLoader.FromYamlMulti("""
                                                        handlers:
                                                          - match:
                                                              methods: [POST]
                                                              route: /users/{id}
                                                              body:
                                                                json: email == "alice@example.com"
                                                            response:
                                                              status: 201
                                                              body:
                                                                json:
                                                                  id: {{route.id}}
                                                                  name: {{body.name}}
                                                                  email: {{body.email}}
                                                        """).Single();

        var request = new FortitudeRequest
        {
            Method = "POST",
            BaseUrl = "http://localhost",
            Route = "/users/7",
            Url = "http://localhost/users/7",
            Protocol = "HTTP/1.1",
            Body = """{"name":"Alice","email":"alice@example.com"}"""u8.ToArray()
        };

        Assert.True(handler.Matches(request));

        var response = await handler.HandleRequestAsync(request);
        var body = System.Text.Encoding.UTF8.GetString(response.Body!);

        Assert.Equal(201, response.Status);
        Assert.Contains("\"id\":7", body);
        Assert.Contains("\"name\":\"Alice\"", body);
        Assert.Contains("\"email\":\"alice@example.com\"", body);
    }

    [Fact]
    public async Task HandlerBuilderGuideRetryExample()
    {
        // docs/handler-builder-usage.md: "Limiting how many times a handler answers"
        var fortitude = FortitudeClient.Create(output);

        fortitude.Accepts().Get().HttpRoute("/flaky")
            .Returns((req, res) => res.Ok("recovered"));

        fortitude.Accepts().Get().HttpRoute("/flaky")
            .Twice()
            .Returns((req, res) => res.InternalServerError("down"));

        var statuses = new List<int>();
        for (var i = 0; i < 3; i++)
            statuses.Add((int)(await fortitude.TryHandle(
                new HttpRequestMessage(HttpMethod.Get, "http://x/flaky"), CancellationToken.None)).StatusCode);

        Assert.Equal([500, 500, 200], statuses);
    }

    [Fact]
    public async Task HandlerBuilderGuideScenarioExample()
    {
        // docs/handler-builder-usage.md: "Stateful flows"
        var fortitude = FortitudeClient.Create(output);

        fortitude.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState("Started").ThenState("Running")
            .Returns((req, res) => res.Ok(new { status = "queued" }));

        fortitude.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState("Running").ThenState("Done")
            .Returns((req, res) => res.Ok(new { status = "running" }));

        fortitude.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState("Done")
            .Returns((req, res) => res.Ok(new { status = "complete" }));

        var seen = new List<string>();
        for (var i = 0; i < 3; i++)
            seen.Add(await (await fortitude.TryHandle(
                    new HttpRequestMessage(HttpMethod.Get, "http://x/jobs/1"), CancellationToken.None))
                .Content.ReadAsStringAsync());

        Assert.Contains("queued", seen[0]);
        Assert.Contains("running", seen[1]);
        Assert.Contains("complete", seen[2]);

        Assert.Equal("Done", fortitude.Scenarios.GetState("job"));
        fortitude.Scenarios.Reset("job");
        Assert.Equal(FortitudeScenarios.InitialState, fortitude.Scenarios.GetState("job"));
    }

    [Fact]
    public async Task HandlerBuilderGuideCatchAllExample()
    {
        // docs/handler-builder-usage.md: "Controlling which handler wins"
        var fortitude = FortitudeClient.Create(output);

        fortitude.Accepts().Get().RouteStartsWith("/")
            .Priority(-100)
            .Returns((req, res) => res.NotFound("no handler for this route"));

        fortitude.Accepts().Get().HttpRoute("/known").Returns((_, res) => res.Ok("known"));

        var known = await fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/known"), CancellationToken.None);
        var unknown = await fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/anything-else"), CancellationToken.None);

        Assert.Equal("known", await known.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task HandlerBuilderGuideFaultExample()
    {
        // docs/handler-builder-usage.md: "Injecting faults"
        var fortitude = FortitudeClient.Create(output);

        fortitude.Accepts().Get().HttpRoute("/users").ReturnsFault(HttpFault.ConnectionReset);

        await Assert.ThrowsAsync<HttpRequestException>(() => fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/users"), CancellationToken.None));
    }

    [Fact]
    public async Task MockingGuideVerificationExample()
    {
        // docs/mocking.md: "Asserting on what the mock received"
        var fortitude = FortitudeClient.Create(output);

        var handler = fortitude.Accepts().Post().HttpRoute("/users")
            .Returns((_, res) => res.Created(new User(1, "Alice")));

        await fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Post, "http://x/users"), CancellationToken.None);

        handler.ShouldHaveReceivedOnce();
        handler.ShouldHaveReceivedAtLeast(1);
        handler.ShouldHaveReceivedAtMost(5);
        handler.ShouldHaveReceived(1, r => r.Method == "POST");
        handler.ShouldNeverHaveBeenCalled(r => r.Method == "DELETE");

        fortitude.VerifyNoUnmatchedRequests();
    }

    [Fact]
    public async Task LocalDevelopmentGuideStatefulExample()
    {
        // docs/local-development.md: "Make it Stateful"
        var fortitude = FortitudeClient.Create(output);

        var users = new List<User> { new(1, "Alice") };

        fortitude.Accepts().Get().HttpRoute("/users").Returns((req, res) => res.Ok(users));

        fortitude.Accepts().Post().HttpRoute("/users").Returns((req, res) =>
        {
            var posted = req.Body.ToJson<User>()!;
            var created = posted with { Id = users.Max(u => u.Id) + 1 };

            users.Add(created);
            res.Created(created, location: $"/users/{created.Id}");
        });

        var post = new HttpRequestMessage(HttpMethod.Post, "http://x/users")
        {
            Content = JsonContent.Create(new User(0, "Bob"))
        };

        var created = await fortitude.TryHandle(post, CancellationToken.None);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/users/2", created.Headers.Location?.ToString());

        var listed = await (await fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/users"), CancellationToken.None))
            .Content.ReadAsStringAsync();

        Assert.Contains("Alice", listed);
        Assert.Contains("Bob", listed);
    }

    [Fact]
    public async Task LocalDevelopmentGuideLatencyExample()
    {
        // docs/local-development.md: "Simulate Latency"
        var fortitude = FortitudeClient.Create(output);

        fortitude.Accepts().Get().HttpRoute("/users")
            .WithDelay(TimeSpan.FromMilliseconds(300))
            .Returns((req, res) => res.Ok(Array.Empty<User>()));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await fortitude.TryHandle(
            new HttpRequestMessage(HttpMethod.Get, "http://x/users"), CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 250);
    }

    [Fact]
    public async Task YamlGuideTemplatingExample()
    {
        // docs/yaml-handlers.md: "Response templating"
        var handler = FortitudeYamlLoader.FromYamlMulti("""
                                                        handlers:
                                                          - match:
                                                              methods: [POST]
                                                              route: /users/{id}
                                                            response:
                                                              status: 200
                                                              body:
                                                                json:
                                                                  userId: "{{route.id}}"
                                                                  email: "{{body.email}}"
                                                        """).Single();

        var response = await handler.HandleRequestAsync(new FortitudeRequest
        {
            Method = "POST",
            BaseUrl = "http://localhost",
            Route = "/users/42",
            Url = "http://localhost/users/42",
            Protocol = "HTTP/1.1",
            Body = """{"email":"alice@example.com"}"""u8.ToArray()
        });

        var body = System.Text.Encoding.UTF8.GetString(response.Body!);

        Assert.Contains("\"userId\":42", body);
        Assert.Contains("\"email\":\"alice@example.com\"", body);
    }
}
