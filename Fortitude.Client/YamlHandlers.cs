using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fortitude.Client;

public static class FortitudeYamlLoader
{
    private sealed class TemplateContext
    {
        public Dictionary<string, string> RouteValues { get; init; } = new();
        public Dictionary<string, object?> Body { get; init; } = new();
        public Dictionary<string, string> Headers { get; init; } = new();
        public Dictionary<string, string> Query { get; init; } = new();
    }

    private static Dictionary<string, string> ExtractRouteValues(
        string template,
        string actualPath)
    {
        var templateParts = template.Trim('/').Split('/');
        var pathParts = actualPath.Trim('/').Split('/');

        if (templateParts.Length != pathParts.Length)
            return new Dictionary<string, string>();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < templateParts.Length; i++)
        {
            var part = templateParts[i];

            if (!part.StartsWith("{") || !part.EndsWith("}")) continue;
            var key = part[1..^1];
            values[key] = pathParts[i];
        }

        return values;
    }

    
    private static Dictionary<string, object?> ParseJsonBody(byte[]? body)
    {
        if (body == null || body.Length == 0)
            return new();

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ToDictionary(doc.RootElement);
        }
        catch
        {
            return new();
        }
    }
    
    private static string RenderTemplate(string template, TemplateContext ctx)
    {
        return Regex.Replace(template, @"\{\{\s*(.*?)\s*\}\}", match =>
        {
            var path = match.Groups[1].Value.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (path.Length == 0) return string.Empty;

            object? current = path[0] switch
            {
                "body" => ctx.Body,
                "headers" => ctx.Headers,
                "query" => ctx.Query,
                "route" => ctx.RouteValues,
                _ => null
            };

            foreach (var segment in path.Skip(1))
            {
                current = current switch
                {
                    Dictionary<string, object?> d when d.TryGetValue(segment, out var v) => v,
                    Dictionary<string, string> d when d.TryGetValue(segment, out var v) => v,
                    _ => null
                };

                if (current == null)
                    return string.Empty;
            }

            return current?.ToString() ?? string.Empty;
        });
    }
    
    private static object? RenderJsonTemplates(object? value, TemplateContext ctx)
    {
        return value switch
        {
            string s => RenderTemplate(s, ctx),

            Dictionary<string, object> d => d.ToDictionary(
                kvp => kvp.Key,
                kvp => RenderJsonTemplates(kvp.Value, ctx)
            ),

            List<object> l => l.Select(v => RenderJsonTemplates(v, ctx)).ToList(),

            _ => value
        };
    }
    
    private static IEnumerable<string> LoadYamlFiles(string dir)
    {
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            yield return text;
        }
    }
    
    public static IEnumerable<FortitudeHandler> LoadHandlers(string dir)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        
        foreach (var yaml in LoadYamlFiles(dir))
        {
            var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml).Handlers;
            foreach (var handlerDef in handlerDefs)
            {
                yield return ToHandler(handlerDef);
            }
        }
    }
    
    public static FortitudeHandler FromYamlSingle(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var handler = deserializer.Deserialize<HandlerDefinition>(yaml);
        return ToHandler(handler);
    }
    
    public static IEnumerable<FortitudeHandler> FromYamlMulti(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml).Handlers;
        foreach (var handlerDef in handlerDefs)
        {
            yield return ToHandler(handlerDef);
        }
    }

    private static object ConvertNumber(object value)
    {
        if (value is string s && int.TryParse(s, out var i))
            return i;
        if (value is string s2 && double.TryParse(s2, out var d))
            return d;
        return value;
    }

    private static Dictionary<string, object> CoerceNumbers(Dictionary<string, object> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value switch
        {
            Dictionary<string, object> d => CoerceNumbers(d),
            List<object> l => l.Select(ConvertNumber).ToList(),
            _ => ConvertNumber(kvp.Value)
        });
    }
    
    private static bool EvaluateJsonPredicate(byte[] body, string jsExpression)
    {
        using var doc = JsonDocument.Parse(body);
        var engine = new Engine(cfg => cfg.LimitRecursion(64).TimeoutInterval(TimeSpan.FromMilliseconds(50)));

        InjectJson(engine, doc.RootElement);

        var result = engine.Evaluate(jsExpression).ToObject();
        return System.Convert.ToBoolean(result);
    }

    private static void InjectJson(Engine engine, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("JSON root must be an object");

        foreach (var prop in element.EnumerateObject())
        {
            engine.SetValue(prop.Name, Convert(prop.Value));
        }
    }

    private static object? Convert(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => ToDictionary(el),
            JsonValueKind.Array => ToArray(el),
            _ => null
        };
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = Convert(prop.Value);
        return dict;
    }

    private static object?[] ToArray(JsonElement arr)
    {
        return arr.EnumerateArray().Select(Convert).ToArray();
    }

    private static FortitudeHandler ToHandler(HandlerDefinition handler)
{
    var methods = handler.Match.Methods;
    var route = handler.Match.Route;
    var headers = handler.Match.Headers;
    var queryParams = handler.Match.Query;

    // -------------------------------
    // Body predicate (composite)
    // -------------------------------
    Func<byte[]?, bool>? bodyPredicate = null;
    var bodyMatch = handler.Match.Body;
    if (bodyMatch != null)
    {
        var predicates = new List<Func<byte[]?, bool>>();

        if (!string.IsNullOrEmpty(bodyMatch.Contains))
        {
            predicates.Add(body =>
            {
                if (body == null || body.Length == 0) return false;
                var text = Encoding.UTF8.GetString(body);
                return text.Contains(bodyMatch.Contains, StringComparison.Ordinal);
            });
        }

        if (!string.IsNullOrEmpty(bodyMatch.Json))
        {
            predicates.Add(body => EvaluateJsonPredicate(body, bodyMatch.Json));
        }

        if (predicates.Count > 0)
        {
            // combine all body predicates with AND
            bodyPredicate = body => predicates.All(p => p(body));
        }
    }

    
    // -------------------------------
    // Request-level predicate (headers + query)
    // -------------------------------
    Func<FortitudeRequest, bool>? requestPredicate = null;
    var headerExists = handler.Match.HeaderExists ?? [];
    var queryExists = handler.Match.QueryExists ?? [];

    if ((headers?.Count ?? 0) > 0 || headerExists.Count != 0 || (queryParams?.Count ?? 0) > 0 || queryExists.Any())
    {
        requestPredicate = req =>
        {
            // Headers with exact values
            if (headers != null)
            {
                foreach (var kvp in headers)
                    if (!req.Headers.TryGetValue(kvp.Key, out var value) || !value.Contains(kvp.Value))
                        return false;
            }

            // Headers existence only
            if (headerExists.Any(key => !req.Headers.ContainsKey(key)))
            {
                return false;
            }

            // Query params exact match
            if (queryParams != null)
            {
                foreach (var kvp in queryParams)
                    if (!req.Query.TryGetValue(kvp.Key, out var value) || !value.Contains(kvp.Value))
                        return false;
            }

            // Query params existence only
            return queryExists.All(key => req.Query.ContainsKey(key));
        };
    }

    // -------------------------------
    // Responder
    // -------------------------------
    void Responder(FortitudeRequest req, FortitudeResponse res)
    {
        res.Status = handler.Response.Status;

        var templateContext = new TemplateContext
        {
            Body = ParseJsonBody(req.Body),
            Headers = req.Headers.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FirstOrDefault() ?? string.Empty
            ),
            Query = req.Query.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FirstOrDefault() ?? string.Empty
            ),
            RouteValues = string.IsNullOrEmpty(handler.Match.Route) ? new Dictionary<string, string>() : ExtractRouteValues(handler.Match.Route, req.Route)
        };

        // Text body
        var bodyText = handler.Response.Body?.Text;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            var rendered = RenderTemplate(bodyText, templateContext);
            res.Body = Encoding.UTF8.GetBytes(rendered);
            res.ContentType = "text/plain; charset=utf-8";
        }

        // JSON body
        var jsonBody = handler.Response.Body?.Json;
        if (jsonBody != null)
        {
            var renderedJson =
                RenderJsonTemplates(jsonBody, templateContext);

            res.Body = JsonSerializer.SerializeToUtf8Bytes(
                CoerceNumbers((Dictionary<string, object>)renderedJson!), 
                JsonSerializerOptions.Web
            );

            res.ContentType = "application/json";
        }

        // Response headers (templated too)
        if (handler.Response.Headers != null)
        {
            res.Headers = handler.Response.Headers.ToDictionary(
                kvp => kvp.Key,
                kvp => RenderTemplate(kvp.Value, templateContext)
            );
        }
    }
    


    return new FortitudeHandler(
        methods,
        route,
        headers,
        queryParams,
        bodyPredicate,
        requestPredicate,
        (Action<FortitudeRequest, FortitudeResponse>)Responder
    );
}

    
}

public sealed record FortitudeFile
{
    public List<HandlerDefinition> Handlers { get; init; } = new();
}

public sealed record HandlerDefinition
{
    public MatchDefinition Match { get; init; } = new();
    public ResponseDefinition Response { get; init; } = new();
}

public sealed record MatchDefinition
{
    // HTTP
    public List<string>? Methods { get; init; }
    public string? Route { get; init; }

    // Headers
    public Dictionary<string, string>? Headers { get; init; }
    public List<string>? HeaderExists { get; init; }

    // Query
    public Dictionary<string, string>? Query { get; init; }
    public List<string>? QueryExists { get; init; }

    // Body
    public BodyMatchDefinition? Body { get; init; }
}

public sealed record BodyMatchDefinition
{
    public string? Contains { get; init; }
    public string? Json { get; init; }

}


public sealed record ResponseDefinition
{
    public int Status { get; init; }
    public Dictionary<string, string>? Headers { get; init; }

    public ResponseBodyDefinition? Body { get; init; }
}

public sealed record ResponseBodyDefinition
{
    public Dictionary<string, object>? Json { get; init; }
    public string? Text { get; init; }
}
