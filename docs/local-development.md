
# Why Build Your Own Mock Service?

 Modern applications depend on external services, but your dev workflow doesn’t have to. A lightweight mock service can make your day-to-day work faster, more reliable, and completely independent of external systems.


## Side effects of relying on a system you don’t control:

-   **Cost / rate limits**: Easy to hit, especially when you’re debugging or experimenting or share services with other devs.
-   **Reliability**: If the API is down or slow, your local work slows down with it.
-   **Difficult to Test Edge Cases**: Simulating errors or edge cases isn’t always possible.
-   **Offline Work**: If you're internet goes down your development is limited.

## An Alternative

A mock service is a small local web server that behaves like the third-party API your app relies on. It doesn’t need to be perfect or even complete, it just needs to mimic the parts your app uses. Instead of pointing to something remote, you connect to something you can run on your own machine.

This gives you full control over:
- Responses
- Latency
- Error conditions
- Test data
- Uptime

## How to Build Your Own Mock Service

Building a mock service is surprisingly easy. Using Fortitude you can create a functional mock server in minutes.

### Create a New Console App Project

Start by creating a new, empty Console App project. This will be your `FakeService`.

### Define Your Endpoints

Look at the documentation for the real API and replicate the endpoints your application uses. You don't need to implement the entire API, only the parts your app actually interacts with.

Let's say the real API has an endpoint `GET /users/{id}`. In your mock service's `Program.cs`, you can create a corresponding endpoint:

```csharp
// In your FakeService's Program.cs

// Connect to a locally running Fortitude Server
var fortitudeBase = Environment.GetEnvironmentVariable("FORTITUDE_URL") ?? throw new ArgumentNullException("FORTITUDE_URL");

var (fortitude, url) = await FortitudeServer.ConnectAsync(fortitudeBase);

// A fake database of users
var users = new List<User>
{
    new User(1, "Alice", "alice@example.com"),
    new User(2, "Bob", "bob@example.com"),
};

// GET /users/{id}
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

// The client runs until the process is stopped; keep it alive.
Console.WriteLine($"Fake service listening on {url}");
await Task.Delay(Timeout.Infinite);
```

`url` is where your application should point: `ConnectAsync` returns the address of the port
this client reserved, which is not necessarily the server's base URL.

### Simulate Realistic Behavior

You now have complete control over the 3rd party services behaviour within your local environment.

**Simulate Errors:**
To determine how your error handling logic works you just have your endpoint return a problem status code.

```csharp
fortitude.Accepts()
    .Get()
    .HttpRoute("/users/{id}")
    .Returns((req, res) => res.Forbidden());
```

**Simulate Latency:**
To see how your app feels when the third party is slow, hold the response back. This is how
you find out whether your timeouts, retries and spinners actually work.

```csharp
fortitude.Accepts()
    .Get()
    .HttpRoute("/users")
    .WithDelay(TimeSpan.FromSeconds(2))
    .Returns((req, res) => res.Ok(users));
```

`WithRandomDelay(100, 500)` varies it per request, which is closer to how a real service
behaves. Note the server gives up after `Settings:ResponseTimeoutSeconds` (5 by default) and
returns a `504`, so raise that if you want a delay longer than it.

**Simulate Faults:**
An error status is the easy case. A dependency that drops the connection, or returns JSON that
does not parse, is the one that finds bugs.

```csharp
fortitude.Accepts()
    .Get()
    .HttpRoute("/users")
    .ReturnsFault(HttpFault.ConnectionReset);
```

`FailsRandomly(0.2)` fails a proportion of calls instead, leaving the rest to the normal
responder - useful for leaving running while you click around.

**Make it Stateful:**
Your mock is ordinary C#, so it can close over state. A `POST /users` can add to a list the
`GET` reads back:

```csharp
var users = new List<User> { new(1, "Alice", "alice@example.com") };

fortitude.Accepts()
    .Get()
    .HttpRoute("/users")
    .Returns((req, res) => res.Ok(users));

fortitude.Accepts()
    .Post()
    .HttpRoute("/users")
    .Returns((req, res) =>
    {
        var posted = req.Body.ToJson<User>()!;
        var created = posted with { Id = users.Max(u => u.Id) + 1 };

        users.Add(created);
        res.Created(created, location: $"/users/{created.Id}");
    });
```

For a flow where the *same* endpoint must answer differently as things progress - queued, then
running, then complete - use a scenario instead:

```csharp
fortitude.Accepts().Get().HttpRoute("/jobs/1")
    .InScenario("job").WhenState("Started").ThenState("Running")
    .Returns((req, res) => res.Ok(new { status = "queued" }));

fortitude.Accepts().Get().HttpRoute("/jobs/1")
    .InScenario("job").WhenState("Running")
    .Returns((req, res) => res.Ok(new { status = "complete" }));
```

### Integrate With Your Application

You just need to tell your main application to talk to the mock service instead of the real one during development.

The best way to do this is through configuration. In your main app's `appsettings.Development.json`, override the base URL for the third-party service:

```json
// In your main application's appsettings.Development.json
{
  "ExternalApiService": {
    "BaseUrl": "http://localhost:5123" // URL of your Fortitude Server
  }
}
```

Your production configuration (`appsettings.json`) would still point to the real API URL.
