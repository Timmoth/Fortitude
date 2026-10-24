using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fortitude.Client;

/// <summary>
///     Converts a cURL command and a sample JSON response into the two halves of a Fortitude
///     handler definition.
/// </summary>
/// <remarks>
///     Both halves are produced with a YAML serializer rather than string concatenation, so
///     values containing YAML syntax — colons, leading dashes, <c>#</c>, newlines — are quoted
///     and escaped correctly and the result always loads.
/// </remarks>
public static class FortitudeYamlGenerator
{
    /// <summary>Headers that vary per run and should never be baked into a handler.</summary>
    private static readonly string[] VolatileHeaders = ["authorization", "cookie", "content-length", "host"];

    private static ISerializer Serializer => new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    ///     Builds the <c>match:</c> half of a handler from a cURL command.
    /// </summary>
    /// <param name="curl">A cURL command, as copied from a terminal or a browser's network tab.</param>
    /// <exception cref="InvalidOperationException">Thrown when no URL can be found.</exception>
    public static string CurlToMatchYaml(string curl)
    {
        if (string.IsNullOrWhiteSpace(curl))
            throw new InvalidOperationException("Could not find a URL in the cURL command.");

        var tokens = Tokenize(curl);

        string? method = null;
        var hasBody = false;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Uri? uri = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // -X POST, --request POST, and the glued -XPOST form.
            if (token is "-X" or "--request")
            {
                if (i + 1 < tokens.Count) method = tokens[++i];
                continue;
            }

            if (token.StartsWith("-X", StringComparison.Ordinal) && token.Length > 2)
            {
                method = token[2..];
                continue;
            }

            if (token is "-H" or "--header")
            {
                if (i + 1 < tokens.Count) AddHeader(headers, tokens[++i]);
                continue;
            }

            if (token.StartsWith("-H", StringComparison.Ordinal) && token.Length > 2)
            {
                AddHeader(headers, token[2..]);
                continue;
            }

            if (token is "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-urlencode" or "--form" or "-F")
            {
                hasBody = true;
                if (i + 1 < tokens.Count) i++;
                continue;
            }

            if (token.StartsWith('-'))
                continue;

            if (Uri.TryCreate(token, UriKind.Absolute, out var candidate)
                && candidate.Scheme is "http" or "https")
                uri = candidate;
        }

        if (uri == null)
            throw new InvalidOperationException("Could not find a URL in the cURL command.");

        var match = new Dictionary<string, object>
        {
            ["methods"] = new List<string> { (method ?? (hasBody ? "POST" : "GET")).ToUpperInvariant() },
            ["route"] = uri.AbsolutePath
        };

        if (headers.Count > 0)
            match["headers"] = headers;

        var query = ParseQuery(uri);
        if (query.Count > 0)
            match["query"] = query;

        return Serializer.Serialize(new Dictionary<string, object> { ["match"] = match });
    }

    /// <summary>
    ///     Builds the <c>response:</c> half of a handler from a sample JSON body.
    /// </summary>
    /// <param name="json">The response body to replay.</param>
    /// <param name="status">Status code to return. Defaults to 200.</param>
    /// <exception cref="JsonException">Thrown when <paramref name="json" /> is not valid JSON.</exception>
    public static string JsonToResponseYaml(string json, int status = 200)
    {
        using var document = JsonDocument.Parse(json);

        var response = new Dictionary<string, object>
        {
            ["status"] = status,
            ["body"] = new Dictionary<string, object?>
            {
                ["json"] = ToPlainObject(document.RootElement)
            }
        };

        return Serializer.Serialize(new Dictionary<string, object> { ["response"] = response });
    }

    /// <summary>
    ///     Builds a complete, loadable handler definition file from a cURL command and a
    ///     sample JSON response.
    /// </summary>
    public static string CurlToHandlerYaml(string curl, string json, int status = 200)
    {
        var match = CurlToMatchYaml(curl);
        var response = JsonToResponseYaml(json, status);

        var document = new StringBuilder();
        document.AppendLine("handlers:");
        document.Append(Indent(match, "  - ", "    "));
        document.Append(Indent(response, "    ", "    "));

        return document.ToString();
    }

    /// <summary>
    ///     Indents a YAML fragment, giving the first line its own prefix so it can carry the
    ///     sequence dash.
    /// </summary>
    private static string Indent(string yaml, string firstLinePrefix, string subsequentPrefix)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        var builder = new StringBuilder();

        for (var i = 0; i < lines.Count; i++)
            builder.AppendLine((i == 0 ? firstLinePrefix : subsequentPrefix) + lines[i]);

        return builder.ToString();
    }

    private static void AddHeader(IDictionary<string, string> headers, string raw)
    {
        var separator = raw.IndexOf(':');
        if (separator <= 0) return;

        var name = raw[..separator].Trim().ToLowerInvariant();
        var value = raw[(separator + 1)..].Trim();

        if (VolatileHeaders.Contains(name))
            return;

        headers[name] = value;
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');

            if (separator < 0)
                query[Uri.UnescapeDataString(pair)] = string.Empty;
            else
                query[Uri.UnescapeDataString(pair[..separator])] =
                    Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return query;
    }

    /// <summary>
    ///     Converts a <see cref="JsonElement" /> into the dictionaries, lists and scalars the
    ///     YAML serializer understands.
    /// </summary>
    private static object? ToPlainObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToPlainObject(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ToPlainObject).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>
    ///     Splits a shell command into arguments, honouring single and double quotes and
    ///     backslash line continuations.
    /// </summary>
    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var any = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    any = true;
                    continue;

                // A backslash before a newline continues the command; before anything else it
                // escapes that character.
                case '\\' when i + 1 < command.Length:
                    var next = command[i + 1];
                    if (next is '\n' or '\r') { i++; continue; }
                    current.Append(next);
                    i++;
                    any = true;
                    continue;

                case ' ' or '\t' or '\n' or '\r':
                    if (current.Length > 0 || any) tokens.Add(current.ToString());
                    current.Clear();
                    any = false;
                    continue;

                default:
                    current.Append(c);
                    any = true;
                    continue;
            }
        }

        if (current.Length > 0 || any) tokens.Add(current.ToString());

        return tokens;
    }
}
