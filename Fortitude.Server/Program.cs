using System.Net;
using Fortitude.Client;
using Fortitude.Server.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

namespace Fortitude.Server;

public class Program
{
    public static void Main(string[] args)
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

        var portsEnv = Environment.GetEnvironmentVariable("PORTS");
        var singlePortEnv = Environment.GetEnvironmentVariable("PORT"); // fallback single port
        builder.Services.Configure<Settings>(
            builder.Configuration.GetSection("Settings"));


        List<int> requestedPorts;
        if (!string.IsNullOrWhiteSpace(portsEnv))
            requestedPorts = ParsePorts(portsEnv).Distinct().OrderBy(p => p).ToList();
        else if (!string.IsNullOrWhiteSpace(singlePortEnv) && int.TryParse(singlePortEnv, out var single) &&
                 single is > 0 and <= 65535)
            requestedPorts = [single];
        else
            requestedPorts = [];
        
        var configDir = Path.Combine(AppContext.BaseDirectory, "config"); 
     
        var handlers = new List<FortitudeHandler>();
        foreach (var handler in FortitudeYamlLoader.LoadHandlers(configDir))
        {
            handlers.AddRange(handler);
        }
        
        builder.Services.AddSingleton<HandlerSet>(h => new HandlerSet(h.GetRequiredService<ILogger<HandlerSet>>(), handlers));

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

        builder.Services.AddSingleton<PendingRequestStore>();
        builder.Services.AddSingleton<PortReservationService>();

        StaticWebAssetsLoader.UseStaticWebAssets(
            builder.Environment,
            builder.Configuration
        );

        builder.Services.AddSignalR();
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

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        app.UseStaticFiles("/fortitude");

        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapBlazorHub("/fortitude/_blazor");

        app.Run();
    }
}

public class Settings
{
    public bool Broadcast { get; set; } = true; // default = false
}