using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Fortitude.Client;
using Xunit.Abstractions;

namespace Fortitude.Example.Api;

public class AspireTests(ITestOutputHelper output, AspireTestFixture fixture) : IClassFixture<AspireTestFixture>
{
    [Fact]
    public async Task CreateUser_ForwardsRequestToExternalApi_AndReturnsCreatedResult()
    {
        // Given: Fortitude fake server simulating the external API
        var (fortitude, mockServiceUrl) = await FortitudeServer.ConnectAsync(fixture._app.GetEndpoint("fortitude-server").AbsoluteUri, output);

        var expectedName = "Alice";
        var expectedEmail = "alice@example.com";

        // Fake external handler: POST /users
        var handler = fortitude.Accepts()
            .Post()
            .HttpRoute("/users")
            .Body(body => body.ToJson<User>()?.Email == expectedEmail)
            .Returns((request, response) =>
            {
                var reqObj = request.Body.ToJson<User>()!;
                response.Created(new User(999, reqObj.Name, reqObj.Email));
            });
        
        // And: The SUT (your API) is running with ExternalApi.BaseUrl overridden
        var client = await fixture.CreateHttpClient("example-api");
        
        // WHEN: Client calls into your SUT API
        var response = await client.PostAsJsonAsync("/users", new User(0, expectedName, expectedEmail));

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<User>();
        
        // Then: Response should be what Fortitude returned
        Assert.NotNull(created);
        Assert.Equal(999, created!.Id); // ID assigned by external service
        Assert.Equal(expectedName, created.Name);
        Assert.Equal(expectedEmail, created.Email);
        
        // Assert only a single request was made to the handler
        Assert.Single(handler.ReceivedRequests);

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

        // Given: Fortitude fake server simulating the external API
        var (fortitude, mockServiceUrl) = await FortitudeServer.ConnectAsync(fixture._app.GetEndpoint("fortitude-server").AbsoluteUri, output);
    
        var getHandler = fortitude.Accepts()
            .Get()
            .HttpRoute("/users")
            .Returns((request, response) =>
            {
                response.Ok(expectedUsers);
            });
        
        // SUT client configured to use Fortitude as external API
        var client = await fixture.CreateHttpClient("example-api");

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
    
    [Fact]
    public async Task GetCreateUser_ForwardsRequestToExternalApi_AndReturnsCreatedResult()
    {
        // Given: Fortitude fake server simulating the external API
        var (fortitude, mockServiceUrl) = await FortitudeServer.ConnectAsync(fixture._app.GetEndpoint("fortitude-server").AbsoluteUri, output);

        var users = new List<User>
        {
            new User(998, "Alice", "alice@example.com")
        };

        // Fake external API: POST /users
        fortitude.Accepts()
            .Post()
            .HttpRoute("/users")
            .Returns((req, res) =>
            {
                var incoming = req.Body.ToJson<User>()!;
                var created = new User(999, incoming.Name, incoming.Email);
                users.Add(created);
                res.Created(created);
            });

        // Fake external API: GET /users
        fortitude.Accepts()
            .Get()
            .HttpRoute("/users")
            .Returns((_, res) => res.Ok(users));

        // Fake external API: GET /users/{id}
        fortitude.Accepts()
            .Get()
            .HttpRoute("/users/{id}")
            .Returns((req, res) =>
            {
                var id = req.Route.GetRouteParameter("/users/{id}", "id")?.ToString();
                var user = users.FirstOrDefault(u => u.Id.ToString() == id);

                if (user is null)
                    res.NotFound();
                else
                    res.Ok(user);
            });

        // And: The SUT (your API) is running with ExternalApi.BaseUrl overridden
        var client = await fixture.CreateHttpClient("example-api");
        
        // Assert: Missing user returns 404
        var missingUserResponse = await client.GetAsync("/users/999");
        Assert.Equal(HttpStatusCode.NotFound, missingUserResponse.StatusCode);

        // Act: Create new user through SUT (POST /users)
        var expectedName = "Bob";
        var expectedEmail = "bob@example.com";

        var postResponse = await client.PostAsJsonAsync("/users", new User(0, expectedName, expectedEmail));
        postResponse.EnsureSuccessStatusCode();

        var createdUser = await postResponse.Content.ReadFromJsonAsync<User>();

        // Assert: SUT relayed creation correctly
        Assert.NotNull(createdUser);
        Assert.Equal(999, createdUser!.Id);
        Assert.Equal(expectedName, createdUser.Name);
        Assert.Equal(expectedEmail, createdUser.Email);

        // Act: GET /users from SUT
        var allUsersResponse = await client.GetAsync("/users");
        allUsersResponse.EnsureSuccessStatusCode();

        var returnedUsers = await allUsersResponse.Content.ReadFromJsonAsync<IEnumerable<User>>();

        // Assert: SUT returned list of users from fake external API
        Assert.NotNull(returnedUsers);
        Assert.Equal(2, returnedUsers!.Count());
        Assert.Contains(returnedUsers, u => u.Name == "Alice" && u.Email == "alice@example.com");
        Assert.Contains(returnedUsers, u => u.Name == "Bob" && u.Email == "bob@example.com");

        // Act/Assert: GET /users/{id} for created user
        var getUserResponse = await client.GetAsync("/users/999");
        getUserResponse.EnsureSuccessStatusCode();

        var foundUser = await getUserResponse.Content.ReadFromJsonAsync<User>();

        Assert.NotNull(foundUser);
        Assert.Equal(999, foundUser!.Id);
        Assert.Equal(expectedName, foundUser.Name);
        Assert.Equal(expectedEmail, foundUser.Email);

        // Cleanup
        await fortitude.StopAsync();
    }


}