using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Fortitude.Client;

/// <summary>
///     Represents an incoming request captured by a Fortitude client or mock server,
///     including method, URL components, headers, cookies, query parameters, and body.
/// </summary>
public class FortitudeRequest
{
    /// <summary>
    ///     The unique identifier for this request.
    /// </summary>
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    /// <summary>
    ///     HTTP method of the request (GET, POST, PUT, DELETE, etc.).
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>
    ///     Base URL (scheme + host) of the request.
    ///     Example: https://example.com
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public required string BaseUrl { get; set; }

    /// <summary>
    ///     The request path/route (e.g., "/api/users/123").
    /// </summary>
    [JsonPropertyName("route")]
    public required string Route { get; set; }

    /// <summary>
    ///     The raw query string, including the leading "?",
    ///     or an empty string if none is present.
    /// </summary>
    [JsonPropertyName("rawQuery")]
    public string RawQuery { get; set; } = string.Empty;

    /// <summary>
    ///     The fully reconstructed request URL.
    ///     Example: https://example.com/api/users?id=123
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; set; }

    /// <summary>
    ///     The HTTP protocol used (HTTP/1.1, HTTP/2, etc.).
    /// </summary>
    [JsonPropertyName("protocol")]
    public required string Protocol { get; set; }

    /// <summary>
    ///     The content type of the request body (if provided).
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>
    ///     The length of the request body in bytes, if known.
    /// </summary>
    [JsonPropertyName("contentLength")]
    public long? ContentLength { get; set; }

    /// <summary>
    ///     Client IP address from which the request originated.
    /// </summary>
    [JsonPropertyName("remoteIp")]
    public string? RemoteIp { get; set; }

    /// <summary>
    ///     Client port from which the request originated.
    /// </summary>
    [JsonPropertyName("remotePort")]
    public int? RemotePort { get; set; }

    /// <summary>
    ///     Headers of the request. Multiple values per header are supported.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string[]> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Query parameters as a multi-value dictionary.
    /// </summary>
    [JsonPropertyName("query")]
    public Dictionary<string, string[]> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Cookies included with the request.
    /// </summary>
    [JsonPropertyName("cookies")]
    public Dictionary<string, string> Cookies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Optional request body as a byte array.
    /// </summary>
    [JsonPropertyName("body")]
    public byte[]? Body { get; set; }

    /// <summary>
    ///     Timestamp when the request was captured.
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Creates a <see cref="FortitudeRequest"/> from an ASP.NET <see cref="HttpContext"/>.
    ///     Reads headers, cookies, body, route, query string, and metadata.
    /// </summary>
    /// <param name="ctx">The HTTP context containing the request.</param>
    /// <param name="requestId">Optional explicit request ID. If not provided, a new one is generated.</param>
    /// <returns>A populated <see cref="FortitudeRequest"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the context or context.Request is null.</exception>
    public static async Task<FortitudeRequest> FromHttpContext(HttpContext ctx, Guid? requestId = null)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (ctx.Request == null) throw new ArgumentNullException(nameof(ctx.Request));

        ctx.Request.EnableBuffering();

        // Read body safely
        byte[]? bodyBytes;
        using (var ms = new MemoryStream())
        {
            await ctx.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            bodyBytes = ms.ToArray();
            ctx.Request.Body.Position = 0;
        }

        var headers = ctx.Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        var query = ctx.Request.Query
            .ToDictionary(q => q.Key, q => q.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        var cookies = ctx.Request.Cookies
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var route = ctx.Request.Path.ToString();
        var rawQuery = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value! : string.Empty;

        var url = rawQuery.Length > 0
            ? $"{baseUrl}{route}{rawQuery}"
            : $"{baseUrl}{route}";

        var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
        var remotePort = ctx.Connection.RemotePort;

        return new FortitudeRequest
        {
            RequestId = requestId ?? Guid.NewGuid(),
            Method = ctx.Request.Method,
            Protocol = ctx.Request.Protocol,
            BaseUrl = baseUrl,
            Route = route,
            RawQuery = rawQuery,
            Url = url,
            ContentType = ctx.Request.ContentType,
            ContentLength = ctx.Request.ContentLength,
            RemoteIp = remoteIp,
            RemotePort = remotePort,
            Headers = headers,
            Query = query,
            Cookies = cookies,
            Body = bodyBytes
        };
    }
}
