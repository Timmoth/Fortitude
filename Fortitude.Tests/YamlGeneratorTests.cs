using Fortitude.Client;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tests;

/// <summary>
///     Covers the cURL and JSON to YAML converters behind the live UI's "Add Handler From
///     cURL" dialog.
/// </summary>
/// <remarks>
///     Every assertion here parses the generated text back with YamlDotNet rather than
///     string-matching it: the point of the converters is to emit YAML that actually loads.
/// </remarks>
public class YamlGeneratorTests
{
    private static T Parse<T>(string yaml) =>
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<T>(yaml);

    private static MatchDefinition ParseMatch(string yaml) =>
        Parse<Dictionary<string, MatchDefinition>>(yaml)["match"];

    // ---------------------------------------------------------------- method

    [Fact]
    public void APlainCurl_IsAGet()
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml("curl https://api.example.com/users"));

        Assert.Equal(["GET"], match.Methods);
        Assert.Equal("/users", match.Route);
    }

    [Theory]
    [InlineData("-X POST", "POST")]
    [InlineData("-X delete", "DELETE")]
    [InlineData("--request PUT", "PUT")]
    [InlineData("-XPATCH", "PATCH")]
    public void AnExplicitMethod_IsHonoured(string flag, string expected)
    {
        var match = ParseMatch(
            FortitudeYamlGenerator.CurlToMatchYaml($"curl {flag} https://api.example.com/users"));

        Assert.Equal([expected], match.Methods);
    }

    [Theory]
    [InlineData("-d '{\"a\":1}'")]
    [InlineData("--data '{\"a\":1}'")]
    [InlineData("--data-raw '{\"a\":1}'")]
    [InlineData("--data-binary '{\"a\":1}'")]
    public void ABodyWithoutAnExplicitMethod_ImpliesPost(string dataFlag)
    {
        var match = ParseMatch(
            FortitudeYamlGenerator.CurlToMatchYaml($"curl {dataFlag} https://api.example.com/users"));

        Assert.Equal(["POST"], match.Methods);
    }

    [Fact]
    public void AnExplicitMethod_BeatsAnImpliedOne()
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            "curl -X PUT --data-raw '{}' https://api.example.com/users"));

        Assert.Equal(["PUT"], match.Methods);
    }

    // ---------------------------------------------------------------- headers

    [Fact]
    public void ShortFormDoubleQuotedHeaders_AreRead()
    {
        // The form browsers emit for "Copy as cURL", which was previously missed entirely.
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            """curl -H "Accept: application/json" -H "X-Tenant: acme" https://api.example.com/users"""));

        Assert.Equal("application/json", match.Headers!["accept"]);
        Assert.Equal("acme", match.Headers["x-tenant"]);
    }

    [Fact]
    public void LongFormSingleQuotedHeaders_AreRead()
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            "curl --header 'Accept: application/json' https://api.example.com/users"));

        Assert.Equal("application/json", match.Headers!["accept"]);
    }

    [Fact]
    public void AuthorizationHeaders_AreDropped()
    {
        // Credentials vary per run and should never be baked into a committed handler file.
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            """curl -H "Authorization: Bearer secret-token" -H "Accept: text/plain" https://api.example.com/users"""));

        Assert.DoesNotContain("authorization", match.Headers!.Keys);
        Assert.DoesNotContain("secret-token", FortitudeYamlGenerator.CurlToMatchYaml(
            """curl -H "Authorization: Bearer secret-token" https://api.example.com/users"""));
        Assert.Equal("text/plain", match.Headers["accept"]);
    }

    [Fact]
    public void AHeaderValueContainingAColon_SurvivesRoundTripping()
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            """curl -H "X-Origin: https://example.com:8443/path" https://api.example.com/users"""));

        Assert.Equal("https://example.com:8443/path", match.Headers!["x-origin"]);
    }

    // ---------------------------------------------------------------- url

    [Fact]
    public void QueryParameters_BecomeMatchCriteria()
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(
            "curl 'https://api.example.com/users?page=2&active=true'"));

        Assert.Equal("/users", match.Route);
        Assert.Equal("2", match.Query!["page"]);
        Assert.Equal("true", match.Query["active"]);
    }

    [Theory]
    [InlineData("curl https://api.example.com/users")]
    [InlineData("curl 'https://api.example.com/users'")]
    [InlineData("""curl "https://api.example.com/users" """)]
    [InlineData("curl --location https://api.example.com/users")]
    public void TheUrl_IsFoundInEveryCommonSpelling(string curl)
    {
        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(curl));

        Assert.Equal("/users", match.Route);
    }

    [Fact]
    public void ALineContinuation_IsHandled()
    {
        var curl = "curl -X POST \\\n  -H 'Accept: application/json' \\\n  https://api.example.com/users";

        var match = ParseMatch(FortitudeYamlGenerator.CurlToMatchYaml(curl));

        Assert.Equal(["POST"], match.Methods);
        Assert.Equal("/users", match.Route);
    }

    [Fact]
    public void ACurlWithNoUrl_IsRejectedClearly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FortitudeYamlGenerator.CurlToMatchYaml("curl -X POST"));

        Assert.Contains("URL", ex.Message);
    }

    // ---------------------------------------------------------------- json to yaml

    [Fact]
    public void ASimpleJsonObject_BecomesLoadableYaml()
    {
        var yaml = FortitudeYamlGenerator.JsonToResponseYaml("""{"id":1,"name":"Alice"}""");

        var response = Parse<Dictionary<string, ResponseDefinition>>(yaml)["response"];

        Assert.Equal(200, response.Status);
        Assert.Equal("1", response.Body!.Json!["id"].ToString());
        Assert.Equal("Alice", response.Body.Json["name"].ToString());
    }

    [Fact]
    public void AStringContainingAColon_IsQuoted()
    {
        // Regression: scalars were written raw, so any value with YAML syntax in it produced
        // a file that would not load.
        var yaml = FortitudeYamlGenerator.JsonToResponseYaml("""{"url":"https://example.com:8443/a"}""");

        var response = Parse<Dictionary<string, ResponseDefinition>>(yaml)["response"];

        Assert.Equal("https://example.com:8443/a", response.Body!.Json!["url"].ToString());
    }

    [Theory]
    [InlineData("a: b")]
    [InlineData("# not a comment")]
    [InlineData("- not a list")]
    [InlineData("line one\nline two")]
    [InlineData("trailing space ")]
    [InlineData("{braces}")]
    [InlineData("[brackets]")]
    [InlineData("@at-sign")]
    [InlineData("*anchor")]
    [InlineData("'single' and \"double\"")]
    public void StringsWithYamlSyntax_RoundTripIntact(string value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["v"] = value });

        var yaml = FortitudeYamlGenerator.JsonToResponseYaml(json);
        var response = Parse<Dictionary<string, ResponseDefinition>>(yaml)["response"];

        Assert.Equal(value, response.Body!.Json!["v"].ToString());
    }

    [Fact]
    public void NestedObjectsAndArrays_AreEmitted()
    {
        var yaml = FortitudeYamlGenerator.JsonToResponseYaml(
            """{"user":{"id":1,"tags":["a","b"]},"items":[{"n":1},{"n":2}]}""");

        // Loads without error and preserves the shape.
        var parsed = Parse<Dictionary<string, object>>(yaml);

        Assert.True(parsed.ContainsKey("response"));
        Assert.Contains("tags", yaml);
        Assert.Contains("items", yaml);
    }

    [Fact]
    public void ACustomStatus_IsUsed()
    {
        var yaml = FortitudeYamlGenerator.JsonToResponseYaml("""{"ok":true}""", status: 201);

        var response = Parse<Dictionary<string, ResponseDefinition>>(yaml)["response"];

        Assert.Equal(201, response.Status);
    }

    [Fact]
    public void AJsonArrayAtTheRoot_IsSupported()
    {
        var yaml = FortitudeYamlGenerator.JsonToResponseYaml("""[{"id":1},{"id":2}]""");

        var parsed = Parse<Dictionary<string, object>>(yaml);

        Assert.True(parsed.ContainsKey("response"));
    }

    [Fact]
    public void InvalidJson_IsRejectedClearly()
    {
        Assert.ThrowsAny<Exception>(() => FortitudeYamlGenerator.JsonToResponseYaml("not json"));
    }

    // ---------------------------------------------------------------- combined

    [Fact]
    public void AGeneratedHandler_LoadsAsAWorkingDefinition()
    {
        var match = FortitudeYamlGenerator.CurlToMatchYaml(
            """curl -X POST -H "Accept: application/json" 'https://api.example.com/users?page=1'""");
        var response = FortitudeYamlGenerator.JsonToResponseYaml("""{"id":99,"name":"Alice"}""", 201);

        var handler = FortitudeYamlLoader.FromYamlSingle(match + response);

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task TheCompleteGeneratedFile_LoadsAndServesTheSampleResponse()
    {
        // This is exactly what the live UI's cURL dialog writes to disk.
        var yaml = FortitudeYamlGenerator.CurlToHandlerYaml(
            """curl -X POST -H "Accept: application/json" 'https://api.example.com/users?page=1'""",
            """{"id":99,"name":"Alice","url":"https://example.com:8443/a"}""",
            201);

        var handlers = FortitudeYamlLoader.FromYamlMulti(yaml).ToList();

        var handler = Assert.Single(handlers);

        var request = new FortitudeRequest
        {
            Method = "POST",
            BaseUrl = "http://localhost",
            Route = "/users",
            RawQuery = "?page=1",
            Url = "http://localhost/users?page=1",
            Protocol = "HTTP/1.1",
            Query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["page"] = ["1"] },
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["accept"] = ["application/json"]
            }
        };

        Assert.True(handler.Matches(request));

        var response = await handler.HandleRequestAsync(request);
        var body = System.Text.Encoding.UTF8.GetString(response.Body!);

        Assert.Equal(201, response.Status);
        Assert.Contains("\"id\":99", body);
        Assert.Contains("\"name\":\"Alice\"", body);
        Assert.Contains("https://example.com:8443/a", body);
    }

    [Fact]
    public void TheCompleteGeneratedFile_IsValidYaml()
    {
        var yaml = FortitudeYamlGenerator.CurlToHandlerYaml(
            "curl 'https://api.example.com/things'", """{"a":"b: c","list":[1,2]}""");

        var parsed = Parse<Dictionary<string, List<object>>>(yaml);

        Assert.Single(parsed["handlers"]);
    }
}
