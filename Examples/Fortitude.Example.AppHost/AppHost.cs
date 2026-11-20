var builder = DistributedApplication.CreateBuilder(args);

// Aspire 17 replaced the generated AddProject<TProject> with a path-based call from
// Aspire.Hosting.Dotnet. Paths are relative to this project.
var fortitudeServer = builder.AddDotnetProject("fortitude-server", "../../Fortitude.Server/Fortitude.Server.csproj")
    .WithEnvironment("Settings:Broadcast", "true");

var exampleApi = builder.AddDotnetProject("example-api", "../Fortitude.Example.Api/Fortitude.Example.Api.csproj")
    .WithReference(fortitudeServer)
    .WithEnvironment("ExternalApi:BaseUrl", fortitudeServer.GetEndpoint("http"))
    .WaitFor(fortitudeServer);

builder.Build().Run();
