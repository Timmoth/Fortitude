using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fortitude.Client;

/// <summary>
///     Represents a response returned by a Fortitude service request, including
///     status code, response body, headers, content type, and the originating request ID.
/// </summary>
public sealed class FortitudeResponse
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = JsonSerializerOptions.Web;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse"/> class.
    /// </summary>
    public FortitudeResponse() { }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse"/> class
    ///     with the given <paramref name="requestId"/> and a default status of 200 (OK).
    /// </summary>
    public FortitudeResponse(Guid requestId)
        : this(requestId, 200) { }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FortitudeResponse"/> class
    ///     with full control of status, headers, and body.
    /// </summary>
    public FortitudeResponse(
        Guid requestId,
        int status = 200,
        Dictionary<string, string>? headers = null,
        byte[]? body = null,
        string contentType = "application/octet-stream")
    {
        RequestId = requestId;
        Status = status;
        Headers = headers ?? new Dictionary<string, string>();
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
    
    public FortitudeResponse NotFound(string? message = null)
        => SetText(404, message ?? "Not Found");

    public FortitudeResponse MethodNotImplemented(string? message = null)
        => SetText(501, message ?? "Method Not Implemented");

    public FortitudeResponse BadRequest(string? message = null)
        => SetText(400, message ?? "Bad Request");

    public FortitudeResponse GatewayTimeout(string? message = null)
        => SetText(504, message ?? "Gateway Timeout");

    public FortitudeResponse InternalServerError(string? message = null)
        => SetText(500, message ?? "Internal Server Error");

    public FortitudeResponse Forbidden(string? message = null)
        => SetText(403, message ?? "Forbidden");

    public FortitudeResponse Unauthorized(string? message = null)
        => SetText(401, message ?? "Unauthorized");

    /// <summary>
    ///     Sets a plain-text response with status 200 (OK).
    /// </summary>
    public FortitudeResponse Ok(string? message = null, Dictionary<string, string>? headers = null)
    {
        Status = 200;

        if (message != null)
            SetTextBody(message);

        if (headers != null)
            ReplaceHeaders(headers);

        return this;
    }

    /// <summary>
    ///     Sets a JSON response for any serializable type.
    /// </summary>
    public FortitudeResponse Ok<T>(T body, Dictionary<string, string>? headers = null)
        => SetJson(200, body, headers);

    /// <summary>
    ///     Sets a JSON response with status 201 (Created).
    /// </summary>
    public FortitudeResponse Created<T>(T body, Dictionary<string, string>? headers = null)
        => SetJson(201, body, headers);
    
    /// <summary>
    ///     Sets a UTF-8 text response for the given status code.
    /// </summary>
    public FortitudeResponse SetText(int status, string message)
    {
        Status = status;
        SetTextBody(message);
        return this;
    }

    /// <summary>
    ///     Sets the body to a UTF-8 text payload and configures Content-Type automatically.
    /// </summary>
    public FortitudeResponse SetTextBody(string message)
    {
        Body = Encoding.UTF8.GetBytes(message);
        ContentType = "text/plain; charset=utf-8";
        return this;
    }

    /// <summary>
    ///     Sets a JSON response for any serializable object.
    ///     JSON Content-Type is inferred automatically.
    /// </summary>
    public FortitudeResponse SetJson<T>(int status, T body, Dictionary<string, string>? headers = null)
    {
        Status = status;

        Body = JsonSerializer.SerializeToUtf8Bytes(body, DefaultJsonOptions);
        ContentType = "application/json";

        if (headers != null)
            ReplaceHeaders(headers);

        return this;
    }

    /// <summary>
    ///     Sets an arbitrary byte array as the response body.
    ///     Content-Type is inferred if possible.
    /// </summary>
    public FortitudeResponse SetBinary(byte[] data, string? inferredContentType = null)
    {
        Body = data;
        ContentType = inferredContentType ?? "application/octet-stream";
        return this;
    }

    /// <summary>
    ///     Sets a file-like response from a stream.
    ///     Stream will be fully copied into memory.
    /// </summary>
    public async Task<FortitudeResponse> SetStreamAsync(Stream stream, string contentType = "application/octet-stream")
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);

        Body = ms.ToArray();
        ContentType = contentType;

        return this;
    }

    /// <summary>
    ///     Sets the response body automatically based on the type of the given value:
    ///     - string → text/plain
    ///     - byte[] → application/octet-stream
    ///     - any other object → application/json
    /// </summary>
    public FortitudeResponse Auto(object? value, int status = 200)
    {
        Status = status;

        switch (value)
        {
            case null:
                Body = null;
                ContentType = "text/plain; charset=utf-8";
                break;

            case string s:
                SetTextBody(s);
                break;

            case byte[] bytes:
                SetBinary(bytes);
                break;

            default:
                SetJson(status, value);
                break;
        }

        return this;
    }
    
    private void ReplaceHeaders(Dictionary<string, string> headers)
        => Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
    
    public override string ToString()
    {
        var bodyInfo = Body == null ? "null" : $"{Body.Length} bytes";
        var headerInfo = Headers?.Count > 0 ? $"{Headers.Count} headers" : "no headers";

        return $"Status {Status} ({ContentType}), Body: {bodyInfo}, {headerInfo} [RequestId: {RequestId}]";
    }

}
