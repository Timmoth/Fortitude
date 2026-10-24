using System.Net;
using System.Text;

namespace Fortitude.Client;

/// <summary>
///     Forwards matched requests to a real upstream service, optionally recording each
///     exchange as a handler definition.
/// </summary>
/// <remarks>
///     The usual workflow is to point Fortitude at the real dependency once, exercise the
///     system under test, and keep the generated YAML as a fixture. From then on the tests
///     run against the recording with the real service out of the picture.
/// </remarks>
public static class FortitudeProxy
{
    /// <summary>
    ///     A client dedicated to upstream calls.
    /// </summary>
    /// <remarks>
    ///     Deliberately constructed here rather than resolved from DI: in intercept mode a
    ///     container-built <see cref="HttpClient" /> is itself intercepted, so the proxy would
    ///     call straight back into the handler that invoked it.
    /// </remarks>
    private static readonly HttpClient Upstream = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

    /// <summary>Headers that describe a specific connection and must not be forwarded.</summary>
    private static readonly string[] HopByHopHeaders =
        ["connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "te", "trailer",
         "transfer-encoding", "upgrade", "host", "content-length"];

    /// <summary>
    ///     Forwards a request to <paramref name="baseUrl" /> and copies the real response back.
    /// </summary>
    /// <param name="request">The captured request to replay upstream.</param>
    /// <param name="response">The response to populate from upstream.</param>
    /// <param name="baseUrl">Scheme and authority of the real service, e.g. <c>https://api.example.com</c>.</param>
    /// <param name="cancellationToken">Cancels the upstream call.</param>
    public static async Task ForwardAsync(
        FortitudeRequest request,
        FortitudeResponse response,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var target = new Uri($"{baseUrl.TrimEnd('/')}{request.Route}{request.RawQuery}");

        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.Method), target);

        if (request.Body is { Length: > 0 })
        {
            upstreamRequest.Content = new ByteArrayContent(request.Body);

            if (!string.IsNullOrEmpty(request.ContentType))
                upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
        }

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                continue;

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        try
        {
            using var upstreamResponse = await Upstream
                .SendAsync(upstreamRequest, cancellationToken)
                .ConfigureAwait(false);

            response.Status = (int)upstreamResponse.StatusCode;
            response.Body = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                                   ?? "application/octet-stream";

            response.ClearHeaders();

            foreach (var header in upstreamResponse.Headers.Concat(upstreamResponse.Content.Headers))
            {
                if (HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    continue;

                response.Headers[header.Key] = string.Join(", ", header.Value);
            }
        }
        catch (Exception ex)
        {
            // Surface the upstream failure as a gateway error rather than an opaque 500:
            // the mock itself is fine, the service behind it is not.
            response.Status = (int)HttpStatusCode.BadGateway;
            response.SetTextBody($"Fortitude could not reach {target}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Writes an observed exchange to <paramref name="directory" /> as a handler definition.
    /// </summary>
    /// <returns>The path of the file written.</returns>
    /// <remarks>
    ///     File names are derived from the method and route, so replaying the same call
    ///     overwrites its own recording rather than accumulating duplicates.
    /// </remarks>
    public static string Record(FortitudeRequest request, FortitudeResponse response, string directory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileNameFor(request));

        File.WriteAllText(path, FortitudeYamlGenerator.ExchangeToHandlerYaml(request, response), Encoding.UTF8);

        return path;
    }

    /// <summary>Derives a stable, filesystem-safe name from a request.</summary>
    private static string FileNameFor(FortitudeRequest request)
    {
        var route = request.Route.Trim('/');
        if (route.Length == 0) route = "root";

        var name = $"{request.Method.ToLowerInvariant()}-{route}";

        foreach (var invalid in Path.GetInvalidFileNameChars().Append('/'))
            name = name.Replace(invalid, '-');

        // Keep well clear of filesystem name limits for deep routes.
        if (name.Length > 100) name = name[..100];

        return name + ".yaml";
    }
}

/// <summary>
///     Builder methods for proxying and recording.
/// </summary>
public static class FortitudeProxyExtensions
{
    /// <summary>
    ///     Forwards matched requests to a real service and returns its response.
    /// </summary>
    /// <param name="builder">The handler being built.</param>
    /// <param name="baseUrl">Scheme and authority of the real service.</param>
    /// <param name="recordTo">
    ///     Directory to write each exchange to as a handler definition. Omit to proxy without
    ///     recording.
    /// </param>
    /// <returns>The created handler.</returns>
    /// <example>
    ///     Capture the real API once, then run against the recording:
    ///     <code>
    ///     fortitude.Accepts().RouteStartsWith("/")
    ///         .ProxiesTo("https://api.example.com", recordTo: "./fixtures");
    ///     </code>
    /// </example>
    public static FortitudeHandler ProxiesTo(
        this FortitudeHandlerBuilder builder,
        string baseUrl,
        string? recordTo = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return builder.Returns(async (request, response) =>
        {
            await FortitudeProxy.ForwardAsync(request, response, baseUrl).ConfigureAwait(false);

            // Only keep exchanges the upstream actually answered; a 502 from an unreachable
            // service is not a fixture anyone wants to replay.
            if (recordTo != null && response.Status != (int)HttpStatusCode.BadGateway)
                FortitudeProxy.Record(request, response, recordTo);
        });
    }
}
