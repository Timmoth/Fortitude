using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class FortitudeYamlGenerator
{

    public static string CurlToMatchYaml(string curl)
    {
        var method = "GET";
        if (curl.Contains("--data-raw") || curl.Contains("-d "))
            method = "POST";

        var methodMatch = Regex.Match(curl, @"-X\s+(\w+)");
        if (methodMatch.Success)
            method = methodMatch.Groups[1].Value;

        var uri = ExtractUrl(curl)
                  ?? throw new InvalidOperationException("Could not find URL in curl");

        var headers = Regex.Matches(curl, @"--header\s+'([^:]+):\s*([^']+)'")
            .Where(h => !h.Groups[1].Value.StartsWith("Authorization", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                h => h.Groups[1].Value.ToLowerInvariant(),
                h => h.Groups[2].Value
            );
        
        var sb = new StringBuilder();
        sb.AppendLine("match:");
        sb.AppendLine($"  methods: [{method}]");
        sb.AppendLine($"  route: {uri.AbsolutePath}");

        if (headers.Any())
        {
            sb.AppendLine("  headers:");
            foreach (var h in headers)
                sb.AppendLine($"    {h.Key}: {h.Value}");
        }

        return sb.ToString();
    }
    public static string JsonToResponseYaml(string json, int status = 200)
    {
        using var doc = JsonDocument.Parse(json);

        var sb = new StringBuilder();
        sb.AppendLine("response:");
        sb.AppendLine($"  status: {status}");
        sb.AppendLine("  body:");
        sb.AppendLine("    json:");

        AppendJson(sb, doc.RootElement, 6);

        return sb.ToString();
    }

    private static void AppendJson(StringBuilder sb, JsonElement element, int indent)
    {
        var pad = new string(' ', indent);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    sb.AppendLine($"{pad}{prop.Name}:");
                    AppendJson(sb, prop.Value, indent + 2);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    sb.AppendLine($"{pad}-");
                    AppendJson(sb, item, indent + 2);
                }
                break;

            default:
                sb.AppendLine($"{pad}{element.ToString()}");
                break;
        }
    }
    
    private static Uri? ExtractUrl(string curl)
    {
        // Match quoted or unquoted URLs
        var matches = Regex.Matches(
            curl,
            @"(https?://[^\s""']+)",
            RegexOptions.IgnoreCase);

        if (matches.Count == 0)
            return null;

        // Curl URLs are conventionally the LAST URL in the command
        var url = matches[^1].Value;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }
}
