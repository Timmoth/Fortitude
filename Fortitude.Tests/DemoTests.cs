using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class FortitudeClientTests
{
    private static string _fortitudeBaseUrl = "http://localhost:5185";
    private readonly ITestOutputHelper _output;

    public FortitudeClientTests(ITestOutputHelper output)
    {
        _output = output;
    }
    public class FakeUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
    [Fact]
    public async Task Test1()
    {
        // Given
        var fortitude = await FortitudeServer.ConnectAsync(_fortitudeBaseUrl, _output);

        var fakeUser = new FakeUser();
        var handler = fortitude.Accepts()
            .Get()
            .HttpRoute($"/users/{fakeUser.Id}")
            .Returns((req, res) =>
            {
                res.Ok(fakeUser);
            });
        
        using var http = new HttpClient();
        var response = await http.GetAsync($"{_fortitudeBaseUrl}/users/{fakeUser.Id}");

        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {body}");

        // Then
        await fortitude.StopAsync();
        var returnedUser = System.Text.Json.JsonSerializer.Deserialize<FakeUser>(body, JsonSerializerOptions.Web);
        Assert.Equal(fakeUser.Id, returnedUser?.Id);

    }

      public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public class CreateUserResponse
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Fact]
    public async Task CanCreateUserWithHeadersAndQueryParams()
    {
        // Given
        
        // Start Fortitude client
        var fortitude = await FortitudeServer.ConnectAsync(_fortitudeBaseUrl, _output);

        // Define handler for POST /users with header and query param checks
        var handler = fortitude.Accepts()
            .Post()
            .HttpRoute("/users")
            .Header("X-Auth-Token", "secret-token")
            .QueryParam("source", "unit-test")
            .Body(body =>
            {
                var req = JsonSerializer.Deserialize<CreateUserRequest>(body);
                return req != null && !string.IsNullOrWhiteSpace(req.Name) && req.Age > 0;
            })
            .Returns((req, res) =>
            {
                var reqObj = JsonSerializer.Deserialize<CreateUserRequest>(req.Body)!;
                res.Created(new CreateUserResponse
                {
                    Name = reqObj.Name,
                    Age = reqObj.Age
                });
            });
        
        // When
        // SUT would make this HTTP request internally,
        // For the sake of the demo we'll make it here
        using var http = new HttpClient();
        var userRequest = new CreateUserRequest { Name = "Alice", Age = 30 };
        var requestBody = new StringContent(JsonSerializer.Serialize(userRequest), Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_fortitudeBaseUrl}/users?source=unit-test")
        {
            Content = requestBody
        };
        httpRequest.Headers.Add("X-Auth-Token", "secret-token");

        var responseMessage = await http.SendAsync(httpRequest);
        var responseBody = await responseMessage.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {responseBody}");


        // Then
        var createdUser = JsonSerializer.Deserialize<CreateUserResponse>(responseBody, JsonSerializerOptions.Web);
        Assert.NotNull(createdUser);
        Assert.Equal(userRequest.Name, createdUser?.Name);
        Assert.Equal(userRequest.Age, createdUser?.Age);
        Assert.NotEqual(Guid.Empty, createdUser?.Id);

        // Stop Fortitude
        await fortitude.StopAsync();
    }
}