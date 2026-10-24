using System.Globalization;
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
    
    /// <summary>
    ///     Walks a deserialized response body, rendering templates and normalising the result
    ///     into shapes <see cref="JsonSerializer" /> can emit.
    /// </summary>
    /// <remarks>
    ///     YamlDotNet produces <c>Dictionary&lt;object, object&gt;</c> for nested mappings while
    ///     the top level is typed as <c>Dictionary&lt;string, object&gt;</c>, so both have to be
    ///     handled or templates below the first level silently fail to render.
    /// </remarks>
    private static object? RenderJsonTemplates(object? value, TemplateContext ctx)
    {
        switch (value)
        {
            case null:
                return null;

            case string s:
                return CoerceScalar(RenderTemplate(s, ctx));

            case IDictionary<object, object?> objectKeyed:
                return TryReadBareTemplate(objectKeyed, out var bare)
                    ? CoerceScalar(RenderTemplate(bare, ctx))
                    : objectKeyed.ToDictionary(
                        kvp => kvp.Key?.ToString() ?? string.Empty,
                        kvp => RenderJsonTemplates(kvp.Value, ctx));

            case IDictionary<string, object?> stringKeyed:
                return stringKeyed.ToDictionary(
                    kvp => kvp.Key,
                    kvp => RenderJsonTemplates(kvp.Value, ctx));

            case IEnumerable<object?> sequence:
                return sequence.Select(v => RenderJsonTemplates(v, ctx)).ToList();

            default:
                return value;
        }
    }

    /// <summary>
    ///     Recognises a template written without quotes, e.g. <c>id: {{route.id}}</c>.
    /// </summary>
    /// <remarks>
    ///     YAML reads <c>{{route.id}}</c> as a flow mapping containing a flow mapping, not as
    ///     text: a single-entry map whose value is null and whose key is itself a single-entry
    ///     map with a null value. Nothing a real response body would contain looks like that,
    ///     so it can be safely reconstructed back into the template the author intended.
    /// </remarks>
    private static bool TryReadBareTemplate(IDictionary<object, object?> map, out string template)
    {
        template = string.Empty;

        if (map.Count != 1) return false;

        var outer = map.First();
        if (outer.Value != null) return false;
        if (outer.Key is not IDictionary<object, object?> innerMap || innerMap.Count != 1) return false;

        var inner = innerMap.First();
        if (inner.Value != null) return false;
        if (inner.Key is not string expression) return false;

        template = "{{" + expression + "}}";
        return true;
    }

    /// <summary>
    ///     Converts a scalar to the JSON type it reads as, so that <c>"{{route.id}}"</c>
    ///     against <c>/users/42</c> emits <c>42</c> rather than <c>"42"</c>, and a literal
    ///     <c>flag: true</c> emits a boolean rather than the string <c>"true"</c>.
    /// </summary>
    /// <remarks>
    ///     YamlDotNet hands back every untyped scalar as a string, so the author's intent has
    ///     to be inferred here. Values with a redundant leading zero (<c>01234</c>) are left as
    ///     strings: those are nearly always identifiers, postcodes or phone numbers that would
    ///     be corrupted by being read as a number.
    /// </remarks>
    private static object CoerceScalar(string value)
    {
        if (value.Length == 0)
            return value;

        if (bool.TryParse(value, out var b))
            return b;

        if (HasRedundantLeadingZero(value))
            return value;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;

        return value;
    }

    /// <summary>"01234" yes, "0" no, "0.5" no.</summary>
    private static bool HasRedundantLeadingZero(string value)
    {
        var digits = value.StartsWith('-') || value.StartsWith('+') ? value[1..] : value;

        return digits.Length > 1 && digits[0] == '0' && digits[1] != '.';
    }
    
    /// <summary>File extensions recognised as handler definitions.</summary>
    public static readonly string[] YamlExtensions = [".yaml", ".yml"];

    /// <summary>
    ///     Enumerates handler definition files under <paramref name="dir" />, recursively.
    /// </summary>
    /// <remarks>
    ///     Both <c>.yaml</c> and <c>.yml</c> are recognised. Results are ordered by path so the
    ///     last-match-wins handler precedence is deterministic across runs and platforms.
    /// </remarks>
    public static IEnumerable<string> EnumerateYamlFiles(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        return Directory
            .EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => YamlExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Loads every handler file under <paramref name="dir" />, reporting per-file outcomes.
    /// </summary>
    /// <remarks>
    ///     A file that fails to parse yields a result carrying its <see cref="YamlHandlerFile.Error" />
    ///     rather than throwing, so one malformed file cannot stop the server from starting or
    ///     hide the handlers defined in its siblings.
    /// </remarks>
    public static IReadOnlyList<YamlHandlerFile> LoadHandlerFiles(string dir)
    {
        var results = new List<YamlHandlerFile>();

        foreach (var file in EnumerateYamlFiles(dir))
        {
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var handlers = FromYamlMulti(text).ToList();
                results.Add(new YamlHandlerFile(file, handlers, null));
            }
            catch (Exception ex)
            {
                results.Add(new YamlHandlerFile(file, [], Describe(ex)));
            }
        }

        return results;
    }

    /// <summary>
    ///     Loads every handler defined under <paramref name="dir" />, skipping files that fail
    ///     to parse. Use <see cref="LoadHandlerFiles" /> when you need to surface the failures.
    /// </summary>
    public static IEnumerable<FortitudeHandler> LoadHandlers(string dir)
    {
        return LoadHandlerFiles(dir).SelectMany(f => f.Handlers);
    }

    /// <summary>Flattens a YAML parse failure into a single readable line.</summary>
    private static string Describe(Exception ex)
    {
        var message = ex.Message;

        // YamlDotNet nests the useful detail (line/column, the offending node) one level down.
        if (ex.InnerException != null)
            message += " -> " + ex.InnerException.Message;

        return message.ReplaceLineEndings(" ");
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

        // An empty or comment-only document deserializes to null.
        var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml)?.Handlers;
        if (handlerDefs == null)
            yield break;

        foreach (var handlerDef in handlerDefs)
        {
            yield return ToHandler(handlerDef);
        }
    }

    private static bool EvaluateJsonPredicate(byte[]? body, string jsExpression)
    {
        // Matching must never throw: a body that is not JSON, or not a JSON object, simply
        // does not match. Letting an exception escape turns a non-match into a 500.
        if (body == null || body.Length == 0)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var engine = new Engine(cfg => cfg.LimitRecursion(64).TimeoutInterval(TimeSpan.FromMilliseconds(50)));

            InjectJson(engine, doc.RootElement);

            var result = engine.Evaluate(jsExpression).ToObject();
            return System.Convert.ToBoolean(result);
        }
        catch
        {
            return false;
        }
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
            var renderedJson = RenderJsonTemplates(jsonBody, templateContext);

            res.Body = JsonSerializer.SerializeToUtf8Bytes(renderedJson, JsonSerializerOptions.Web);
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

/// <summary>
///     The outcome of loading one YAML handler definition file.
/// </summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Handlers">Handlers parsed from it; empty when <paramref name="Error" /> is set.</param>
/// <param name="Error">Parse failure description, or null when the file loaded cleanly.</param>
public sealed record YamlHandlerFile(string Path, IReadOnlyList<FortitudeHandler> Handlers, string? Error)
{
    /// <summary>File name without its directory.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Whether the file failed to parse.</summary>
    public bool HasError => Error != null;
}
