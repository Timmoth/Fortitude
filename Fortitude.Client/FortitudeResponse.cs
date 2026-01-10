using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fortitude.Client;

/// <summary>
///     Represents a response returned by a Fortitude service request, including
///     status code, response body, headers, content type, and the originating request ID.
/// </summary>
public sealed record FortitudeResponse
{
    internal static readonly JsonSerializerOptions DefaultJsonOptions = JsonSerializerOptions.Web;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class.
    /// </summary>
    public FortitudeResponse()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class
    ///     with the given <paramref name="requestId" /> and a default status of 200 (OK).
    /// </summary>
    /// <param name="requestId">The unique identifier of the originating request.</param>
    public FortitudeResponse(Guid requestId)
        : this(requestId, 200)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class
    ///     with full control of status, headers, and body.
    /// </summary>
    /// <param name="requestId">The unique identifier of the originating request.</param>
    /// <param name="status">The HTTP-like status code.</param>
    /// <param name="headers">Optional response headers.</param>
    /// <param name="body">Optional response body.</param>
    /// <param name="contentType">The response content type.</param>
    public FortitudeResponse(
        Guid requestId,
        int status = 200,
        Dictionary<string, string>? headers = null,
        byte[]? body = null,
        string contentType = "application/octet-stream")
    {
        RequestId = requestId;
        Status = status;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Body = body;
        ContentType = contentType;
    }

    /// <summary>
    ///     Gets or sets the unique identifier of the originating request.
    /// </summary>
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    /// <summary>
    ///     Gets or sets the HTTP-like status code for the response.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    ///     Gets or sets the content type describing the response body.
    /// </summary>
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    ///     Gets or sets headers associated with the response.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets or sets the raw response body.
    /// </summary>
    [JsonPropertyName("body")]
    public byte[]? Body { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the status code represents success (2xx).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccessStatusCode => Status is >= 200 and <= 299;

    #region HTTP conversion

    /// <summary>
    ///     Converts this instance to an <see cref="HttpResponseMessage" />.
    /// </summary>
    public HttpResponseMessage ToHttpResponseMessage()
    {
        var response = new HttpResponseMessage((HttpStatusCode)Status);

        if (Body is { Length: > 0 })
        {
            response.Content = new ByteArrayContent(Body);
            response.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(ContentType);
            response.Content.Headers.ContentLength = Body.Length;
        }

        foreach (var header in Headers)
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                response.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return response;
    }

    #endregion

    #region Common status helpers

    /// <summary>
    ///     Sets a 200 (OK) plain-text response.
    /// </summary>
    public FortitudeResponse Ok(string? message = null, Dictionary<string, string>? headers = null)
    {
        return SetText(200, message ?? string.Empty).WithHeaders(headers);
    }

    /// <summary>
    ///     Sets a 200 (OK) JSON response.
    /// </summary>
    public FortitudeResponse Ok<T>(T body, Dictionary<string, string>? headers = null)
    {
        return SetJson(200, body, headers);
    }

    /// <summary>
    ///     Sets a 201 (Created) JSON response and optional Location header.
    /// </summary>
    public FortitudeResponse Created<T>(T body, string? location = null)
    {
        return SetJson(201, body).WithLocation(location);
    }

    /// <summary>
    ///     Sets a 202 (Accepted) response.
    /// </summary>
    public FortitudeResponse Accepted(string? message = null)
    {
        return SetText(202, message ?? "Accepted");
    }

    /// <summary>
    ///     Sets a 204 (No Content) response.
    /// </summary>
    public FortitudeResponse NoContent()
    {
        Status = 204;
        ClearBody();
        return this;
    }

    /// <summary>
    ///     Sets a 400 (Bad Request) response.
    /// </summary>
    public FortitudeResponse BadRequest(string? message = null)
    {
        return SetText(400, message ?? "Bad Request");
    }

    /// <summary>
    ///     Sets a 401 (Unauthorized) response.
    /// </summary>
    public FortitudeResponse Unauthorized(string? message = null)
    {
        return SetText(401, message ?? "Unauthorized");
    }

    /// <summary>
    ///     Sets a 403 (Forbidden) response.
    /// </summary>
    public FortitudeResponse Forbidden(string? message = null)
    {
        return SetText(403, message ?? "Forbidden");
    }

    /// <summary>
    ///     Sets a 404 (Not Found) response.
    /// </summary>
    public FortitudeResponse NotFound(string? message = null)
    {
        return SetText(404, message ?? "Not Found");
    }

    /// <summary>
    ///     Sets a 409 (Conflict) response.
    /// </summary>
    public FortitudeResponse Conflict(string? message = null)
    {
        return SetText(409, message ?? "Conflict");
    }

    /// <summary>
    ///     Sets a 429 (Too Many Requests) response and optional Retry-After header.
    /// </summary>
    public FortitudeResponse TooManyRequests(string? message = null, int? retryAfterSeconds = null)
    {
        return SetText(429, message ?? "Too Many Requests")
            .WithRetryAfter(retryAfterSeconds);
    }

    /// <summary>
    ///     Sets a 500 (Internal Server Error) response.
    /// </summary>
    public FortitudeResponse InternalServerError(string? message = null)
    {
        return SetText(500, message ?? "Internal Server Error");
    }

    /// <summary>
    ///     Sets a 501 (Not Implemented) response.
    /// </summary>
    public FortitudeResponse MethodNotImplemented(string? message = null)
    {
        return SetText(501, message ?? "Not Implemented");
    }

    /// <summary>
    ///     Sets a 504 (Gateway Timeout) response.
    /// </summary>
    public FortitudeResponse GatewayTimeout(string? message = null)
    {
        return SetText(504, message ?? "Gateway Timeout");
    }

    #endregion

    #region Redirects

    /// <summary>
    ///     Sets a 302 (Found) redirect response.
    /// </summary>
    public FortitudeResponse Redirect(string location)
    {
        Status = 302;
        return WithLocation(location);
    }

    /// <summary>
    ///     Sets a 308 (Permanent Redirect) response.
    /// </summary>
    public FortitudeResponse PermanentRedirect(string location)
    {
        Status = 308;
        return WithLocation(location);
    }

    /// <summary>
    ///     Sets a 304 (Not Modified) response with an optional ETag.
    /// </summary>
    public FortitudeResponse NotModified(string? etag = null)
    {
        Status = 304;
        return WithETag(etag).ClearBody();
    }

    #endregion

    #region Body helpers

    /// <summary>
    ///     Sets a plain-text response with the specified status code.
    /// </summary>
    public FortitudeResponse SetText(int status, string message)
    {
        Status = status;
        return SetTextBody(message);
    }

    /// <summary>
    ///     Sets the response body to UTF-8 encoded plain text.
    /// </summary>
    public FortitudeResponse SetTextBody(string message)
    {
        Body = Encoding.UTF8.GetBytes(message);
        ContentType = "text/plain; charset=utf-8";
        return this;
    }

    /// <summary>
    ///     Sets a JSON response for the specified status code.
    /// </summary>
    public FortitudeResponse SetJson<T>(int status, T body, Dictionary<string, string>? headers = null)
    {
        Status = status;
        Body = JsonSerializer.SerializeToUtf8Bytes(body, DefaultJsonOptions);
        ContentType = "application/json";
        return WithHeaders(headers);
    }

    /// <summary>
    ///     Sets a binary response body.
    /// </summary>
    public FortitudeResponse SetBinary(byte[] data, string contentType = "application/octet-stream")
    {
        Body = data;
        ContentType = contentType;
        return this;
    }

    /// <summary>
    ///     Sets a file download response.
    /// </summary>
    public FortitudeResponse File(byte[] data, string fileName, string contentType)
    {
        return SetBinary(data, contentType)
            .WithHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
    }

    /// <summary>
    ///     Clears the response body.
    /// </summary>
    public FortitudeResponse ClearBody()
    {
        Body = null;
        return this;
    }

    #endregion

    #region Header helpers

    /// <summary>
    ///     Adds or removes a response header.
    /// </summary>
    public FortitudeResponse WithHeader(string name, string? value)
    {
        if (value == null)
            Headers.Remove(name);
        else
            Headers[name] = value;

        return this;
    }

    /// <summary>
    ///     Adds or replaces multiple response headers.
    /// </summary>
    public FortitudeResponse WithHeaders(Dictionary<string, string>? headers)
    {
        if (headers == null) return this;

        foreach (var kvp in headers)
            Headers[kvp.Key] = kvp.Value;

        return this;
    }

    /// <summary>
    ///     Removes all response headers.
    /// </summary>
    public FortitudeResponse ClearHeaders()
    {
        Headers.Clear();
        return this;
    }

    /// <summary>
    ///     Sets the Location response header.
    /// </summary>
    public FortitudeResponse WithLocation(string? location)
    {
        return location == null ? this : WithHeader("Location", location);
    }

    /// <summary>
    ///     Sets the ETag response header.
    /// </summary>
    public FortitudeResponse WithETag(string? etag)
    {
        return etag == null ? this : WithHeader("ETag", etag);
    }

    /// <summary>
    ///     Sets the Cache-Control response header.
    /// </summary>
    public FortitudeResponse WithCacheControl(string value)
    {
        return WithHeader("Cache-Control", value);
    }

    /// <summary>
    ///     Disables client and intermediary caching.
    /// </summary>
    public FortitudeResponse WithNoCache()
    {
        return WithCacheControl("no-store, no-cache, must-revalidate");
    }

    /// <summary>
    ///     Sets a correlation identifier header.
    /// </summary>
    public FortitudeResponse WithCorrelationId(Guid correlationId)
    {
        return WithHeader("X-Correlation-Id", correlationId.ToString());
    }

    /// <summary>
    ///     Sets the Retry-After response header.
    /// </summary>
    public FortitudeResponse WithRetryAfter(int? seconds)
    {
        return seconds == null ? this : WithHeader("Retry-After", seconds.Value.ToString());
    }

    #endregion
}