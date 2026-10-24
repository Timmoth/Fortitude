
# Mocking Third-Party APIs in .NET Integration Tests with Fortitude

Using real external services in integration tests can introduce slowdowns, flakiness, and unnecessary cost. Mocking that dependency is usually the better approach, and Fortitude offers a fluent API that makes this style of testing both clear and flexible.

## Getting Started

Fortitude works by defining a set of handlers within your test. Each handler defines a set of matching conditions for an incoming HTTP request (e.g., the method, the URL, headers, body content) and defines the response to return if those conditions are met.

## `FortitudeClient.Create()`

Begin by creating a FortitudeClient

```csharp
// In your test class
var fortitude = FortitudeClient.Create(output); // 'output' is an ITestOutputHelper for logging
```

### Start with `Accepts()`

Each behaviour starts with Accepts():

```csharp
var handler = fortitude.Accepts()
    // ... chain more methods here
```

### Match the HTTP Method

Specify the method you want to match:

```csharp
.Post() // or .Get(), .Put(), .Delete(), etc.
```

### Match the Route

Use .HttpRoute() to define the URL pattern:

```csharp
.HttpRoute("/users") // Matches POST /users
```
Route parameters work as expected:
```csharp
.HttpRoute("/users/{id}") // Matches GET /users/123
```

### Match the Request Body

If you need to validate incoming JSON, .Body() gives you full control:

```csharp
.Body(body => body.ToJson<User>()?.Email == "alice@example.com")
```

### Define the Response with `Returns()`

Specify what Fortitude should return when all the rules matche:

```csharp
.Returns((request, response) =>
{
    var reqObj = request.Body.ToJson<User>()!;
    response.Created(new User(999, reqObj.Name, reqObj.Email));
});
```

The `response` builder has methods for all common scenarios: `Ok()`, `NotFound()`, `BadRequest()`, `NoContent()`, and more.

## Putting It All Together

Here is the complete handler we just built:

```csharp
var handler = fortitude.Accepts()
    .Post()
    .HttpRoute("/users")
    .Body(body => body.ToJson<User>()?.Email == "alice@example.com")
    .Returns((request, response) =>
    {
        var reqObj = request.Body.ToJson<User>()!;
        response.Created(new User(999, reqObj.Name, reqObj.Email));
    });
```

Any request that fits these criteria will receive the defined response.

## Verifying Interactions

Beyond controlling responses, Fortitude helps you verify that your service made the correct calls during the test. Each handler records the requests it fulfilled:

The handler object tracks all requests it receives. You can use this to make assertions.

```csharp
// After your test logic has run...

// Assert that the handler received exactly one request
Assert.Single(handler.ReceivedRequests);

// You can also inspect the details of the received requests
var receivedRequest = handler.ReceivedRequests.First();
Assert.Equal("/users", receivedRequest.Path);
```

## Asserting on what the mock received

Every handler records the requests it answered, so the simplest assertion is on the list
itself:

```csharp
Assert.Single(handler.ReceivedRequests);
```

The verification helpers do the same job but fail with a message that explains itself, which
matters when the only thing you have is a CI log:

```csharp
handler.ShouldHaveReceivedOnce();
handler.ShouldHaveReceived(3);
handler.ShouldHaveReceivedAtLeast(1);
handler.ShouldHaveReceivedAtMost(5);
handler.ShouldNeverHaveBeenCalled();
```

A failure prints the handler and the traffic it actually saw:

```
Expected 3 requests, but saw 1 request.
  Handler: [GET] /users
  It received:
    GET /users
```

All of them take an optional predicate to narrow what is counted:

```csharp
handler.ShouldHaveReceived(2, r => r.Method == "POST");
```

### Catching requests you did not set up

A request that matches no handler is answered with `501 Not Implemented`. That is easy to
miss - it tends to look like the dependency misbehaving rather than a gap in the test. The
client keeps them:

```csharp
fortitude.VerifyNoUnmatchedRequests();
```

which fails with both halves of the mismatch:

```
1 request was not matched by any handler:
    GET /accounts?id=7
  Registered handlers:
    [GET] /users
    [POST] /orders  (exhausted)
```

Handlers that have used up a `Times(n)` allowance are marked, since a handler that looks like
it should have matched is the most confusing version of this failure.

`UnmatchedRequests` exposes the same list if you would rather inspect it, and
`ClearUnmatchedRequests()` resets it between phases of a longer test.
