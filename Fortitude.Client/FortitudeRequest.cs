using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Fortitude.Client;

public class FortitudeRequest
{
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = default!;

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = default!;

    [JsonPropertyName("route")]
    public string Route { get; set; } = default!;

    /// <summary>
    /// Headers as string arrays for multiple values per header
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string[]> Headers { get; set; } = new();

    /// <summary>
    /// Query parameters as string arrays
    /// </summary>
    [JsonPropertyName("query")]
    public Dictionary<string, string[]> Query { get; set; } = new();

    [JsonPropertyName("cookies")]
    public Dictionary<string, string> Cookies { get; set; } = new();

    [JsonPropertyName("body")]
    public byte[]? Body { get; set; }

    /// <summary>
    /// Builds a FortitudeRequest from an HttpContext
    /// </summary>
    public static async Task<FortitudeRequest> FromHttpContext(HttpContext ctx, Guid? requestId = null)
    {
        if (ctx.Request == null)
            throw new ArgumentNullException(nameof(ctx.Request));

        // Read body if present
        byte[]? bodyBytes = null;

        ctx.Request.EnableBuffering();

        using (var ms = new MemoryStream())
        {
            await ctx.Request.Body.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
            ctx.Request.Body.Position = 0;
        }
        
        // Convert headers to Dictionary<string,string[]>
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in ctx.Request.Headers)
        {
            headers[h.Key] = h.Value.ToArray();
        }

        // Convert query parameters to Dictionary<string,string[]>
        var query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in ctx.Request.Query)
        {
            query[q.Key] = q.Value.ToArray();
        }

        // Convert cookies
        var cookies = new Dictionary<string, string>();
        foreach (var c in ctx.Request.Cookies)
        {
            cookies[c.Key] = c.Value;
        }

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
