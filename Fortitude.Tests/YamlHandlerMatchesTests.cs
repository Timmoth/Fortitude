using System.Text;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Fortitude.Tests;

public class YamlHandlerMatchesTests(ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(GetMatchingRequests))]
    public void Matches_WithCustomPredicate_ReturnsTrue_WhenPredicateMatches(FortitudeRequest req, FortitudeHandler handler)
    {
        // Arrange
        output.WriteLine(req.ToString());
        output.WriteLine(handler.ToString());

        // Act
        var result = handler.Matches(req);

        // Assert
        Assert.True(result);
    }
    
    [Theory]
    [MemberData(nameof(GetNonMatchingRequests))]
    public void Matches_WithCustomPredicate_ReturnsFalse_WhenPredicateDoesNotMatch(FortitudeRequest req,
        FortitudeHandler handler)
    {
        // Arrange
        output.WriteLine(req.ToString());
        output.WriteLine(handler.ToString());

        // Act
        var result = handler.Matches(req);

        // Assert
        Assert.False(result);
    }

    public static IEnumerable<object[]> GetMatchingRequests()
    {
        FortitudeRequest defaultReq()
        {
            return new FortitudeRequest
            {
                Method = "GET",
                BaseUrl = "http://localhost",
                Route = "/test",
                Url = "http://localhost/test",
                Protocol = "HTTP/1.1",
                Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                Query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                Body = Array.Empty<byte>()
            };
        }

        // ---------------------------
        // Method match
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with { Method = "POST" },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [POST]
response:
  status: 201
                ")
        };

        // ---------------------------
        // Route match
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with { Route = "/users" },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [GET]
  route: /users
response:
  status: 201
                ")
        };

        // ---------------------------
        // Header exact match
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]>
                {
                    {"content-type", new[] {"application/json"}}
                }
            },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [GET]
  headers:
    content-type: application/json
response:
  status: 201
                ")
        };

        // ---------------------------
        // Header existence
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]>
                {
                    {"x-api-key", new[] {"abc"}}
                }
            },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [GET]
  headerExists: [x-api-key]
response:
  status: 201
                ")
        };

        // ---------------------------
        // Query param exact match
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with
            {
                Query = new Dictionary<string, string[]>
                {
                    {"id", new[] {"42"}}
                }
            },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [GET]
  query:
    id: '42'
response:
  status: 201
                ")
        };

        // ---------------------------
        // Query existence
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with
            {
                Query = new Dictionary<string, string[]>
                {
                    {"token", new[] {"abc"}}
                }
            },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  methods: [GET]
  queryExists: [token]
response:
  status: 201
                ")
        };

        // ---------------------------
        // Body contains
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with { Body = Encoding.UTF8.GetBytes("hello world") },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  body:
    contains: hello
response:
  status: 201
                ")
        };

        // ---------------------------
        // Body JSON predicate (simple JS-like string)
        // ---------------------------
        yield return new object[]
        {
            defaultReq() with { Body = JsonSerializer.SerializeToUtf8Bytes(new { age = 18 }) },
            FortitudeYamlLoader.FromYamlSingle(
                @"
match:
  body:
    json: age >= 18
response:
  status: 201
                ")
        };
    }
    
    public static IEnumerable<object[]> GetNonMatchingRequests()
{
    FortitudeRequest defaultReq()
    {
        return new FortitudeRequest
        {
            Method = "GET",
            BaseUrl = "http://localhost",
            Route = "/test",
            Url = "http://localhost/test",
            Protocol = "HTTP/1.1",
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            Query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            Body = Array.Empty<byte>()
        };
    }

    // ---------------------------
    // Method mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with { Method = "GET" }, // Request is GET
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [POST]
response:
  status: 201
                ")
    };

    // ---------------------------
    // Route mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with { Route = "/wrong-route" },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [GET]
  route: /users
response:
  status: 201
                ")
    };

    // ---------------------------
    // Header exact mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with
        {
            Headers = new Dictionary<string, string[]>
            {
                {"content-type", new[] {"text/plain"}}
            }
        },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [GET]
  headers:
    content-type: application/json
response:
  status: 201
                ")
    };

    // ---------------------------
    // Header existence mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with
        {
            Headers = new Dictionary<string, string[]>()
        },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [GET]
  headerExists: [x-api-key]
response:
  status: 201
                ")
    };

    // ---------------------------
    // Query param exact mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with
        {
            Query = new Dictionary<string, string[]>
            {
                {"id", new[] {"99"}}
            }
        },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [GET]
  query:
    id: '42'
response:
  status: 201
                ")
    };

    // ---------------------------
    // Query existence mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with
        {
            Query = new Dictionary<string, string[]>()
        },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  methods: [GET]
  queryExists: [token]
response:
  status: 201
                ")
    };

    // ---------------------------
    // Body contains mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with { Body = Encoding.UTF8.GetBytes("goodbye world") },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  body:
    contains: hello
response:
  status: 201
                ")
    };

    // ---------------------------
    // Body JSON predicate mismatch
    // ---------------------------
    yield return new object[]
    {
        defaultReq() with { Body = JsonSerializer.SerializeToUtf8Bytes(new { age = 16 }) },
        FortitudeYamlLoader.FromYamlSingle(
            @"
match:
  body:
    json: age >= 18
response:
  status: 201
                ")
    };
}

}
