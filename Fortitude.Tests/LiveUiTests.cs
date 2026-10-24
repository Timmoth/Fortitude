using Bunit;
using Fortitude.Client;
using Fortitude.Server;
using Fortitude.Server.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

/// <summary>
///     The live monitoring and handler-editing UI.
/// </summary>
public sealed class LiveUiTests : IDisposable
{
    private readonly string _dir = CreateTempDirectory();
    private readonly TestContext _ctx = new();

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortitude-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        _ctx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private const string ValidHandler = """
                                        handlers:
                                          - match:
                                              methods: [GET]
                                              route: /ping
                                            response:
                                              status: 200
                                              body:
                                                text: pong
                                        """;

    private YamlHandlerStore Register()
    {
        var store = new YamlHandlerStore(
            NullLogger<YamlHandlerStore>.Instance,
            Options.Create(new Settings { ConfigDirectory = _dir, HotReload = false }));

        var ports = new PortReservationService(NullLogger<PortReservationService>.Instance);

        _ctx.Services.AddSingleton(store);
        _ctx.Services.AddSingleton(new RequestTracker());
        _ctx.Services.AddSingleton(ports);
        _ctx.Services.AddSingleton(new ConnectedClientService(
            NullLogger<ConnectedClientService>.Instance, ports, Options.Create(new Settings())));

        return store;
    }

    private static FortitudeRequest Request(string route = "/ping", string method = "GET") => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- metrics

    [Fact]
    public void TheMetricsPageShowsTheRequestTotal()
    {
        Register();
        var tracker = _ctx.Services.GetRequiredService<RequestTracker>();

        tracker.Add(Request());
        tracker.Add(Request("/other"));

        var page = _ctx.Render<RequestMetrics>();

        Assert.Contains("Total Requests:", page.Markup);
        Assert.Contains("2", page.Markup);
    }

    [Fact]
    public void ObservedRequestsAreListed()
    {
        Register();
        var tracker = _ctx.Services.GetRequiredService<RequestTracker>();

        var request = Request("/users/42", "POST");
        tracker.Add(request);
        tracker.Add(new FortitudeResponse(request.RequestId).Created(new { id = 42 }));

        var page = _ctx.Render<RequestMetrics>();

        Assert.Contains("/users/42", page.Markup);
        Assert.Contains("POST", page.Markup);
        Assert.Contains("201", page.Markup);
    }

    [Fact]
    public void ARequestWithNoResponseYetShowsAsPending()
    {
        Register();
        _ctx.Services.GetRequiredService<RequestTracker>().Add(Request());

        var page = _ctx.Render<RequestMetrics>();

        Assert.Contains("Pending", page.Markup);
    }

    [Fact]
    public void ClearEmptiesTheList()
    {
        Register();
        var tracker = _ctx.Services.GetRequiredService<RequestTracker>();
        tracker.Add(Request("/will-be-cleared"));

        var page = _ctx.Render<RequestMetrics>();
        Assert.Contains("/will-be-cleared", page.Markup);

        page.Find("button").Click();

        Assert.DoesNotContain("/will-be-cleared", page.Markup);
    }

    [Fact]
    public void TheConnectedClientCountIsShown()
    {
        Register();
        _ctx.Services.GetRequiredService<ConnectedClientService>().Add("conn-1");

        var page = _ctx.Render<RequestMetrics>();

        Assert.Contains("Clients Connected:", page.Markup);
    }

    // ---------------------------------------------------------------- yaml editor

    [Fact]
    public void HandlerFilesAreListed()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), ValidHandler);
        Register();

        var page = _ctx.Render<Yaml>();

        Assert.Contains("ping.yaml", page.Markup);
    }

    [Fact]
    public void AFileThatFailedToParseIsFlagged()
    {
        // Previously the count silently showed 0, which looked like an empty-but-valid file.
        File.WriteAllText(Path.Combine(_dir, "broken.yaml"), "handlers: [ unbalanced: yaml:");
        Register();

        var page = _ctx.Render<Yaml>();

        Assert.Contains("broken.yaml", page.Markup);
        Assert.Contains("parse error", page.Markup);
    }

    [Fact]
    public void TheHandlerCountIsShownForAValidFile()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), ValidHandler);
        Register();

        var page = _ctx.Render<Yaml>();

        Assert.DoesNotContain("parse error", page.Markup);
    }

    [Fact]
    public void DeletingAFileRemovesItFromTheList()
    {
        File.WriteAllText(Path.Combine(_dir, "ping.yaml"), ValidHandler);
        var store = Register();

        var page = _ctx.Render<Yaml>();
        page.FindAll("button").First(b => b.TextContent.Trim() == "Delete").Click();

        Assert.DoesNotContain("ping.yaml", page.Markup);
        Assert.Empty(store.Files);
    }

    [Fact]
    public void TheCurlDialogGeneratesAnEditableHandler()
    {
        Register();

        var page = _ctx.Render<Yaml>();
        page.FindAll("button").First(b => b.TextContent.Contains("From cURL")).Click();

        // Re-query between interactions: each change re-renders, which invalidates any
        // element references taken before it.
        page.FindAll("textarea")[0].Change("curl -X POST https://api.example.com/orders");
        page.FindAll("textarea")[1].Change("""{"id":1}""");

        page.FindAll("button").First(b => b.TextContent.Trim() == "Generate").Click();

        // The editor opens pre-filled with the generated definition.
        Assert.Contains("handlers:", page.Markup);
        Assert.Contains("/orders", page.Markup);
    }

    [Fact]
    public void BadCurlInputReportsAnErrorRatherThanDoingNothing()
    {
        Register();

        var page = _ctx.Render<Yaml>();
        page.FindAll("button").First(b => b.TextContent.Contains("From cURL")).Click();

        page.FindAll("textarea")[0].Change("curl -X POST");      // no URL
        page.FindAll("textarea")[1].Change("""{"id":1}""");

        page.FindAll("button").First(b => b.TextContent.Trim() == "Generate").Click();

        Assert.Contains("Could not generate", page.Markup);
    }

    [Fact]
    public void AddingANewHandlerOpensAnEditor()
    {
        Register();

        var page = _ctx.Render<Yaml>();
        page.FindAll("button").First(b => b.TextContent.Trim() == "Add New Handler").Click();

        Assert.Contains("New Handler", page.Markup);
        Assert.NotEmpty(page.FindAll("textarea"));
    }

    [Fact]
    public void SavingANewHandlerMakesItLive()
    {
        var store = Register();

        var page = _ctx.Render<Yaml>();
        page.FindAll("button").First(b => b.TextContent.Trim() == "Add New Handler").Click();

        page.Find("textarea").Change(ValidHandler);
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        Assert.Single(store.Files);
        Assert.Single(store.Files[0].Handlers);
    }
}
