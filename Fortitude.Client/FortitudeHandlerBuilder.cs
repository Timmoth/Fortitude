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
    private TimeSpan _delay;
    private TimeSpan? _delayMax;
    private int? _maxCalls;
    private int _priority;
    private double _failureProbability;
    private Action<FortitudeRequest, FortitudeResponse>? _onFailure;
    private string? _scenario;
    private string? _requiredState;
    private string? _nextState;
    private FortitudeScenarios? _scenarios;

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

    #region Scenarios

    /// <summary>
    ///     Places the handler in a named scenario, so it can require and advance a state.
    /// </summary>
    /// <param name="name">The scenario's name.</param>
    /// <param name="scenarios">
    ///     The state machine to use. Defaults to the client's, or a standalone one when the
    ///     handler is built without a client.
    /// </param>
    public FortitudeHandlerBuilder InScenario(string name, FortitudeScenarios? scenarios = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Scenario name cannot be null or empty.", nameof(name));

        _scenario = name;
        _scenarios = scenarios ?? _client?.Scenarios ?? new FortitudeScenarios();
        return this;
    }

    /// <summary>
    ///     Restricts the handler to matching only while the scenario is in this state.
    /// </summary>
    /// <remarks>
    ///     Scenarios begin in <see cref="FortitudeScenarios.InitialState" />.
    /// </remarks>
    public FortitudeHandlerBuilder WhenState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be null or empty.", nameof(state));

        _requiredState = state;
        return this;
    }

    /// <summary>
    ///     Moves the scenario to this state once the handler has answered.
    /// </summary>
    public FortitudeHandlerBuilder ThenState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be null or empty.", nameof(state));

        _nextState = state;
        return this;
    }

    #endregion

    #region Call limits and precedence

    /// <summary>
    ///     Limits the handler to answering <paramref name="count" /> requests, after which it
    ///     stops matching and a later-registered handler can take over.
    /// </summary>
    /// <remarks>
    ///     This is how retry behaviour is expressed: register a failing handler with
    ///     <c>Times(2)</c> and a succeeding one after it.
    /// </remarks>
    public FortitudeHandlerBuilder Times(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Call count must be greater than zero.");

        _maxCalls = count;
        return this;
    }

    /// <summary>Limits the handler to a single request.</summary>
    public FortitudeHandlerBuilder Once() => Times(1);

    /// <summary>Limits the handler to two requests.</summary>
    public FortitudeHandlerBuilder Twice() => Times(2);

    /// <summary>
    ///     Sets match precedence. Higher wins; the default is 0, where the most recently
    ///     registered matching handler answers.
    /// </summary>
    public FortitudeHandlerBuilder Priority(int priority)
    {
        _priority = priority;
        return this;
    }

    #endregion

    #region Faults

    /// <summary>
    ///     Returns a transport-level failure for every matching request.
    /// </summary>
    /// <param name="fault">The failure to simulate.</param>
    public FortitudeHandler ReturnsFault(HttpFault fault)
    {
        return Returns((_, res) => res.WithFault(fault));
    }

    /// <summary>
    ///     Fails a proportion of matching requests, passing the rest to the normal responder.
    /// </summary>
    /// <param name="probability">Chance of failure, between 0 and 1.</param>
    /// <param name="onFailure">How to fail. Defaults to a 500.</param>
    /// <remarks>
    ///     Call before <see cref="Returns(Action{FortitudeRequest, FortitudeResponse})" />:
    ///     <code>
    ///     fortitude.Accepts().Get().HttpRoute("/flaky")
    ///         .FailsRandomly(0.2)
    ///         .Returns((req, res) => res.Ok(data));
    ///     </code>
    /// </remarks>
    public FortitudeHandlerBuilder FailsRandomly(
        double probability,
        Action<FortitudeRequest, FortitudeResponse>? onFailure = null)
    {
        if (probability is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0 and 1.");

        _failureProbability = probability;
        _onFailure = onFailure ?? ((_, res) => res.InternalServerError());
        return this;
    }

    #endregion

    #region Latency

    /// <summary>
    ///     Waits before responding, simulating a slow dependency.
    /// </summary>
    /// <param name="delay">How long to wait.</param>
    /// <remarks>
    ///     Useful for exercising timeouts, retries and circuit breakers in the system under
    ///     test. A delay longer than the server's <c>Settings:ResponseTimeoutSeconds</c>
    ///     (5 seconds by default) is cut short by a 504.
    /// </remarks>
    public FortitudeHandlerBuilder WithDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");

        _delay = delay;
        _delayMax = null;
        return this;
    }

    /// <summary>
    ///     Waits <paramref name="milliseconds" /> before responding.
    /// </summary>
    public FortitudeHandlerBuilder WithDelay(int milliseconds)
    {
        return WithDelay(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>
    ///     Waits a random time between <paramref name="min" /> and <paramref name="max" />
    ///     before responding, simulating jitter.
    /// </summary>
    public FortitudeHandlerBuilder WithRandomDelay(TimeSpan min, TimeSpan max)
    {
        if (min < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(min), "Delay cannot be negative.");

        if (max < min)
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum delay cannot be less than the minimum.");

        _delay = min;
        _delayMax = max;
        return this;
    }

    /// <summary>
    ///     Waits a random number of milliseconds between <paramref name="minMs" /> and
    ///     <paramref name="maxMs" /> before responding.
    /// </summary>
    public FortitudeHandlerBuilder WithRandomDelay(int minMs, int maxMs)
    {
        return WithRandomDelay(TimeSpan.FromMilliseconds(minMs), TimeSpan.FromMilliseconds(maxMs));
    }

    #endregion

    #region Responder

    /// <summary>
    ///     Returns a different response to each successive request, with the final one
    ///     repeating once the sequence is exhausted.
    /// </summary>
    /// <param name="responders">The responses to return, in order. At least one is required.</param>
    /// <example>
    ///     Fail twice, then succeed:
    ///     <code>
    ///     fortitude.Accepts().Get().HttpRoute("/flaky").ReturnsInSequence(
    ///         (_, res) => res.InternalServerError(),
    ///         (_, res) => res.InternalServerError(),
    ///         (_, res) => res.Ok("recovered"));
    ///     </code>
    /// </example>
    public FortitudeHandler ReturnsInSequence(params Action<FortitudeRequest, FortitudeResponse>[] responders)
    {
        if (responders == null || responders.Length == 0)
            throw new ArgumentException("At least one responder is required.", nameof(responders));

        if (responders.Any(r => r == null))
            throw new ArgumentException("Responders cannot be null.", nameof(responders));

        var index = -1;

        return Returns((req, res) =>
        {
            // Advance per request, clamping to the last entry so the sequence has a
            // well-defined steady state rather than running out.
            var position = Math.Min(Interlocked.Increment(ref index), responders.Length - 1);
            responders[position](req, res);
        });
    }


    /// <summary>
    ///     Registers a synchronous responder for matched requests.
    /// </summary>
    /// <param name="responder">The action used to generate a response.</param>
    /// <returns>The created handler.</returns>
    public FortitudeHandler Returns(Action<FortitudeRequest, FortitudeResponse> responder)
    {
        if (responder == null) throw new ArgumentNullException(nameof(responder));

        responder = WrapWithRandomFailure(responder);

        var handler = new FortitudeHandler(
            _methods,
            _route,
            _headers,
            _queryParams,
            _bodyPredicate,
            _requestPredicate,
            responder
        )
        {
            Delay = _delay,
            DelayMax = _delayMax,
            MaxCalls = _maxCalls,
            Priority = _priority,
            Scenario = BuildScenario()
        };

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
        )
        {
            Delay = _delay,
            DelayMax = _delayMax,
            MaxCalls = _maxCalls,
            Priority = _priority,
            Scenario = BuildScenario()
        };

        _client?.AddHandler(handler);
        return handler;
    }

    /// <summary>
    ///     Builds the scenario binding, if this handler declared one.
    /// </summary>
    private ScenarioBinding? BuildScenario()
    {
        if (_scenario == null) return null;

        if (_requiredState == null && _nextState == null)
            throw new InvalidOperationException(
                $"Scenario '{_scenario}' declares neither WhenState nor ThenState, so it has no effect.");

        return new ScenarioBinding(
            _scenarios ?? new FortitudeScenarios(), _scenario, _requiredState, _nextState);
    }

    /// <summary>
    ///     Wraps a responder so a configured proportion of calls fail instead.
    /// </summary>
    private Action<FortitudeRequest, FortitudeResponse> WrapWithRandomFailure(
        Action<FortitudeRequest, FortitudeResponse> responder)
    {
        if (_failureProbability <= 0 || _onFailure == null)
            return responder;

        var probability = _failureProbability;
        var onFailure = _onFailure;

        return (req, res) =>
        {
            if (Random.Shared.NextDouble() < probability)
                onFailure(req, res);
            else
                responder(req, res);
        };
    }

    #endregion
}