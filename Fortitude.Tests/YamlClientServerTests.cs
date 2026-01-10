using System.Net;
using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

public class YamlClientServerTests(ITestOutputHelper testOutputHelper)
{
    private async Task<(HttpClient httpClient, FortitudeClient fortitudeClient, HubConnection hubConnection)> Setup(WebApplicationFactory<Program> factory)
    {
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

        return (client, fortitudeClient, hubConnection);
    }
    
    [Fact]
    public async Task FortitudeServer_MatchesRequest_AndReturnsExpectedResponse()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /test
  methods: [GET]
response:
  status: 202
                "));
        
        // Act
        var res = await client.GetAsync("/test");
        
        // Assert
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_MatchesRouteTemplate_AndReturnsExpectedResponse()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /users/{id}/address
  methods: [GET]
response:
  status: 202
                "));
        
        // Act
        var res = await client.GetAsync("/users/12/address");
        
        // Assert
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_DoesNotMatch_WhenHttpMethodIsDifferent()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /test
  methods: [POST]
response:
  status: 200
                "));

        // Act
        var response = await client.GetAsync("/test");

        // Assert
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_DoesNotMatch_WhenRouteIsDifferent()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /expected
  methods: [GET]
response:
  status: 200
                "));

        // Act
        var response = await client.GetAsync("/unexpected");

        // Assert
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_Matches_WhenRequiredHeaderIsPresent()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /secured
  methods: [GET]
  headers:
    X-Test: 123
response:
  status: 200
                "));

        var request = new HttpRequestMessage(HttpMethod.Get, "/secured");
        request.Headers.Add("X-Test", "123");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_DoesNotMatch_WhenHeaderValueIsDifferent()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /secured
  methods: [GET]
  headers:
    X-Test: expected
response:
  status: 200
                "));

        var request = new HttpRequestMessage(HttpMethod.Get, "/secured");
        request.Headers.Add("X-Test", "actual");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_Matches_WhenQueryParameterMatches()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /search
  methods: [GET]
  query:
    q: test
response:
  status: 200
                "));
        
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/search")
            .QueryParam("q", "test")
            .Returns((req, res) =>
            {
                res.Ok();
            });

        // Act
        var response = await client.GetAsync("/search?q=test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_Matches_WhenBodyContainsText()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /body
  methods: [POST]
  body:
    contains: hello
response:
  status: 200
                "));
        
        var content = new StringContent("hello world", Encoding.UTF8, "text/plain");

        // Act
        var response = await client.PostAsync("/body", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    private record TestPayload(string Name, int Age);

    [Fact]
    public async Task FortitudeServer_Matches_WhenJsonBodyPredicateMatches()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /json
  methods: [POST]
  body:
    json: Age >= 18
response:
  status: 202
                "));

        var json = JsonSerializer.Serialize(new TestPayload("Alice", 25));
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/json", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task FortitudeServer_OkText_SetsContentTypeAndBody()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /text
  methods: [GET]
response:
  status: 200
  body:
    text: hello world
                "));

        var response = await client.GetAsync("/text");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType!.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello world", body);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_OkJson_SerializesAndSetsContentType()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /json
  methods: [GET]
response:
  status: 200
  body:
    json: 
        name: Alice
        age: 30
                "));
        

        var response = await client.GetAsync("/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"Alice\"", json);
        Assert.Contains("\"age\":30", json);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_NoContent_HasNoBody()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /nocontent
  methods: [GET]
response:
  status: 204
                "));

        var response = await client.GetAsync("/nocontent");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", body);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_CustomHeaders_AreIncluded()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        
        fortitudeClient.AddHandler(FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  route: /headers
  methods: [GET]
response:
  status: 200
  headers: 
    X-Test: 123
    X-Another: abc
                "));

        var response = await client.GetAsync("/headers");

        Assert.True(response.Headers.Contains("X-Test"));
        Assert.Equal("123", response.Headers.GetValues("X-Test").Single());
        Assert.Equal("abc", response.Headers.GetValues("X-Another").Single());

        await hubConnection.DisposeAsync();
    }
}
