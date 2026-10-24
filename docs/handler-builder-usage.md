# FortitudeHandlerBuilder Usage Guide

This document describes each method available on `FortitudeHandlerBuilder`. Examples focus only on the method being described and assume chaining from `fortitudeClient.Accepts()`.

## Entry Point

### Accepts()

Starts building a request handler for a `FortitudeClient`.

```csharp
fortitudeClient.Accepts()
```

## Request Predicates

### Matches(Func<FortitudeRequest, bool>)

Adds a custom predicate that must return true for the request to match.

```csharp
.Matches(req => req.Route == "/health")
```

## HTTP Method Matching

### Method(string method)

Matches a specific HTTP method.

```csharp
.Method("GET")
```
### Get()

Matches HTTP GET requests.

```csharp
.Get()
```
### Post()

Matches HTTP POST requests.

```csharp
.Post()
```

### Put()

Matches HTTP PUT requests.

```csharp
.Put()
```

### Delete()

Matches HTTP DELETE requests.

```csharp
.Delete()
```
### Patch()

Matches HTTP PATCH requests.

```csharp
.Patch()
```
### Options()

Matches HTTP OPTIONS requests.

```csharp
.Options()
```

### Methods(params string[] methods)

Matches multiple HTTP methods.

```csharp
.Methods("GET", "POST")
```

### AnyMethod()

Clears method restrictions and matches any HTTP method.

```csharp
.AnyMethod()
```

## Routing & URL Matching

### HttpRoute(string route)

Matches an exact HTTP route.

```csharp
.HttpRoute("/users")
```

### RouteStartsWith(string prefix)

Matches routes that start with the given prefix.

```csharp
.RouteStartsWith("/api")
```

### RouteEndsWith(string suffix)

Matches routes that end with the given suffix.

```csharp
.RouteEndsWith("/status")
```

## Header Matching

### Header(string key, string value)

Requires a specific header and value.

```csharp
.Header("X-Env", "test")
```

### HeaderExists(string key)

Requires a header to exist, regardless of value.

```csharp
.HeaderExists("X-Request-Id")
```

### Authorization(string value)

Matches an exact Authorization header value.

```csharp
.Authorization("Basic abc123")
```

### BearerToken(string token)

Matches a Bearer token Authorization header.

```csharp
.BearerToken("my-token")
```

### Accept(string mediaType)

Matches the Accept header.

```csharp
.Accept("application/json")
```

### ContentType(string contentType)

Matches the Content-Type header.

```csharp
.ContentType("application/json")
```

### UserAgent(string userAgent)

Matches the User-Agent header.

```csharp
.UserAgent("Fortitude-Test")
```

## Query String Matching

### QueryParam(string key, string value)

Requires a query parameter with a specific value.

```csharp
.QueryParam("page", "1")
```

### QueryParamExists(string key)

Requires a query parameter to exist, regardless of value.

```csharp
.QueryParamExists("debug")
```

### QueryParams(Dictionary<string, string> parameters)

Adds multiple required query parameters.

```csharp
.QueryParams(new Dictionary<string, string>
{
    ["sort"] = "asc",
    ["limit"] = "10"
})
```

## Body Matching

### Body(Func<byte[]?, bool>)

Adds a custom predicate for matching the request body.

```csharp
.Body(body => Encoding.UTF8.GetString(body!) == "hello")
```

### BodyIsEmpty()

Matches requests with no body or an empty body.

```csharp
.BodyIsEmpty()
```

### BodyContains(string text)

Matches requests whose UTF-8 body contains the given text.

```csharp
.BodyContains("hello")
```

### JsonBody()

Matches requests with Content-Type set to application/json.

```csharp
.JsonBody()
```

### JsonBody<T>(Func<T, bool> predicate)

Deserializes the JSON body and applies a predicate to the result.

```csharp
.JsonBody<MyDto>(dto => dto.IsActive)
```

## Responders

### Returns(Action<FortitudeRequest, FortitudeResponse>)

Registers a synchronous responder.

```csharp
.Returns((req, res) =>
{
    res.Ok();
})
```

### Returns(Func<FortitudeRequest, FortitudeResponse, Task>)

Registers an asynchronous responder.

```csharp
.Returns(async (req, res) =>
{
    await Task.Delay(10);
    res.Ok();
})
```

## Simulating latency

`WithDelay` holds the response back, so the system under test experiences a slow dependency.
This is how you exercise timeouts, retries and circuit breakers.

```csharp
fortitude.Accepts()
    .Get()
    .HttpRoute("/users")
    .WithDelay(TimeSpan.FromSeconds(2))
    .Returns((req, res) => res.Ok(users));
```

`WithDelay` also takes milliseconds directly, and `WithRandomDelay` draws a fresh delay from a
range on every request to simulate jitter:

```csharp
.WithDelay(250)                 // always 250ms
.WithRandomDelay(100, 500)      // uniform between 100ms and 500ms
```

Two things worth knowing:

- The server gives up on a handler after `Settings:ResponseTimeoutSeconds` (5 seconds by
  default) and returns `504`. A delay beyond that is cut short - which is exactly how you test
  a client's timeout handling, but raise the setting if it is not what you meant.
- `WaitForRequestAsync` completes as soon as the request arrives, before the delay elapses, so
  asserting that the dependency was called does not cost you the simulated latency.

In YAML the same options live on the response:

```yaml
response:
  status: 200
  delayMs: 2000
```

```yaml
response:
  status: 200
  delay:
    minMs: 100
    maxMs: 500
```

## Limiting how many times a handler answers

`Times(n)`, and its shorthands `Once()` and `Twice()`, stop a handler matching once it has
answered that many requests. An exhausted handler steps aside and the next matching one takes
over, which is how retry behaviour is expressed:

```csharp
// Registered first, so it is the fallback.
fortitude.Accepts().Get().HttpRoute("/flaky")
    .Returns((req, res) => res.Ok("recovered"));

// Registered later, so it wins - until it has answered twice.
fortitude.Accepts().Get().HttpRoute("/flaky")
    .Twice()
    .Returns((req, res) => res.InternalServerError("down"));
```

The first two calls get a 500, the third gets "recovered". `CallCount` and `IsExhausted` on the
returned handler let you assert on it afterwards.

With no fallback registered, requests after the limit fall through to `501 Not Implemented`.

Under genuinely concurrent requests the limit is best effort - two requests can both pass the
check before either is counted. Sequential use, the normal case in tests, is exact.

## Returning a different response each time

`ReturnsInSequence` walks a list of responders, repeating the last one once the sequence runs
out. Useful for polling flows:

```csharp
fortitude.Accepts().Get().HttpRoute("/jobs/1")
    .ReturnsInSequence(
        (req, res) => res.Ok(new { status = "pending" }),
        (req, res) => res.Ok(new { status = "pending" }),
        (req, res) => res.Ok(new { status = "complete" }));
```

## Controlling which handler wins

When several handlers match the same request, the most recently registered one answers. That
default is usually what you want - it lets a test override a fixture set up in a base class.

`Priority(n)` overrides it explicitly. Higher wins, the default is `0`, and negatives are
useful for a catch-all that should only apply when nothing else matches:

```csharp
fortitude.Accepts().Get().RouteStartsWith("/")
    .Priority(-100)
    .Returns((req, res) => res.NotFound("no handler for this route"));
```

## Injecting faults

An error status is the easy case for a client to handle. `ReturnsFault` produces the harder
ones: a dependency that goes away mid-request, or answers with something unusable.

```csharp
fortitude.Accepts().Get().HttpRoute("/users").ReturnsFault(HttpFault.ConnectionReset);
```

| Fault | What the caller sees |
|---|---|
| `ConnectionReset` | The connection is aborted with no response at all - a socket error, not a status code. In intercept mode this surfaces as `HttpRequestException`. |
| `EmptyBody` | A success status with no content, where content was expected. |
| `MalformedJson` | `application/json` whose body is truncated mid-object and will not parse. |
| `RandomBytes` | `application/json` whose body is binary noise. |

To fail only some of the time, `FailsRandomly` wraps the normal responder:

```csharp
fortitude.Accepts().Get().HttpRoute("/users")
    .FailsRandomly(0.2)                       // 20% of calls get a 500
    .Returns((req, res) => res.Ok(users));
```

The failure response is configurable, which is handy for rate limiting:

```csharp
.FailsRandomly(0.1, (req, res) => res.TooManyRequests("slow down", retryAfterSeconds: 30))
```

Faults combine with the other modifiers, so a handler can fail the first call and then behave:

```csharp
fortitude.Accepts().Get().HttpRoute("/users")
    .Once()
    .ReturnsFault(HttpFault.ConnectionReset);
```
