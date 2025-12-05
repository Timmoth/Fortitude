# Fortitude Server - WIP
_Cheat reality just enough to achieve your objective - A lightweight, fluent, middleware-powered fake server facilitating next level testing_

<p align="center">
  <img src="./docs/banner.png" width="240" alt="Fortitude Banner"/>
</p>

## What is Fortitude?
**Fortitude** is a .NET testing utility that spins up a lightweight in-process server so your tests can define **fake service behavior dynamically and fluently**.

Fortitude enables you to:
- Define fake service behavior **dynamically and fluently**
- Mock real external services without any workarounds
- Perform true **black-box** and integration testing
- Simulate headers, query params, method checks, body predicates, and more
- Operate without standing up real infrastructure or modifying your SUT

Fortitude acts as a highly flexible mock server for your tests — returning exactly what you define, when you define it. Your SUT thinks it’s calling real services, but your tests are in total control.

## **Operation Fortitude in history**

The name **Fortitude** is a deliberate nod to **Operation Fortitude**, the famed WW2 deception campaign used by the Allies in 1944.

Operation Fortitude was part of the larger deception strategy preceding D-Day.  
Its purpose was to convince German intelligence that the invasion would occur in **Pas-de-Calais** instead of Normandy.

The Allies used:
- Inflatable tanks  
- Wooden aircraft  
- Entire ghost armies  

All designed to **simulate real military forces that didn’t actually exist**.
 
Much like its namesake, Fortitude simulates service behavior - a controlled deception that empowers your testing strategy.


## Key Features
- **Ideal for black-box testing**  
- **Fluent API** for defining fake routes, responses, and behaviors  
- **Middleware-driven architecture** that intercepts routes  
- **No need for external mocks**  
- **Minimal Configuration**

## Example

### Running the Fortitude Server via Docker

To pull the latest Fortitude Server image from Docker Hub:

```docker pull aptacode/fortitude-server:latest```

To run the container and expose port 8080:

```docker run -p 5093:8080 aptacode/fortitude-server:latest```

```csharp
    [Fact]
    public async Task CanCreateUserWithHeadersAndQueryParams()
    {
        // Given
        
        // Start Fortitude client
        var fortitude = new FortitudeClient(_output);
        await fortitude.StartAsync(url: "http://localhost:5093/fortitude");

        // Define handler for POST /users with header and query param checks
        var handler = new FortitudeHandlerExtensions.FortitudeHandlerBuilder()
            .Post()
            .HttpRoute("/users")
            .Header("X-Auth-Token", "secret-token")
            .QueryParam("source", "unit-test")
            .Body(body =>
            {
                var req = JsonSerializer.Deserialize<CreateUserRequest>(body ?? "");
                return req != null && !string.IsNullOrWhiteSpace(req.Name) && req.Age > 0;
            })
            .Build(request =>
            {
                var reqObj = JsonSerializer.Deserialize<CreateUserRequest>(request.Body ?? "")!;
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

        fortitude.Add(handler);

        // When
        // SUT would make this HTTP request internally,
        // For the sake of the demo we'll make it here
        using var http = new HttpClient();
        var userRequest = new CreateUserRequest { Name = "Alice", Age = 30 };
        var requestBody = new StringContent(JsonSerializer.Serialize(userRequest), Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5093/users?source=unit-test")
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
