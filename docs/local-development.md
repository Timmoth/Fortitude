
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

app.Run();

```

### Simulate Realistic Behavior

You now have complete control over the 3rd party services behaviour within your local environment.

**Simulate Errors:**
To determine how your error handling logic works you just have your endpoint return a problem status code.

```csharp
fortitude.Accepts()
    .Get()
    .HttpRoute("/users/{id}")
    .Returns((req, res) =>
    {
        res.Forbidden()
    });
```

**Simulate Latency:**
To determine how your app feels when the 3rd party service is slow simply add an artificial delay to your mock endpoint.

```csharp
app.MapGet("/users/slow", async () => 
{
    await Task.Delay(2000); // Wait for 2 seconds
    return Results.Ok(users);
});
```

**Make it Stateful:**
Sometimes you'll need to simulate a longer flow through your system which will require a stateful mocked service. For example, a `POST /users` endpoint can add a new user to your in-memory list, which can then be retrieved by the `GET` endpoint.

```csharp
// POST /users
app.MapPost("/users", (User user) =>
{
    var newUser = user with { Id = users.Max(u => u.Id) + 1 };
    users.Add(newUser);
    return Results.Created($"/users/{newUser.Id}", newUser);
});
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
