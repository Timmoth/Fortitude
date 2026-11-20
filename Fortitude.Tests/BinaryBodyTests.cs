using System.Text;
using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     Bodies that are not text: recording them, defining them in YAML, and serving them back
///     byte for byte.
/// </summary>
/// <remarks>
///     Everything here used to pass through <c>Encoding.UTF8.GetString</c>, which substitutes
///     U+FFFD rather than failing. A recorded image came back as a string of replacement
///     characters, and replaying the recording served those characters instead of the image.
/// </remarks>
public class BinaryBodyTests
{
    /// <summary>A one pixel GIF: short, genuinely binary, and not valid UTF-8.</summary>
    private static readonly byte[] Gif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00,
        0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
        0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
    ];

    private static FortitudeRequest Request(string method = "GET", string route = "/logo.gif") => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    // ---------------------------------------------------------------- yaml definition

    [Fact]
    public async Task ABase64BodyIsServedByteForByte()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle($"""
                                                          match:
                                                            methods: [GET]
                                                            route: /logo.gif
                                                          response:
                                                            status: 200
                                                            body:
                                                              base64: {Convert.ToBase64String(Gif)}
                                                              contentType: image/gif
                                                          """);

        var response = await handler.HandleRequestAsync(Request());

        Assert.Equal(Gif, response.Body);
        Assert.Equal("image/gif", response.ContentType);
    }

    [Fact]
    public async Task ABase64BodyWithoutAContentTypeIsServedAsOctetStream()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle($"""
                                                          match:
                                                            methods: [GET]
                                                            route: /logo.gif
                                                          response:
                                                            status: 200
                                                            body:
                                                              base64: {Convert.ToBase64String(Gif)}
                                                          """);

        var response = await handler.HandleRequestAsync(Request());

        Assert.Equal("application/octet-stream", response.ContentType);
    }

    [Fact]
    public async Task ABase64BodyTakesPrecedenceOverText()
    {
        var handler = FortitudeYamlLoader.FromYamlSingle($"""
                                                          match:
                                                            methods: [GET]
                                                            route: /logo.gif
                                                          response:
                                                            status: 200
                                                            body:
                                                              base64: {Convert.ToBase64String(Gif)}
                                                              text: should not win
                                                          """);

        var response = await handler.HandleRequestAsync(Request());

        Assert.Equal(Gif, response.Body);
    }

    [Fact]
    public void MalformedBase64IsReportedWhenTheDefinitionLoads()
    {
        // Not at the moment the first request arrives, where it would surface as an opaque
        // 500 from a handler that looked fine at startup.
        var error = Assert.ThrowsAny<Exception>(() => FortitudeYamlLoader.FromYamlSingle("""
                                                                                         match:
                                                                                           methods: [GET]
                                                                                           route: /logo.gif
                                                                                         response:
                                                                                           status: 200
                                                                                           body:
                                                                                             base64: "not ** base64"
                                                                                         """));

        Assert.Contains("base64", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- recording

    [Fact]
    public void ARecordedBinaryResponseIsWrittenAsBase64()
    {
        var response = new FortitudeResponse(Guid.NewGuid())
        {
            Status = 200,
            Body = Gif,
            ContentType = "image/gif"
        };

        var yaml = FortitudeYamlGenerator.ExchangeToHandlerYaml(Request(), response);

        Assert.Contains("base64:", yaml);
        Assert.Contains(Convert.ToBase64String(Gif), yaml);
        Assert.DoesNotContain("text:", yaml);
    }

    [Fact]
    public async Task ARecordedBinaryResponseReplaysTheOriginalBytes()
    {
        // The whole point of record and replay: what comes back off disk is what was
        // captured, not a lossy transcription of it.
        var recorded = new FortitudeResponse(Guid.NewGuid())
        {
            Status = 200,
            Body = Gif,
            ContentType = "image/gif"
        };

        var yaml = FortitudeYamlGenerator.ExchangeToHandlerYaml(Request(), recorded);
        var handler = FortitudeYamlLoader.FromYamlMulti(yaml).Single();

        var replayed = await handler.HandleRequestAsync(Request());

        Assert.Equal(Gif, replayed.Body);
        Assert.Equal("image/gif", replayed.ContentType);
    }

    [Fact]
    public void ARecordedTextResponseIsStillWrittenAsText()
    {
        var response = new FortitudeResponse(Guid.NewGuid())
        {
            Status = 200,
            Body = Encoding.UTF8.GetBytes("plain words"),
            ContentType = "text/plain"
        };

        var yaml = FortitudeYamlGenerator.ExchangeToHandlerYaml(Request(), response);

        Assert.Contains("plain words", yaml);
        Assert.DoesNotContain("base64:", yaml);
    }

    [Fact]
    public void ARecordedJsonResponseIsStillWrittenAsStructuredJson()
    {
        var response = new FortitudeResponse(Guid.NewGuid())
        {
            Status = 200,
            Body = Encoding.UTF8.GetBytes("""{"id":7}"""),
            ContentType = "application/json"
        };

        var yaml = FortitudeYamlGenerator.ExchangeToHandlerYaml(Request(), response);

        Assert.Contains("json:", yaml);
        Assert.DoesNotContain("base64:", yaml);
    }
}
