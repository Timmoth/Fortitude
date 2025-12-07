using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fortitude.Client;

/// <summary>
///     Represents a response returned by a Fortitude service request, including
///     status code, response body, optional headers, and the originating request ID.
/// </summary>
public sealed class FortitudeResponse
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class
    ///     with default values.
    /// </summary>
    public FortitudeResponse()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class
    ///     using the provided <paramref name="requestId" /> and defaulting to
    ///     status code 200 (OK).
    /// </summary>
    /// <param name="requestId">The identifier of the originating request.</param>
    public FortitudeResponse(Guid requestId)
        : this(requestId, 200)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse" /> class
    ///     with explicit request details.
    /// </summary>
    /// <param name="requestId">The identifier of the originating request.</param>
    /// <param name="status">The status code to assign.</param>
    /// <param name="headers">Optional response headers.</param>
    /// <param name="body">Optional response body.</param>
    public FortitudeResponse(
        Guid requestId,
        int status = 200,
        Dictionary<string, string>? headers = null,
        string? body = null)
    {
        RequestId = requestId;
        Status = status;
        Headers = headers ?? new Dictionary<string, string>();
        Body = body ?? string.Empty;
    }

    /// <summary>
    ///     Gets or sets the unique identifier of the request associated with this response.
    /// </summary>
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    /// <summary>
    ///     Gets or sets the HTTP-like status code for the response.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    ///     Gets or sets the header values associated with the response.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    ///     Gets or sets the optional string body payload of the response.
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    ///     Sets the response to status 404 (Not Found) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse NotFound(string? message = null)
    {
        Status = 404;
        Body = message ?? "Not Found";
        return this;
    }

    /// <summary>
    ///     Sets the response to status 501 (Method Not Implemented) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse MethodNotImplemented(string? message = null)
    {
        Status = 501;
        Body = message ?? "Method Not Implemented";
        return this;
    }

    /// <summary>
    ///     Sets the response to status 400 (Bad Request) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse BadRequest(string? message = null)
    {
        Status = 400;
        Body = message ?? "Bad Request";
        return this;
    }
    
    /// <summary>
    ///     Sets the response to status 504 (Gateway Timeout) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse GatewayTimeout(string? message = null)
    {
        Status = 504;
        Body = message ?? "Gateway Timeout";
        return this;
    }
    

    /// <summary>
    ///     Sets the response to status 500 (Internal Server Error) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse InternalServerError(string? message = null)
    {
        Status = 500;
        Body = message ?? "Internal Server Error";
        return this;
    }

    /// <summary>
    ///     Sets the response to status 403 (Forbidden) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse Forbidden(string? message = null)
    {
        Status = 403;
        Body = message ?? "Forbidden";
        return this;
    }

    /// <summary>
    ///     Sets the response to status 401 (Unauthorized) and assigns an optional message.
    /// </summary>
    /// <param name="message">Optional message to include in the body.</param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse Unauthorized(string? message = null)
    {
        Status = 401;
        Body = message ?? "Unauthorized";
        return this;
    }

    /// <summary>
    ///     Sets the response to status 200 (OK) and optionally assigns a raw string body
    ///     and/or replaces the existing header collection.
    /// </summary>
    /// <param name="body">
    ///     Optional raw string body to assign. If <c>null</c>, the existing body is preserved.
    /// </param>
    /// <param name="headers">
    ///     Optional headers to assign. If provided, they replace the existing header collection.
    /// </param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse Ok(string? body = null, Dictionary<string, string>? headers = null)
    {
        Status = 200;

        if (body != null)
            Body = body;

        if (headers != null)
            Headers = headers;

        return this;
    }

    /// <summary>
    ///     Sets the response to status 200 (OK) and assigns a serialized JSON representation
    ///     of the specified object to the response body. Optionally replaces the existing
    ///     header collection.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize into the body.</typeparam>
    /// <param name="body">The object to serialize into JSON for the response body.</param>
    /// <param name="headers">
    ///     Optional headers to assign. If provided, they replace the existing header collection.
    /// </param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse Ok<T>(T body, Dictionary<string, string>? headers = null)
    {
        Status = 200;

        Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);

        if (headers != null)
            Headers = headers;

        return this;
    }

    /// <summary>
    ///     Sets the response to status 201 (Created) and assigns a serialized JSON representation
    ///     of the specified object to the response body. Optionally replaces the existing
    ///     header collection.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize into the body.</typeparam>
    /// <param name="body">The object to serialize into JSON for the response body.</param>
    /// <param name="headers">
    ///     Optional headers to assign. If provided, they replace the existing header collection.
    /// </param>
    /// <returns>The current <see cref="FortitudeResponse" /> instance.</returns>
    public FortitudeResponse Created<T>(T body, Dictionary<string, string>? headers = null)
    {
        Status = 201;

        Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);

        if (headers != null)
            Headers = headers;

        return this;
    }
}