using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var fortitudeServer = builder.AddProject<Fortitude_Server>("fortitude-server")
    .WithEnvironment("Settings:Broadcast", "true");

var exampleApi = builder.AddProject<Fortitude_Example_Api>("example-api")
    .WithReference(fortitudeServer)
    .WithEnvironment("ExternalApi:BaseUrl", fortitudeServer.GetEndpoint("http"))
    .WaitFor(fortitudeServer);

builder.Build().Run();