using System.Net;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Stateful scenarios: modelling a flow where the same route must answer differently
///     depending on what has already happened.
/// </summary>
public sealed class ScenarioTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = CreateTempDirectory();

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortitude-scenarios", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // ---------------------------------------------------------------- state machine

    [Fact]
    public void AScenarioStartsInTheInitialState()
    {
        var scenarios = new FortitudeScenarios();

        Assert.Equal(FortitudeScenarios.InitialState, scenarios.GetState("checkout"));
    }

    [Fact]
    public void StateCanBeSetAndRead()
    {
        var scenarios = new FortitudeScenarios();

        scenarios.SetState("checkout", "ItemAdded");

        Assert.Equal("ItemAdded", scenarios.GetState("checkout"));
    }

    [Fact]
    public void ScenariosAreIndependent()
    {
        var scenarios = new FortitudeScenarios();

        scenarios.SetState("a", "X");

        Assert.Equal("X", scenarios.GetState("a"));
        Assert.Equal(FortitudeScenarios.InitialState, scenarios.GetState("b"));
    }

    [Fact]
    public void ResetReturnsAScenarioToTheStart()
    {
        var scenarios = new FortitudeScenarios();
        scenarios.SetState("a", "X");
        scenarios.SetState("b", "Y");

        scenarios.Reset("a");

        Assert.Equal(FortitudeScenarios.InitialState, scenarios.GetState("a"));
        Assert.Equal("Y", scenarios.GetState("b"));

        scenarios.ResetAll();
        Assert.Equal(FortitudeScenarios.InitialState, scenarios.GetState("b"));
    }

    // ---------------------------------------------------------------- matching

    [Fact]
    public async Task AHandlerOnlyMatchesInItsRequiredState()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x")
            .InScenario("flow").WhenState("Ready")
            .Returns((_, res) => res.Ok("ready"));

        var before = await client.TryHandle(Get("/x"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotImplemented, before.StatusCode);

        client.Scenarios.SetState("flow", "Ready");

        var after = await client.TryHandle(Get("/x"), CancellationToken.None);
        Assert.Equal("ready", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AHandlerAdvancesTheStateAfterAnswering()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Post().HttpRoute("/cart")
            .InScenario("checkout").WhenState(FortitudeScenarios.InitialState).ThenState("ItemAdded")
            .Returns((_, res) => res.Ok("added"));

        await client.TryHandle(new HttpRequestMessage(HttpMethod.Post, "http://x/cart"), CancellationToken.None);

        Assert.Equal("ItemAdded", client.Scenarios.GetState("checkout"));
    }

    [Fact]
    public async Task AFlowWalksThroughItsStates()
    {
        // The canonical case: one route, three answers, driven by what came before.
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState(FortitudeScenarios.InitialState).ThenState("Running")
            .Returns((_, res) => res.Ok("queued"));

        client.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState("Running").ThenState("Done")
            .Returns((_, res) => res.Ok("running"));

        client.Accepts().Get().HttpRoute("/jobs/1")
            .InScenario("job").WhenState("Done")
            .Returns((_, res) => res.Ok("complete"));

        var answers = new List<string>();
        for (var i = 0; i < 4; i++)
            answers.Add(await (await client.TryHandle(Get("/jobs/1"), CancellationToken.None))
                .Content.ReadAsStringAsync());

        Assert.Equal(["queued", "running", "complete", "complete"], answers);
    }

    [Fact]
    public async Task AHandlerWithNoRequiredState_MatchesInAnyState()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/ping")
            .InScenario("flow").ThenState("Pinged")
            .Returns((_, res) => res.Ok("pong"));

        client.Scenarios.SetState("flow", "SomethingElse");

        var response = await client.TryHandle(Get("/ping"), CancellationToken.None);

        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        Assert.Equal("Pinged", client.Scenarios.GetState("flow"));
    }

    [Fact]
    public void AScenarioThatDeclaresNoStates_IsRejected()
    {
        // Silently doing nothing would be worse than refusing: the author clearly meant
        // something by naming a scenario.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FortitudeHandler.Accepts().Get().InScenario("flow").Returns((_, res) => res.Ok()));

        Assert.Contains("neither WhenState nor ThenState", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AnEmptyScenarioOrStateName_IsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => FortitudeHandler.Accepts().InScenario(value));
        Assert.Throws<ArgumentException>(() => FortitudeHandler.Accepts().WhenState(value));
        Assert.Throws<ArgumentException>(() => FortitudeHandler.Accepts().ThenState(value));
    }

    [Fact]
    public async Task ResettingAScenario_RewindsTheFlow()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Get().HttpRoute("/x")
            .InScenario("flow").WhenState(FortitudeScenarios.InitialState).ThenState("Done")
            .Returns((_, res) => res.Ok("first"));

        await client.TryHandle(Get("/x"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await client.TryHandle(Get("/x"), CancellationToken.None)).StatusCode);

        client.Scenarios.Reset("flow");

        Assert.Equal("first", await (await client.TryHandle(Get("/x"), CancellationToken.None))
            .Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScenariosCombineWithOtherMatchers()
    {
        var client = FortitudeClient.Create(output);

        client.Accepts().Post().HttpRoute("/orders")
            .InScenario("order").WhenState(FortitudeScenarios.InitialState).ThenState("Placed")
            .Returns((_, res) => res.Created(new { id = 1 }));

        // Wrong method, so the scenario must not advance.
        await client.TryHandle(Get("/orders"), CancellationToken.None);

        Assert.Equal(FortitudeScenarios.InitialState, client.Scenarios.GetState("order"));
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task AFlowWorksOverRealHttp()
    {
        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.Accepts().Get().HttpRoute("/status")
            .InScenario("deploy").WhenState(FortitudeScenarios.InitialState).ThenState("InProgress")
            .Returns((_, res) => res.Ok("pending"));

        client.Accepts().Get().HttpRoute("/status")
            .InScenario("deploy").WhenState("InProgress").ThenState("Finished")
            .Returns((_, res) => res.Ok("in-progress"));

        client.Accepts().Get().HttpRoute("/status")
            .InScenario("deploy").WhenState("Finished")
            .Returns((_, res) => res.Ok("done"));

        using var http = LiveServer.HttpClientFor(port);

        Assert.Equal("pending", await http.GetStringAsync("/status"));
        Assert.Equal("in-progress", await http.GetStringAsync("/status"));
        Assert.Equal("done", await http.GetStringAsync("/status"));
    }

    // ---------------------------------------------------------------- yaml

    [Fact]
    public async Task AYamlFlowWalksThroughItsStates()
    {
        File.WriteAllText(Path.Combine(_dir, "job.yaml"), """
                                                          handlers:
                                                            - match:
                                                                methods: [GET]
                                                                route: /job
                                                                scenario:
                                                                  name: job
                                                                  whenState: Started
                                                                  thenState: Running
                                                              response:
                                                                status: 200
                                                                body:
                                                                  text: queued
                                                            - match:
                                                                methods: [GET]
                                                                route: /job
                                                                scenario:
                                                                  name: job
                                                                  whenState: Running
                                                                  thenState: Done
                                                              response:
                                                                status: 200
                                                                body:
                                                                  text: running
                                                            - match:
                                                                methods: [GET]
                                                                route: /job
                                                                scenario:
                                                                  name: job
                                                                  whenState: Done
                                                              response:
                                                                status: 200
                                                                body:
                                                                  text: complete
                                                          """);

        await using var server = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"));

        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        // Later definitions win at equal priority, so the Done handler is tried first and
        // only the one whose state matches answers.
        Assert.Equal("queued", await http.GetStringAsync("/job"));
        Assert.Equal("running", await http.GetStringAsync("/job"));
        Assert.Equal("complete", await http.GetStringAsync("/job"));
    }

    [Fact]
    public async Task AYamlScenarioSpansSeveralFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "a-first.yaml"), """
                                                              handlers:
                                                                - match:
                                                                    route: /step
                                                                    scenario:
                                                                      name: shared
                                                                      whenState: Started
                                                                      thenState: Second
                                                                  response:
                                                                    status: 200
                                                                    body:
                                                                      text: one
                                                              """);

        File.WriteAllText(Path.Combine(_dir, "b-second.yaml"), """
                                                               handlers:
                                                                 - match:
                                                                     route: /step
                                                                     scenario:
                                                                       name: shared
                                                                       whenState: Second
                                                                   response:
                                                                     status: 200
                                                                     body:
                                                                       text: two
                                                               """);

        await using var server = await LiveServer.StartAsync(
            portCount: 1, ("Settings:ConfigDirectory", _dir), ("Settings:HotReload", "false"));

        using var http = LiveServer.HttpClientFor(server.Ports[0]);

        Assert.Equal("one", await http.GetStringAsync("/step"));
        Assert.Equal("two", await http.GetStringAsync("/step"));
    }

    private static HttpRequestMessage Get(string route) =>
        new(HttpMethod.Get, "http://x" + route);
}
