using System.Text;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Fortitude.Tests;

public class HandlerMatchesTests(ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(GetMatchingRequests))]
    public void Matches_WithCustomPredicate_ReturnsTrue_WhenPredicateMatches(FortitudeRequest req,
        FortitudeHandler handler)
    {
        // Arrange

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
                Protocol = "HTTP/1.1"
            };
        }

        // -------------------------------------------------------
        // METHOD MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Method = "GET" },
            FortitudeHandler.Accepts()
                .Get()
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "POST" },
            FortitudeHandler.Accepts()
                .Post()
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "PUT" },
            FortitudeHandler.Accepts()
                .Put()
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "DELETE" },
            FortitudeHandler.Accepts()
                .Delete()
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "PATCH" },
            FortitudeHandler.Accepts()
                .Patch()
                .Returns((req, res) => { })
        ];

        // Multiple allowed methods
        yield return
        [
            defaultReq() with { Method = "POST" },
            FortitudeHandler.Accepts()
                .Methods("GET", "POST")
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // ROUTE MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Route = "/abc", Url = "http://localhost/abc" },
            FortitudeHandler.Accepts()
                .HttpRoute("/abc")
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Route = "/users/42", Url = "http://localhost/users/42" },
            FortitudeHandler.Accepts()
                .HttpRoute("/users/42")
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // HEADER MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]>
                {
                    ["X-Test"] = new[] { "123" }
                }
            },
            FortitudeHandler.Accepts()
                .Header("X-Test", "123")
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]>
                {
                    ["Authorization"] = new[] { "Bearer abc" }
                }
            },
            FortitudeHandler.Accepts()
                .Header("Authorization", "Bearer abc")
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // QUERY PARAM MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with
            {
                RawQuery = "?id=10",
                Url = "http://localhost/test?id=10",
                Query = new Dictionary<string, string[]>
                {
                    ["id"] = new[] { "10" }
                }
            },
            FortitudeHandler.Accepts()
                .QueryParam("id", "10")
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                RawQuery = "?a=1&b=2",
                Url = "http://localhost/test?a=1&b=2",
                Query = new Dictionary<string, string[]>
                {
                    ["a"] = new[] { "1" },
                    ["b"] = new[] { "2" }
                }
            },
            FortitudeHandler.Accepts()
                .QueryParam("a", "1")
                .QueryParam("b", "2")
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // BODY PREDICATE MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with
            {
                Body = Encoding.UTF8.GetBytes("hello")
            },
            FortitudeHandler.Accepts()
                .Body(body => Encoding.UTF8.GetString(body!) == "hello")
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Body = new byte[] { 1, 2, 3 }
            },
            FortitudeHandler.Accepts()
                .Body(b => b != null && b.Length == 3 && b[0] == 1)
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // FULL REQUEST PREDICATE MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Method = "GET", Route = "/special" },
            FortitudeHandler.Accepts()
                .Matches(r => r.Method == "GET" && r.Route == "/special")
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Method = "POST",
                Headers = new Dictionary<string, string[]> { ["X-Flag"] = new[] { "true" } }
            },
            FortitudeHandler.Accepts()
                .Matches(r => r.Headers.ContainsKey("X-Flag"))
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // COMPLEX COMBO MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with
            {
                Method = "PUT",
                Route = "/combo",
                Url = "http://localhost/combo?a=1",
                RawQuery = "?a=1",
                Query = new Dictionary<string, string[]>
                {
                    ["a"] = new[] { "1" }
                },
                Headers = new Dictionary<string, string[]>
                {
                    ["X-Test"] = new[] { "ok" }
                }
            },
            FortitudeHandler.Accepts()
                .Put()
                .HttpRoute("/combo")
                .Header("X-Test", "ok")
                .QueryParam("a", "1")
                .Returns((req, res) => { })
        ];
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
                Protocol = "HTTP/1.1"
            };
        }

        // -------------------------------------------------------
        // HTTP METHOD - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Method = "GET" },
            FortitudeHandler.Accepts()
                .Post() // does NOT match GET
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "DELETE" },
            FortitudeHandler.Accepts()
                .Get() // does NOT match DELETE
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "PATCH" },
            FortitudeHandler.Accepts()
                .Methods("GET", "POST") // PATCH not allowed
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // ROUTE - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Route = "/wrong", Url = "http://localhost/wrong" },
            FortitudeHandler.Accepts()
                .HttpRoute("/test") // wrong path
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Route = "/abc", Url = "http://localhost/abc" },
            FortitudeHandler.Accepts()
                .HttpRoute("/xyz") // route mismatch
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // HEADER - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq(),
            FortitudeHandler.Accepts()
                .Header("X-Unit", "true") // request has no headers
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]> { ["X-Test"] = new[] { "abc" } }
            },
            FortitudeHandler.Accepts()
                .Header("X-Test", "xyz") // wrong value
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Headers = new Dictionary<string, string[]> { ["Other"] = new[] { "1" } }
            },
            FortitudeHandler.Accepts()
                .Header("Missing", "val") // header not present
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // QUERY PARAM - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq(),
            FortitudeHandler.Accepts()
                .QueryParam("id", "123") // request has no query
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                RawQuery = "?id=10",
                Url = "http://localhost/test?id=10",
                Query = new Dictionary<string, string[]>
                {
                    ["id"] = new[] { "10" }
                }
            },
            FortitudeHandler.Accepts()
                .QueryParam("id", "999") // wrong value
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                RawQuery = "?a=1&b=2",
                Url = "http://localhost/test?a=1&b=2",
                Query = new Dictionary<string, string[]>
                {
                    ["a"] = new[] { "1" },
                    ["b"] = new[] { "2" }
                }
            },
            FortitudeHandler.Accepts()
                .QueryParam("a", "1")
                .QueryParam("b", "999") // wrong value for b
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // BODY - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq(),
            FortitudeHandler.Accepts()
                .Body(b => b != null && b.Length > 0) // body missing
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Body = Encoding.UTF8.GetBytes("different")
            },
            FortitudeHandler.Accepts()
                .Body(b => Encoding.UTF8.GetString(b!) == "expected") // mismatch
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // CUSTOM MATCHER - NON MATCHING
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with { Route = "/nope" },
            FortitudeHandler.Accepts()
                .Matches(r => r.Route == "/expected") // wrong route
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with { Method = "POST" },
            FortitudeHandler.Accepts()
                .Matches(r => r.Method == "PUT") // incorrect method
                .Returns((req, res) => { })
        ];

        // -------------------------------------------------------
        // COMPLEX COMBINATIONS THAT SHOULD NOT MATCH
        // -------------------------------------------------------

        yield return
        [
            defaultReq() with
            {
                Method = "GET",
                Route = "/combo",
                Url = "http://localhost/combo?a=1",
                RawQuery = "?a=1",
                Query = new Dictionary<string, string[]> { ["a"] = new[] { "1" } },
                Headers = new Dictionary<string, string[]> { ["X-Test"] = new[] { "ok" } }
            },
            FortitudeHandler.Accepts()
                .Post() // wrong method
                .HttpRoute("/combo") // this matches
                .Header("X-Test", "ok") // this matches
                .QueryParam("a", "1") // this matches
                .Returns((req, res) => { })
        ];

        yield return
        [
            defaultReq() with
            {
                Method = "PUT",
                Route = "/combo2",
                Url = "http://localhost/combo2?a=5",
                RawQuery = "?a=5",
                Query = new Dictionary<string, string[]> { ["a"] = new[] { "5" } }
            },
            FortitudeHandler.Accepts()
                .Put()
                .HttpRoute("/combo2")
                .QueryParam("a", "5")
                .Header("X-Missing", "true") // required header missing → no match
                .Returns((req, res) => { })
        ];
    }
}