using System.Net.Http.Json;
using System.Text.Json;
using Fortitude.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Xunit.Abstractions;

namespace Fortitude.Example.Api;

public class UsersApiTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string FortitudeBase = "http://localhost:5093";

    private readonly JsonSerializerOptions _defaultOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    [Fact]
    public async Task CreateUser_ForwardsRequestToExternalApi_AndReturnsCreatedResult()
    {
        // Given: Fortitude fake server simulating the external API
        var fortitude = new FortitudeClient(output);
        var expectedName = "Alice";
        var expectedEmail = "alice@example.com";

        // Fake external handler: POST /users
        var handler = fortitude.For()
            .Post()
            .HttpRoute("/users")
            .Body(body =>
            {
                var req = JsonSerializer.Deserialize<User>(body, _defaultOptions);
                return req != null && req.Email == expectedEmail;
            })
            .Build(request =>
            {
                var reqObj = JsonSerializer.Deserialize<User>(request.Body, _defaultOptions);
                var response = new User(999, reqObj.Name, reqObj.Email);

                return new FortitudeResponse(request.RequestId)
                {
                    Body = JsonSerializer.Serialize(response),
                    Status = 201
                };
            });

        await fortitude.StartAsync($"{FortitudeBase}/fortitude");

        // And: The SUT (your minimal API) is running with ExternalApi.BaseUrl overridden
        var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ExternalApi:BaseUrl"] = $"{FortitudeBase}/"
                    });
                });
            })
            .CreateClient();

        // WHEN: Client calls into your SUT API
        var newUser = new User(0, expectedName, expectedEmail);
        var response = await client.PostAsJsonAsync("/users", newUser);

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<User>();
        
        // Then: Response should be what Fortitude returned
        Assert.NotNull(created);
        Assert.Equal(999, created!.Id); // ID assigned by external service
        Assert.Equal(expectedName, created.Name);
        Assert.Equal(expectedEmail, created.Email);
        Assert.Single(handler.ReceivedRequests); // Assert only a single request was made to the handler

        // Cleanup
        await fortitude.StopAsync();
    }

    [Fact]
    public async Task GetUsers_ForwardsRequestToExternalApi_AndReturnsUsers()
    {
        // Given
        var expectedUsers = new[]
        {
            new User(1, "Alice", "alice@example.com"),
            new User(2, "Bob", "bob@example.com")
        };

        // Start Fortitude fake server
        var fortitude = new FortitudeClient(output);
    
        var getHandler = fortitude.For()
            .Get()
            .HttpRoute("/users")
            .Build(request => new FortitudeResponse(request.RequestId)
            {
                Body = JsonSerializer.Serialize(expectedUsers),
                Status = 200
            });

        await fortitude.StartAsync($"{FortitudeBase}/fortitude");

        // SUT client configured to use Fortitude as external API
        var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ExternalApi:BaseUrl"] = $"{FortitudeBase}/"
                    });
                });
            })
            .CreateClient();

        // When: Client calls GET /users
        var response = await client.GetAsync("/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<IEnumerable<User>>();
    
        // Then: Response matches what Fortitude returned
        Assert.NotNull(users);
        Assert.Equal(2, users!.Count());
        Assert.Contains(users, u => u is { Name: "Alice", Email: "alice@example.com" });
        Assert.Contains(users, u => u is { Name: "Bob", Email: "bob@example.com" });

        await fortitude.StopAsync();
    }

}
