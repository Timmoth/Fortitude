using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Fortitude.Client;

/// <summary>
///     Represents an incoming request to a Fortitude client or server.
/// </summary>
public class FortitudeRequest
{
    /// <summary>
    ///     The unique identifier for the request.
    /// </summary>
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    /// <summary>
    ///     HTTP method of the request (GET, POST, etc.).
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = default!;

    /// <summary>
    ///     Base URL (scheme + host) of the request.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = default!;

    /// <summary>
    ///     Request route/path.
    /// </summary>
    [JsonPropertyName("route")]
    public string Route { get; set; } = default!;

    /// <summary>
    ///     Headers as string arrays to support multiple values per header.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string[]> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Query parameters as string arrays.
    /// </summary>
    [JsonPropertyName("query")]
    public Dictionary<string, string[]> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Cookies as key/value dictionary.
    /// </summary>
    [JsonPropertyName("cookies")]
    public Dictionary<string, string> Cookies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Optional request body as a byte array.
    /// </summary>
    [JsonPropertyName("body")]
    public byte[]? Body { get; set; }

    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Builds a <see cref="FortitudeRequest" /> from an <see cref="HttpContext" />.
    /// </summary>
    /// <param name="ctx">The HTTP context to extract the request from.</param>
    /// <param name="requestId">Optional request ID; if not provided, a new GUID will be generated.</param>
    /// <returns>A <see cref="FortitudeRequest" /> representing the HTTP request.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="ctx" /> or <paramref name="ctx.Request" /> is null.</exception>
    public static async Task<FortitudeRequest> FromHttpContext(HttpContext ctx, Guid? requestId = null)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (ctx.Request == null) throw new ArgumentNullException(nameof(ctx.Request));

        byte[]? bodyBytes = null;

        // Enable buffering so the body can be read multiple times
        ctx.Request.EnableBuffering();

        // Read the body
        using (var ms = new MemoryStream())
        {
            await ctx.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            bodyBytes = ms.ToArray();
            ctx.Request.Body.Position = 0; // Reset stream for further middleware
        }

        // Extract headers
        var headers = ctx.Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        // Extract query parameters
        var query = ctx.Request.Query
            .ToDictionary(q => q.Key, q => q.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        // Extract cookies
        var cookies = ctx.Request.Cookies
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        // Build base URL
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

        // Build route/path
        var route = ctx.Request.Path.ToString();

        return new FortitudeRequest
        {
            RequestId = requestId ?? Guid.NewGuid(),
            Method = ctx.Request.Method,
            BaseUrl = baseUrl,
            Route = route,
            Headers = headers,
            Query = query,
            Cookies = cookies,
            Body = bodyBytes
        };
    }
}