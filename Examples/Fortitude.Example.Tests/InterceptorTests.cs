using System.Net.Http.Json;
using Fortitude.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace Fortitude.Example.Api;

public class InterceptorTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateUser_HandlesRequestInternally_AndReturnsCreatedResult()
    {
        // GIVEN
        var expectedName = "Alice";
        var expectedEmail = "alice@example.com";
        // Create Fortitude Client
        var fortitude = FortitudeClient.Create(output);
        
        // Configure Fortitude Handler
        var handler = fortitude.Accepts()
            .Post()
            .HttpRoute("/users")
            .Body(body => body.ToJson<User>()?.Email == expectedEmail)
            .Returns((request, response) =>
            {
                var reqObj = request.Body.ToJson<User>()!;
                response.Created(new User(999, reqObj.Name, reqObj.Email));
            });
        
        // Create SUT
        var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(s =>
                {
                    // Register Fortitude Client
                    s.AddFortitudeClient(fortitude);
                });
            })
            .CreateClient();
        
        // WHEN: Client calls into your SUT API
        var response = await client.PostAsJsonAsync("/users", new User(0, expectedName, expectedEmail));

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<User>();
        
        // Then: Response should be what Fortitude returned
        Assert.NotNull(created);
        Assert.Equal(999, created!.Id); // ID assigned by external service
    }
}