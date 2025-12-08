using System.Net;
using Fortitude.Server;
using Fortitude.Server.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

var builder = WebApplication.CreateBuilder(args);


// -----------------------------
// Determine port range
// -----------------------------

int portMin, portMax;

// Try reading from environment variables first
string? minEnv = Environment.GetEnvironmentVariable("PORT_MIN");
string? maxEnv = Environment.GetEnvironmentVariable("PORT_MAX");

if (int.TryParse(minEnv, out var envMin) &&
    int.TryParse(maxEnv, out var envMax) &&
    envMin > 0 && envMax > envMin && envMax <= 65535)
{
    portMin = envMin;
    portMax = envMax;
}
else
{
    portMin = 54000;
    portMax = portMin + 100;
}

builder.WebHost.ConfigureKestrel(options =>
{
    bool bound = false;

    for (int port = portMin; port <= portMax; port++)
    {
        try
        {
            options.Listen(IPAddress.Any, port);
            builder.Configuration["Fortitude:SelectedPort"] = port.ToString();
            bound = true;
        }
        catch
        {
            // Port is probably in use — skip silently
        }
    }

    if (!bound)
    {
        throw new InvalidOperationException(
            $"No free ports found in range {portMin}–{portMax}");
    }
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
    app.Logger.LogInformation("[Port Range] Using ports {portMin}–{portMax}", portMin, portMax);

    var rawUrl = app.Urls.FirstOrDefault() ?? "";
    var displayUrl = rawUrl.Replace("0.0.0.0", "localhost");

    app.Logger.LogInformation(
        "Fortitude Server Live UI is now running on: {Url}",
        $"{displayUrl.TrimEnd('/')}/fortitude"
    );
});

app.UseMiddleware<FortitudeMiddleware>();

app.MapHub<FortitudeHub>("/fortitude/hub");
app.MapGet("/fortitude/health", () => "server running");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseStaticFiles("/fortitude");

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapBlazorHub("/fortitude/_blazor");

app.Run();