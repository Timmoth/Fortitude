# FortitudeResponse Usage Guide

This document outlines every public method on FortitudeResponse, examples assume you are inside a handler and already have a FortitudeResponse res instance.

## Core Properties

### RequestId

Gets or sets the originating request ID.

```csharp
res.RequestId = requestId;
```
### Status

Gets or sets the HTTP-like status code.

```csharp
res.Status = 404;
```
### ContentType

Gets or sets the response content type.

```csharp
res.ContentType = "application/json";
```
### Headers

Gets or sets the response headers dictionary.

```csharp
res.Headers["X-Test"] = "value";
```
### Body

Gets or sets the raw response body.

```csharp
res.Body = Encoding.UTF8.GetBytes("hello");
```
### IsSuccessStatusCode

Indicates whether the status code is in the 2xx range.

```csharp
if (res.IsSuccessStatusCode) { }
```
## HTTP Conversion

### ToHttpResponseMessage()

Converts the response into an HttpResponseMessage.

```csharp
var httpResponse = res.ToHttpResponseMessage();
```
## Common Status Helpers

### Ok(string? message = null, Dictionary<string,string>? headers = null)

Sets a 200 OK plain-text response.

```csharp
res.Ok("Success");
```
### Ok<T>(T body, Dictionary<string,string>? headers = null)

Sets a 200 OK JSON response.

```csharp
res.Ok(new { message = "Success" });
```
### Created<T>(T body, string? location = null)

Sets a 201 Created JSON response and optional Location header.

```csharp
res.Created(new { id = 1 }, "/items/1");
```
### Accepted(string? message = null)

Sets a 202 Accepted response.

```csharp
res.Accepted();
```
### NoContent()

Sets a 204 No Content response and clears the body.

```csharp
res.NoContent();
```
### BadRequest(string? message = null)

Sets a 400 Bad Request response.

```csharp
res.BadRequest("Invalid input");
```
### Unauthorized(string? message = null)

Sets a 401 Unauthorized response.

```csharp
res.Unauthorized();
```
### Forbidden(string? message = null)

Sets a 403 Forbidden response.

```csharp
res.Forbidden();
```
### NotFound(string? message = null)

Sets a 404 Not Found response.

```csharp
res.NotFound();
```
### Conflict(string? message = null)

Sets a 409 Conflict response.

```csharp
res.Conflict("Already exists");
```
### TooManyRequests(string? message = null, int? retryAfterSeconds = null)

Sets a 429 Too Many Requests response with optional Retry-After header.

```csharp
res.TooManyRequests("Slow down", 30);
```
### InternalServerError(string? message = null)

Sets a 500 Internal Server Error response.

```csharp
res.InternalServerError();
```
### MethodNotImplemented(string? message = null)

Sets a 501 Not Implemented response.

```csharp
res.MethodNotImplemented();
```
### GatewayTimeout(string? message = null)

Sets a 504 Gateway Timeout response.

```csharp
res.GatewayTimeout();
```
## Redirects

### Redirect(string location)

Sets a 302 Found redirect response.

```csharp
res.Redirect("/login");
```
### PermanentRedirect(string location)

Sets a 308 Permanent Redirect response.

```csharp
res.PermanentRedirect("/new-endpoint");
```
### NotModified(string? etag = null)

Sets a 304 Not Modified response and clears the body.

```csharp
res.NotModified("abc123");
```
## Body Helpers

### SetText(int status, string message)

Sets a plain-text response with the specified status code.

```csharp
res.SetText(418, "I'm a teapot");
```
### SetTextBody(string message)

Sets the response body to UTF-8 encoded plain text.

```csharp
res.SetTextBody("Hello world");
```
### SetJson<T>(int status, T body, Dictionary<string,string>? headers = null)

Sets a JSON response with the specified status code.

```csharp
res.SetJson(200, new { ok = true });
```
### SetBinary(byte[] data, string contentType = "application/octet-stream")

Sets a binary response body.

```csharp
res.SetBinary(fileBytes, "application/pdf");
```
### File(byte[] data, string fileName, string contentType)

Sets a file download response.

```csharp
res.File(fileBytes, "report.pdf", "application/pdf");
```
### ClearBody()

Clears the response body.

```csharp
res.ClearBody();
```
## Header Helpers

### WithHeader(string name, string? value)

Adds or removes a response header.

```csharp
res.WithHeader("X-Test", "value");
```
### WithHeaders(Dictionary<string,string>? headers)

Adds or replaces multiple response headers.

```csharp
res.WithHeaders(new Dictionary<string,string> { ["X-A"] = "1" });
```
### ClearHeaders()

Removes all response headers.

```csharp
res.ClearHeaders();
```
### WithLocation(string? location)

Sets the Location header.

```csharp
res.WithLocation("/items/1");
```
### WithETag(string? etag)

Sets the ETag header.

```csharp
res.WithETag("abc123");
```
### WithCacheControl(string value)

Sets the Cache-Control header.

```csharp
res.WithCacheControl("public, max-age=60");
```
### WithNoCache()

Disables client and intermediary caching.

```csharp
res.WithNoCache();
```
### WithCorrelationId(Guid correlationId)

Sets the X-Correlation-Id header.

```csharp
res.WithCorrelationId(correlationId);
```
### WithRetryAfter(int? seconds)

Sets the Retry-After header.

```csharp
res.WithRetryAfter(30);
```