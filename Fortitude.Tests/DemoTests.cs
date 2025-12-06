using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class FortitudeClientTests
{
    private static string _fortitudeBaseUrl = "http://localhost:8080";
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
        var snide = new FortitudeClient(_output);
        await snide.StartAsync(url: $"{_fortitudeBaseUrl}/fortitude");

        var fakeUser = new FakeUser();
        var handler = snide.For()
            .Get()
            .HttpRoute($"/users/{fakeUser.Id}")
            .Build(r =>  new FortitudeResponse(r.RequestId)
            {
                Body = System.Text.Json.JsonSerializer.Serialize(fakeUser)
            });
        
        using var http = new HttpClient();
        var response = await http.GetAsync($"{_fortitudeBaseUrl}/users/{fakeUser.Id}");

        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {body}");

        // Then
        await snide.StopAsync();
        var returnedUser = System.Text.Json.JsonSerializer.Deserialize<FakeUser>(body);
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
        var fortitude = new FortitudeClient(_output);
        await fortitude.StartAsync(url: $"{_fortitudeBaseUrl}/fortitude");

        // Define handler for POST /users with header and query param checks
        var handler = fortitude.For()
            .Post()
            .HttpRoute("/users")
            .Header("X-Auth-Token", "secret-token")
            .QueryParam("source", "unit-test")
            .Body(body =>
            {
                var req = JsonSerializer.Deserialize<CreateUserRequest>(body);
                return req != null && !string.IsNullOrWhiteSpace(req.Name) && req.Age > 0;
            })
            .Build(request =>
            {
                var reqObj = JsonSerializer.Deserialize<CreateUserRequest>(request.Body)!;
                var response = new CreateUserResponse
                {
                    Name = reqObj.Name,
                    Age = reqObj.Age
                };

                return new FortitudeResponse(request.RequestId)
                {
                    Body = JsonSerializer.Serialize(response),
                    Status = 201
                };
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
        var createdUser = JsonSerializer.Deserialize<CreateUserResponse>(responseBody);
        Assert.NotNull(createdUser);
        Assert.Equal(userRequest.Name, createdUser?.Name);
        Assert.Equal(userRequest.Age, createdUser?.Age);
        Assert.NotEqual(Guid.Empty, createdUser?.Id);

        // Stop Fortitude
        await fortitude.StopAsync();
    }
}