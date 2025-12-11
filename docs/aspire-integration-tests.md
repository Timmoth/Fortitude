
# Integration Testing in .NET Aspire with Fortitude

Modern dotnet projects, especially those orchestrated with Aspire introduce new possibilities for local development and integration testing. Services often rely on external APIs and testing these interactions without relying on real external services is essential for speed, reliability, simplicity.

This guide walks through integration testing .NET Aspire applications using **Fortitude** to mock external HTTP APIs.

## The Example Project

The `Example` solution includes three core projects:

- **`Fortitude.Example.Api`** - A simple web API that depends on an external user service and communicates via `IUserClient`.
- **`Fortitude.Example.AppHost`** - The Aspire application host. It hooks up the services in the distributed application, including the `example-api` and the `fortitude-server`.
- **`Fortitude.Example.Tests`** - xunit test project.

## 1. Setting Up the Test Environment with Aspire

Your Aspire `AppHost` defines your services and how they connect.

### `AppHost.cs`

```csharp
// Examples/Fortitude.Example.AppHost/AppHost.cs

var builder = DistributedApplication.CreateBuilder(args);

var fortitudeServer = builder.AddProject<Fortitude_Server>("fortitude-server")
    .WithEnvironment("Settings:Broadcast", "true");

var exampleApi = builder.AddProject<Fortitude_Example_Api>("example-api")
    .WithReference(fortitudeServer)
    .WithEnvironment("ExternalApi:BaseUrl", fortitudeServer.GetEndpoint("http"))
    .WaitFor(fortitudeServer);

builder.Build().Run();
```

In the `AppHost` we need to:

1.  Add the `fortitude-server` project. This will be our mock server for the external API.
2.  Configure the `example-api` service to use the Fortitude server’s URL via its ExternalApi:BaseUrl. Aspire’s built-in service discovery makes this easy.

## 2. Creating a Test Fixture for Aspire

To run integration tests, you need Aspire to spin up your distributed application. An xUnit test fixture handles this setup and teardown cleanly.

### `AspireTestFixture.cs`

```csharp
// Examples/Fortitude.Example.Tests/AspireTestFixture.cs

public sealed class AspireTestFixture : IAsyncLifetime
{
    public DistributedApplication? _app;
    private ResourceNotificationService? _notificationService;

    public async Task InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Fortitude_Example_AppHost>();
        _app = await builder.BuildAsync();
        _notificationService = _app.Services.GetService<ResourceNotificationService>();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }
    
    public async Task<HttpClient> CreateHttpClient(string applicationName)
    {
        var _testingClient = _app!.CreateHttpClient(applicationName);
        await _notificationService!.WaitForResourceAsync(applicationName, KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));
        return _testingClient;
    }
}
```

This fixture uses `DistributedApplicationTestingBuilder` to build and start our `AppHost`. It provides a helper method, `CreateHttpClient`, which gives us a `HttpClient` that can communicate with our `example-api` service running within the test host.

### `AspireTests.cs`

Testing user creation.

```csharp
// Examples/Fortitude.Example.Tests/AspireTests.cs

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
```

Here's a breakdown of what's happening:

1.  **Connect to Fortitude**: We connect to the `fortitude-server` running in our test host.
2.  **Define a Mock Response**: We use Fortitude's fluent API to define a handler, which essentially says: "When you receive a `POST` request to `/users` with a specific email in the body, respond with a `201 Created` and a new user object."
3.  **Get an `HttpClient`**: We get a client for our `example-api` from Aspire.
4.  **Make the Request**: We `POST` a new user to our `example-api`.
5.  **Assert the Response**: Our `example-api` forwards the request to the `fortitude-server` which in turn forwards the request into the fortitude client, in our case this is our test runner which then finds the matching handler we defined and returns the mock response. 
6.  **Verify Interactions**: We can assert that our mock handler received exactly one request, confirming our API behaved as expected.
