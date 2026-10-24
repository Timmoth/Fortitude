using System.Net;
using Fortitude.Client;
using Fortitude.Server.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Options;

namespace Fortitude.Server;

public class Program
{
    /// <summary>Entry point: builds the server and runs it until shutdown.</summary>
    public static void Main(string[] args)
    {
        BuildApp(args).Run();
    }

    /// <summary>
    ///     Builds the Fortitude server without starting it.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="ports">
    ///     Ports to bind, overriding the PORT/PORTS environment variables. Pass 0 for an
    ///     ephemeral port. Intended for tests that need the real Kestrel pipeline on real
    ///     sockets rather than an in-memory TestServer.
    /// </param>
    public static WebApplication BuildApp(string[] args, IReadOnlyCollection<int>? ports = null)
    {
        var builder = WebApplication.CreateBuilder(args);

// Helper: parse ports string like "8080", "8080,8082", "8080-8085"
        static IEnumerable<int> ParsePorts(string? portsEnv)
        {
            if (string.IsNullOrWhiteSpace(portsEnv)) yield break;

            foreach (var token in portsEnv.Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (token.Contains('-'))
                {
                    var parts = token.Split('-', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var start)
                        && int.TryParse(parts[1], out var end)
                        && start > 0 && end >= start && end <= 65535)
                        for (var p = start; p <= end; p++)
                            yield return p;
                }
                else if (int.TryParse(token, out var single) && single is > 0 and <= 65535)
                {
                    yield return single;
                }
        }

        var settingsForStartup = new Settings();
        builder.Configuration.GetSection("Settings").Bind(settingsForStartup);

        var portsEnv = Environment.GetEnvironmentVariable("PORTS");
        var singlePortEnv = Environment.GetEnvironmentVariable("PORT"); // fallback single port
        builder.Services.Configure<Settings>(
            builder.Configuration.GetSection("Settings"));


        List<int> requestedPorts;
        if (ports != null)
            requestedPorts = ports.ToList();
        else if (!string.IsNullOrWhiteSpace(portsEnv))
            requestedPorts = ParsePorts(portsEnv).Distinct().OrderBy(p => p).ToList();
        else if (!string.IsNullOrWhiteSpace(singlePortEnv) && int.TryParse(singlePortEnv, out var single) &&
                 single is > 0 and <= 65535)
            requestedPorts = [single];
        else
            requestedPorts = [];
        
// If ports were requested, configure Kestrel to listen on each of them
        if (requestedPorts.Count > 0)
            builder.WebHost.ConfigureKestrel(options =>
            {
                var bound = new List<int>();

                foreach (var port in requestedPorts)
                    try
                    {
                        // Attempt to bind; if the port is already in use the call will throw
                        options.Listen(IPAddress.Any, port);
                        bound.Add(port);
                    }
                    catch (Exception ex)
                    {
                        // Log and continue. Don't crash immediately so we can try other ports.
                        Console.WriteLine($"[Fortitude] Warning: could not bind to port {port}: {ex.Message}");
                    }

                if (bound.Count == 0)
                    throw new InvalidOperationException(
                        $"Fortitude: Could not bind to any requested port(s): {string.Join(',', requestedPorts)}");

                // Store the list of successful ports in configuration for later logging/usage
                builder.Configuration["Fortitude:SelectedPorts"] = string.Join(',', bound);
                Console.WriteLine($"[Fortitude] Bound to ports: {string.Join(',', bound)}");
            });

        builder.Services.AddSingleton<YamlHandlerStore>();
        builder.Services.AddSingleton<PendingRequestStore>();
        builder.Services.AddSingleton<PortReservationService>();

        StaticWebAssetsLoader.UseStaticWebAssets(
            builder.Environment,
            builder.Configuration
        );

        // SignalR caps received messages at 32 KB by default, which silently turns any
        // larger request or response body into a gateway timeout. A mock server has to be
        // able to carry realistic payloads.
        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = settingsForStartup.MaxMessageSizeBytes;
        });
        builder.Services.AddSingleton<RequestTracker>();
        builder.Services.AddSingleton<ConnectedClientService>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();


        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            var portReservationService = app.Services.GetRequiredService<PortReservationService>();
            var server = app.Services.GetRequiredService<IServer>();

            portReservationService.Initialize(server);

            // Use selected ports if present, otherwise fall back to app.Urls (ASPNETCORE_URLS)
            var selected = builder.Configuration["Fortitude:SelectedPorts"];
            if (!string.IsNullOrEmpty(selected))
            {
                var ports = selected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var p in ports)
                    app.Logger.LogInformation("Fortitude Server Live UI is now running on: {Url}",
                        $"http://localhost:{p}/fortitude");
            }
            else
            {
                // app.Urls may contain one or multiple URLs configured via ASPNETCORE_URLS
                foreach (var rawUrl in app.Urls)
                {
                    var displayUrl = rawUrl.Replace("0.0.0.0", "localhost");
                    app.Logger.LogInformation("Fortitude Server Live UI is now running on: {Url}",
                        $"{displayUrl.TrimEnd('/')}/fortitude");
                }
            }
        });


        app.UseMiddleware<FortitudeMiddleware>();

        app.MapHub<FortitudeHub>("/fortitude/hub");
        app.MapGet("/fortitude/health", () => "server running");

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", true);
            app.UseHsts();
        }

        // The admin UI is unauthenticated and can write handler files, so it is switchable.
        var settings = app.Services.GetRequiredService<IOptions<Settings>>().Value;

        if (settings.EnableAdminUi)
        {
            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

            app.UseStaticFiles("/fortitude");

            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapBlazorHub("/fortitude/_blazor");
        }
        else
        {
            app.Logger.LogInformation(
                "Admin UI disabled via Settings:EnableAdminUi; /fortitude serves the hub and health endpoint only.");
        }

        return app;
    }
}

/// <summary>
///     Server configuration, bound from the "Settings" configuration section.
/// </summary>
public class Settings
{
    /// <summary>
    ///     Forces every connected client to receive every incoming request, bypassing per-port
    ///     routing entirely.
    /// </summary>
    /// <remarks>
    ///     Leave this off. By default each connecting client reserves one of the server's bound
    ///     ports and only sees requests that arrive on it, which is what allows concurrent tests
    ///     to share one server without interfering. Clients still fall back to shared delivery
    ///     automatically when no port can be reserved, so this switch is only needed to force
    ///     that behaviour when ports *are* available.
    /// </remarks>
    public bool Broadcast { get; set; }

    /// <summary>
    ///     How long the server waits for a connected client to produce a response before giving
    ///     up and returning 504 Gateway Timeout. Defaults to 5 seconds.
    /// </summary>
    /// <remarks>
    ///     Raise this when a handler does real work, deliberately delays, or when you want to
    ///     step through a responder in a debugger without the request expiring underneath you.
    ///     Values of zero or less are ignored in favour of the default.
    /// </remarks>
    public int ResponseTimeoutSeconds { get; set; } = DefaultResponseTimeoutSeconds;

    /// <summary>The response timeout applied when none is configured.</summary>
    public const int DefaultResponseTimeoutSeconds = 5;

    /// <summary>
    ///     Directory to read YAML handler definitions from, searched recursively for
    ///     <c>.yaml</c> and <c>.yml</c> files. Relative paths resolve against the application
    ///     directory. Defaults to <c>config</c> beside the executable, which is the path the
    ///     Docker image expects a volume to be mounted at.
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    ///     Watch <see cref="ConfigDirectory" /> and reload handler definitions when they change.
    ///     On by default.
    /// </summary>
    public bool HotReload { get; set; } = true;

    /// <summary>
    ///     Largest request or response body, in bytes, that can be relayed between the server
    ///     and a connected client. Defaults to 32 MB.
    /// </summary>
    /// <remarks>
    ///     Bodies travel over SignalR, which caps received messages at 32 KB unless told
    ///     otherwise — small enough that an ordinary JSON fixture would fail as a timeout.
    /// </remarks>
    public long MaxMessageSizeBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    ///     Serve the live monitoring and YAML editing UI at <c>/fortitude</c>. On by default.
    /// </summary>
    /// <remarks>
    ///     The UI is unauthenticated and can write handler files, so turn it off when the
    ///     server is reachable by anything you do not trust.
    /// </remarks>
    public bool EnableAdminUi { get; set; } = true;

    /// <summary>
    ///     <see cref="ResponseTimeoutSeconds" /> as a <see cref="TimeSpan" />, falling back to
    ///     the default if it was configured to a non-positive value.
    /// </summary>
    public TimeSpan ResponseTimeout => ResponseTimeoutSeconds > 0
        ? TimeSpan.FromSeconds(ResponseTimeoutSeconds)
        : TimeSpan.FromSeconds(DefaultResponseTimeoutSeconds);
}