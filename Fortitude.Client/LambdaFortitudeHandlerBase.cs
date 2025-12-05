namespace Fortitude.Client;
public class LambdaFortitudeHandlerBase : FortitudeHandlerBase
{
    private readonly HashSet<string> _methods;
    private readonly string? _route;
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _queryParams;
    private readonly Func<string?, bool>? _bodyPredicate;
    private readonly Func<FortitudeRequest, Task<FortitudeResponse>> _asyncResponder;

    public LambdaFortitudeHandlerBase(IEnumerable<string> methods,
        string? route,
        Dictionary<string, string> headers,
        Dictionary<string, string> queryParams,
        Func<string?, bool>? bodyPredicate,
        Func<FortitudeRequest, Task<FortitudeResponse>> asyncResponder)
    {
        _route = route;
        _headers = headers;
        _queryParams = queryParams;
        _bodyPredicate = bodyPredicate;
        _asyncResponder = asyncResponder;
        _methods = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase);
    }

    public LambdaFortitudeHandlerBase(IEnumerable<string> methods,
        string? route,
        Dictionary<string, string> headers,
        Dictionary<string, string> queryParams,
        Func<string?, bool>? bodyPredicate,
        Func<FortitudeRequest, FortitudeResponse> responder)
    {
        _route = route;
        _headers = headers;
        _queryParams = queryParams;
        _bodyPredicate = bodyPredicate;
        _asyncResponder = req => Task.FromResult(responder(req));

        _methods = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase);
    }
    public override bool Matches(FortitudeRequest req)
    {
        if (_methods.Any() && !_methods.Contains(req.Method))
            return false;

        if (_route != null && !_route.Equals(req.Route, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var header in _headers)
        {
            if (!req.Headers.TryGetValue(header.Key, out var values) || !values.Contains(header.Value))
                return false;
        }

        foreach (var qp in _queryParams)
        {
            if (!req.Query.TryGetValue(qp.Key, out var values) || !values.Contains(qp.Value))
                return false;
        }


        if (_bodyPredicate != null && !_bodyPredicate(req.Body))
            return false;

        return true;
    }

    public override Task<FortitudeResponse> BuildResponse(FortitudeRequest req)
    {
        return _asyncResponder(req);
    }
}