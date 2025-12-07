using Fortitude.Server;
using Fortitude.Server.Components;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<PendingRequestStore>();

StaticWebAssetsLoader.UseStaticWebAssets(
    builder.Environment, 
    builder.Configuration
);

builder.Services.AddSignalR();
builder.Services.AddSingleton<RequestTracker>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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