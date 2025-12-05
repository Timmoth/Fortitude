using Fortitude.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<PendingRequestStore>();
var app = builder.Build();

app.UseMiddleware<FortitudeMiddleware>();
app.UseHttpsRedirection();
app.MapHub<FortitudeHub>("/fortitude");
app.MapGet("/fortitude/health", () => "Fortitude server running");

app.Run();