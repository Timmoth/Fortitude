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
