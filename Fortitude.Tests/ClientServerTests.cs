using System.Net;
using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

public class ClientServerTests(ITestOutputHelper testOutputHelper)
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
    
    [Fact]
    public async Task FortitudeServer_DoesNotMatch_WhenHttpMethodIsDifferent()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.Accepts()
            .Post()
            .HttpRoute("/test")
            .Returns((req, res) =>
            {
                res.Ok();
            });

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

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/expected")
            .Returns((req, res) =>
            {
                res.Ok();
            });

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

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/secured")
            .Header("X-Test", "123")
            .Returns((req, res) =>
            {
                res.Ok();
            });

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

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/secured")
            .Header("X-Test", "expected")
            .Returns((req, res) =>
            {
                res.Ok();
            });

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

        fortitudeClient.Accepts()
            .Post()
            .HttpRoute("/body")
            .BodyContains("hello")
            .Returns((req, res) =>
            {
                res.Ok();
            });

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

        fortitudeClient.Accepts()
            .Post()
            .HttpRoute("/json")
            .JsonBody<TestPayload>(p => p.Age >= 18)
            .Returns((req, res) =>
            {
                res.Accepted();
            });

        var json = JsonSerializer.Serialize(new TestPayload("Alice", 25));
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/json", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_UsesRequestPredicate_WhenProvided()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.Accepts()
            .Get()
            .Matches(req => req.Route == "/predicate" && req.Method == "GET")
            .Returns((req, res) =>
            {
                res.Ok();
            });

        // Act
        var response = await client.GetAsync("/predicate");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task FortitudeServer_HandlesAsyncResponder()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/async")
            .Returns(async (req, res) =>
            {
                await Task.Delay(50);
                res.NoContent();
            });

        // Act
        var response = await client.GetAsync("/async");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_UsesLastMatchingHandler()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/conflict")
            .Returns((req, res) => res.BadRequest());

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/conflict")
            .Returns((req, res) => res.Ok());

        // Act
        var response = await client.GetAsync("/conflict");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    
    [Fact]
    public async Task FortitudeServer_OkText_SetsContentTypeAndBody()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/text")
            .Returns((req, res) =>
            {
                res.Ok("hello world");
            });

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

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/json")
            .Returns((req, res) =>
            {
                res.Ok(new { name = "Alice", age = 30 });
            });

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
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/nocontent")
            .Returns((req, res) =>
            {
                res.NoContent();
            });

        var response = await client.GetAsync("/nocontent");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", body);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_Redirect_SetsLocationHeader()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/old")
            .Returns((req, res) =>
            {
                res.Redirect("/new");
            });
        
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/new")
            .Returns((req, res) =>
            {
                res.Unauthorized();
            });

        var response = await client.GetAsync("/old");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_PermanentRedirect_Uses308()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/old")
            .Returns((req, res) =>
            {
                res.PermanentRedirect("/new");
            });
        
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/new")
            .Returns((req, res) =>
            {
                res.Conflict();
            });

        var response = await client.GetAsync("/old");

        Assert.Equal((HttpStatusCode)409, response.StatusCode);

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_NotModified_HasNoBodyAndETag()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/cache")
            .Returns((req, res) =>
            {
                res.NotModified("abc123");
            });

        var response = await client.GetAsync("/cache");

        Assert.True(response.Headers.Contains("ETag"));
        Assert.Equal("abc123", response.Headers.GetValues("ETag").Single());
        
        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_TooManyRequests_SetsRetryAfter()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/rate-limit")
            .Returns((req, res) =>
            {
                res.TooManyRequests(retryAfterSeconds: 30);
            });

        var response = await client.GetAsync("/rate-limit");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Equal("30", response.Headers.GetValues("Retry-After").Single());

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_File_SetsContentDisposition()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        var bytes = Encoding.UTF8.GetBytes("file-content");

        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/download")
            .Returns((req, res) =>
            {
                res.File(bytes, "test.txt", "text/plain");
            });

        var response = await client.GetAsync("/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("test.txt", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

        await hubConnection.DisposeAsync();
    }
    [Fact]
    public async Task FortitudeServer_CustomHeaders_AreIncluded()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/headers")
            .Returns((req, res) =>
            {
                res.Ok("ok")
                    .WithHeader("X-Test", "123")
                    .WithHeader("X-Another", "abc");
            });

        var response = await client.GetAsync("/headers");

        Assert.True(response.Headers.Contains("X-Test"));
        Assert.Equal("123", response.Headers.GetValues("X-Test").Single());
        Assert.Equal("abc", response.Headers.GetValues("X-Another").Single());

        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task FortitudeServer_ClearHeaders_RemovesAllHeaders()
    {
        await using var factory = new WebApplicationFactory<Program>();

        // Arrange
        var (client, fortitudeClient, hubConnection) = await Setup(factory);
        fortitudeClient.Accepts()
            .Get()
            .HttpRoute("/clear-headers")
            .Returns((req, res) =>
            {
                res.Ok("ok")
                    .WithHeader("X-Test", "123")
                    .ClearHeaders();
            });

        var response = await client.GetAsync("/clear-headers");

        Assert.False(response.Headers.Contains("X-Test"));

        await hubConnection.DisposeAsync();
    }
}
