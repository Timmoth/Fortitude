using System.Text;
using System.Text.Json;

namespace Fortitude.Client;

/// <summary>
///     Provides extension methods to build request handlers for a <see cref="FortitudeClient" />.
/// </summary>
public static class FortitudeHandlerExtensions
{
    /// <summary>
    ///     Starts building a handler for the specified <see cref="FortitudeClient" />.
    /// </summary>
    /// <param name="client">The Fortitude client.</param>
    /// <returns>A <see cref="FortitudeHandlerBuilder" /> for fluent configuration.</returns>
    public static FortitudeHandlerBuilder Accepts(this FortitudeClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        return new FortitudeHandlerBuilder(client);
    }
}

/// <summary>
///     Fluent builder for defining request handlers on a <see cref="FortitudeClient" />.
/// </summary>
public class FortitudeHandlerBuilder
{
    private readonly FortitudeClient? _client;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _methods = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _queryParams = new(StringComparer.OrdinalIgnoreCase);
    private Func<byte[]?, bool>? _bodyPredicate;
    private Func<FortitudeRequest, bool>? _requestPredicate;
    private string? _route;

    /// <summary>
    ///     Initializes a new instance of <see cref="FortitudeHandlerBuilder" />.
    /// </summary>
    /// <param name="fortitudeClient">The client to attach handlers to.</param>
    public FortitudeHandlerBuilder(FortitudeClient? fortitudeClient)
    {
        _client = fortitudeClient;
    }

    #region Request predicates

    /// <summary>
    ///     Adds a request-level predicate to the handler.
    /// </summary>
    /// <param name="predicate">Predicate used to match requests.</param>
    /// <returns>The current builder instance.</returns>
    public FortitudeHandlerBuilder Matches(Func<FortitudeRequest, bool> predicate)
    {
        _requestPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    #endregion

    #region HTTP Methods

    /// <summary>
    ///     Adds an HTTP method to the handler.
    /// </summary>
    /// <param name="method">The HTTP method to match.</param>
    /// <returns>The current builder instance.</returns>
    public FortitudeHandlerBuilder Method(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));

        _methods.Add(method.ToUpperInvariant());
        return this;
    }

    /// <summary>
    ///     Matches HTTP GET requests.
    /// </summary>
    public FortitudeHandlerBuilder Get()
    {
        return Method("GET");
    }

    /// <summary>
    ///     Matches HTTP POST requests.
    /// </summary>
    public FortitudeHandlerBuilder Post()
    {
        return Method("POST");
    }

    /// <summary>
    ///     Matches HTTP PUT requests.
    /// </summary>
    public FortitudeHandlerBuilder Put()
    {
        return Method("PUT");
    }

    /// <summary>
    ///     Matches HTTP DELETE requests.
    /// </summary>
    public FortitudeHandlerBuilder Delete()
    {
        return Method("DELETE");
    }

    /// <summary>
    ///     Matches HTTP PATCH requests.
    /// </summary>
    public FortitudeHandlerBuilder Patch()
    {
        return Method("PATCH");
    }

    /// <summary>
    ///     Matches HTTP OPTIONS requests.
    /// </summary>
    public FortitudeHandlerBuilder Options()
    {
        return Method("OPTIONS");
    }

    /// <summary>
    ///     Adds multiple HTTP methods at once.
    /// </summary>
    /// <param name="methods">The HTTP methods to match.</param>
    public FortitudeHandlerBuilder Methods(params string[] methods)
    {
        if (methods == null || methods.Length == 0)
            throw new ArgumentException("Methods cannot be null or empty.", nameof(methods));

        foreach (var m in methods)
            Method(m);

        return this;
    }

    /// <summary>
    ///     Matches any HTTP method.
    /// </summary>
    public FortitudeHandlerBuilder AnyMethod()
    {
        _methods.Clear();
        return this;
    }

    #endregion

    #region Routing & Filtering

    /// <summary>
    ///     Sets the HTTP route the handler will match.
    /// </summary>
    /// <param name="route">The route to match.</param>
    public FortitudeHandlerBuilder HttpRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("Route cannot be null or empty.", nameof(route));

        _route = route;
        return this;
    }

    /// <summary>
    ///     Matches requests whose route starts with the specified prefix.
    /// </summary>
    public FortitudeHandlerBuilder RouteStartsWith(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix cannot be null or empty.", nameof(prefix));

        return Matches(r => r.Route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Matches requests whose route ends with the specified suffix.
    /// </summary>
    public FortitudeHandlerBuilder RouteEndsWith(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            throw new ArgumentException("Suffix cannot be null or empty.", nameof(suffix));

        return Matches(r => r.Route.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Header helpers

    /// <summary>
    ///     Adds a required header for the handler to match.
    /// </summary>
    public FortitudeHandlerBuilder Header(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Header key cannot be null or empty.", nameof(key));

        _headers[key] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>
    ///     Matches requests containing the specified header, regardless of value.
    /// </summary>
    public FortitudeHandlerBuilder HeaderExists(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Header key cannot be null or empty.", nameof(key));

        return Matches(req => req.Headers.ContainsKey(key));
    }

    /// <summary>
    ///     Matches requests containing the specified Authorization header.
    /// </summary>
    public FortitudeHandlerBuilder Authorization(string value)
    {
        return Header("Authorization", value);
    }

    /// <summary>
    ///     Matches requests containing a Bearer token.
    /// </summary>
    public FortitudeHandlerBuilder BearerToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));

        return Header("Authorization", $"Bearer {token}");
    }

    /// <summary>
    ///     Matches requests containing a specific Accept header.
    /// </summary>
    public FortitudeHandlerBuilder Accept(string mediaType)
    {
        return Header("Accept", mediaType);
    }

    /// <summary>
    ///     Matches requests with a specific Content-Type header.
    /// </summary>
    public FortitudeHandlerBuilder ContentType(string contentType)
    {
        return Header("Content-Type", contentType);
    }

    /// <summary>
    ///     Matches requests with a specific User-Agent header.
    /// </summary>
    public FortitudeHandlerBuilder UserAgent(string userAgent)
    {
        return Header("User-Agent", userAgent);
    }

    #endregion

    #region Query helpers

    /// <summary>
    ///     Adds a required query parameter for the handler to match.
    /// </summary>
    public FortitudeHandlerBuilder QueryParam(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Query parameter key cannot be null or empty.", nameof(key));

        _queryParams[key] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>
    ///     Matches requests containing the specified query parameter, regardless of value.
    /// </summary>
    public FortitudeHandlerBuilder QueryParamExists(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Query parameter key cannot be null or empty.", nameof(key));

        return Matches(req => req.Query.ContainsKey(key));
    }

    /// <summary>
    ///     Adds multiple required query parameters at once.
    /// </summary>
    public FortitudeHandlerBuilder QueryParams(Dictionary<string, string> parameters)
    {
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));

        foreach (var kvp in parameters)
            QueryParam(kvp.Key, kvp.Value);

        return this;
    }

    #endregion

    #region Body helpers

    /// <summary>
    ///     Adds a predicate to match the request body.
    /// </summary>
    public FortitudeHandlerBuilder Body(Func<byte[]?, bool> predicate)
    {
        _bodyPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    /// <summary>
    ///     Matches requests with no body or an empty body.
    /// </summary>
    public FortitudeHandlerBuilder BodyIsEmpty()
    {
        return Body(body => body == null || body.Length == 0);
    }

    /// <summary>
    ///     Matches requests whose body contains the specified UTF-8 text.
    /// </summary>
    public FortitudeHandlerBuilder BodyContains(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        return Body(body =>
        {
            if (body == null || body.Length == 0)
                return false;

            var content = Encoding.UTF8.GetString(body);
            return content.Contains(text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Matches requests with a JSON content type.
    /// </summary>
    public FortitudeHandlerBuilder JsonBody()
    {
        return ContentType("application/json");
    }

    /// <summary>
    ///     Matches requests whose JSON body satisfies the provided predicate.
    /// </summary>
    public FortitudeHandlerBuilder JsonBody<T>(Func<T, bool> predicate, JsonSerializerOptions? options = null)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return Body(body =>
        {
            if (body == null || body.Length == 0)
                return false;

            try
            {
                var value = JsonSerializer.Deserialize<T>(body, options ?? JsonSerializerOptions.Web);
                return value != null && predicate(value);
            }
            catch
            {
                return false;
            }
        });
    }

    #endregion

    #region Responder

    /// <summary>
    ///     Registers a synchronous responder for matched requests.
    /// </summary>
    /// <param name="responder">The action used to generate a response.</param>
    /// <returns>The created handler.</returns>
    public FortitudeHandler Returns(Action<FortitudeRequest, FortitudeResponse> responder)
    {
        if (responder == null) throw new ArgumentNullException(nameof(responder));

        var handler = new FortitudeHandler(
            _methods,
            _route,
            _headers,
            _queryParams,
            _bodyPredicate,
            _requestPredicate,
            responder
        );

        _client?.AddHandler(handler);
        return handler;
    }

    /// <summary>
    ///     Registers an asynchronous responder for matched requests.
    /// </summary>
    /// <param name="asyncResponder">The async function used to generate a response.</param>
    /// <returns>The created handler.</returns>
    public FortitudeHandler Returns(Func<FortitudeRequest, FortitudeResponse, Task> asyncResponder)
    {
        if (asyncResponder == null) throw new ArgumentNullException(nameof(asyncResponder));

        var handler = new FortitudeHandler(
            _methods,
            _route,
            _headers,
            _queryParams,
            _bodyPredicate,
            _requestPredicate,
            asyncResponder
        );

        _client?.AddHandler(handler);
        return handler;
    }

    #endregion
}