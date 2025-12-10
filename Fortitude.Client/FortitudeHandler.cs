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
    private string? _route;
    private Func<FortitudeRequest, bool>? _requestPredicate;
    
    /// <summary>
    ///     Initializes a new instance of <see cref="FortitudeHandlerBuilder" />.
    /// </summary>
    /// <param name="fortitudeClient">The client to attach handlers to.</param>
    /// <param name="method">Optional initial HTTP method.</param>
    public FortitudeHandlerBuilder(FortitudeClient? fortitudeClient)
    {
        _client = fortitudeClient;
    }
    
    #region HTTP Methods

    /// <summary>
    ///     Adds a Request matching predicate to the handler.
    /// </summary>
    public FortitudeHandlerBuilder Matches(Func<FortitudeRequest, bool> predicate)
    {
        _requestPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    
    /// <summary>
    ///     Adds an HTTP method to the handler.
    /// </summary>
    public FortitudeHandlerBuilder Method(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));

        _methods.Add(method.ToUpperInvariant());
        return this;
    }

    public FortitudeHandlerBuilder Get()
    {
        return Method("GET");
    }

    public FortitudeHandlerBuilder Post()
    {
        return Method("POST");
    }

    public FortitudeHandlerBuilder Put()
    {
        return Method("PUT");
    }

    public FortitudeHandlerBuilder Delete()
    {
        return Method("DELETE");
    }

    public FortitudeHandlerBuilder Patch()
    {
        return Method("PATCH");
    }

    public FortitudeHandlerBuilder Options()
    {
        return Method("OPTIONS");
    }

    /// <summary>
    ///     Adds multiple HTTP methods at once.
    /// </summary>
    public FortitudeHandlerBuilder Methods(params string[] methods)
    {
        if (methods == null || methods.Length == 0)
            throw new ArgumentException("Methods cannot be null or empty.", nameof(methods));

        foreach (var m in methods)
            _methods.Add(m.ToUpperInvariant());

        return this;
    }

    #endregion

    #region Routing & Filtering

    /// <summary>
    ///     Sets the HTTP route the handler will match.
    /// </summary>
    public FortitudeHandlerBuilder HttpRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("Route cannot be null or empty.", nameof(route));

        _route = route;
        return this;
    }

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
    ///     Adds a predicate to match the request body.
    /// </summary>
    public FortitudeHandlerBuilder Body(Func<byte[]?, bool> predicate)
    {
        _bodyPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    #endregion

    #region Responder

    /// <summary>
    ///     Registers a synchronous responder for matched requests.
    /// </summary>
    /// <param name="responder">The action to generate a response.</param>
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
    /// <param name="asyncResponder">The async function to generate a response.</param>
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