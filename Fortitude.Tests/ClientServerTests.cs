using System.Net;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

public class ClientServerTests(WebApplicationFactory<Program> factory, ITestOutputHelper testOutputHelper)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task FortitudeServer_MatchesRequest_AndReturnsExpectedResponse()
    {
        // Arrange: Spin up Fortitude server in-memory
        var client = factory.CreateClient();
        var server = factory.Server;
        
        // Set up HubConnection to the in-memory server
        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/fortitude/hub", options =>
            {
                // This ensures it uses the in-memory server instead of real HTTP
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        var fortitudeClient = FortitudeClient.Create(testOutputHelper);
        await fortitudeClient.StartAsync(hubConnection);

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/test")
            .Returns((req, res) =>
        {
            res.Accepted();
        });
        
        // Act
        var res = await client.GetAsync("/test");
        
        // Assert
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        await hubConnection.DisposeAsync();
    }
}
