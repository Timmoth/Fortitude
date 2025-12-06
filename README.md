# Fortitude Server - WIP
_Fortitude spins up a local mock server so you can fluently configure HTTP routes, responses, and test scenarios directly from your .NET tests._

<p align="center">
  <img src="./docs/banner.png" width="240" alt="Fortitude Banner"/>
</p>

## What is Fortitude?
Fortitude is a .NET testing utility that lets you control and simulate external HTTP APIs directly from your tests. It’s perfect for functional and behavioral testing when your system under test (SUT) depends on services that are unavailable, difficult to run locally, or expensive to maintain.

With Fortitude, your tests can:
- **Define expected API behavior dynamically and fluently**, including headers, query parameters, methods, and body content
- **Perform true black-box integration** tests without fragile mocks or stubs
- **Track, assert, and wait for requests** to ensure the SUT interacts with external services correctly
- **Simulate realistic responses** so your SUT thinks it’s calling real APIs

Fortitude keeps expected behavior close to the test code, increases code coverage, and reduces the complexity of maintaining mocks. Essentially, it gives you full control over your SUT’s external dependencies while letting your tests remain fast, reliable, and expressive.

## Example

### Running the Fortitude Server via Docker

To pull the latest Fortitude Server image from Docker Hub:

```docker pull aptacode/fortitude-server:latest```

To run the container and expose port 8080:

```docker run -p 5093:8080 aptacode/fortitude-server:latest```

### Example

An example project / tests can be in the /Examples directory.


To run the example tests:
```bash
# Run the Fortitude Server Project
dotnet run --project ./Fortitude.Server/

# Or the docker container
docker run -p 5093:8080 aptacode/fortitude-server:latest

# Run the tests
dotnet test ./Examples/Fortitude.Example.Tests

```

**Make sure to add the NuGet package to your test project**

[Fortitude on NuGet](https://www.nuget.org/packages/Fortitude/10.0.0)

Install via the .NET CLI:

```bash
dotnet add package Fortitude --version 10.0.0
```


Here is a sample Test which connects to the Fortitude Server and intercepts request coming from the SUT

```csharp
    [Fact]
    public async Task CreateUser_ForwardsRequestToExternalApi_AndReturnsCreatedResult()
    {
        // Given: Fortitude fake server simulating the external API
        var fortitude = await FortitudeServer.ConnectAsync(FortitudeBase, output);

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
                response.Body = JsonSerializer.Serialize(new User(999, reqObj.Name, reqObj.Email));
                response.Status = 201;
            });
        
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
```

## How it works

```
[Test Code] 
    │
    │ 1. Start FortitudeClient + define handlers
    ▼
[Fortitude Client]
    │
    │ 2. Connects to Fortitude Server (SignalR)
    ▼
[Fortitude Server]
    │
    │ 3. Waits for incoming HTTP requests from SUT
    ▼
[SUT Service]
    │
    │ 4. Makes HTTP call to what it thinks is a real API
    ▼
[Fortitude Server Middleware]
    │
    │ 5. Intercepts request (catch-all middleware)
    │ 6. Forwards request → Fortitude Client via SignalR
    ▼
[Fortitude Client / Test]
    │
    │ 7. Matches request using:
    │      - Method
    │      - Route
    │      - Headers
    │      - Query params
    │      - Body predicates
    │
    │ 8. Selects last matching handler → produces a FortitudeResponse
    ▼
[Fortitude Server]
    │
    │ 9. Returns the fake response to the SUT
    ▼
[SUT Service]
    │
    │ 10. Processes the response as if from a real dependency
    ▼
[Test Code]
    │
    │ 11. Assert on outputs, triggered flows, and the returned data
    ▼
[End]
```

## **Operation Fortitude in history**

The name **Fortitude** is a nod to **Operation Fortitude**, the famed WW2 deception campaign used by the Allies in 1944.

Operation Fortitude was part of the larger deception strategy preceding D-Day.  
Its purpose was to convince German intelligence that the invasion would occur in **Pas-de-Calais** instead of Normandy.

The Allies used:
- Inflatable tanks  
- Wooden aircraft  
- Entire ghost armies  

All designed to **simulate real military forces that didn’t actually exist**.
 
Much like its namesake, Fortitude simulates service behavior - a controlled deception that empowers your testing strategy.