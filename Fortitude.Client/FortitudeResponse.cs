using System.Text.Json.Serialization;

namespace Fortitude.Client;

public class FortitudeResponse
{
    [JsonPropertyName("requestId")]
    public Guid RequestId { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    public FortitudeResponse()
    {

    }
    
    public FortitudeResponse(Guid requestId)
    {
        RequestId = requestId;
        Status = 200;
        Headers = new Dictionary<string, string>();
        Body = string.Empty;
    }

    public FortitudeResponse(Guid requestId, int status = 200, Dictionary<string, string>? headers = null, string? body = null)
    {
        RequestId = requestId;
        Status = status;
        Headers = headers ?? new Dictionary<string, string>();
        Body = body ?? string.Empty;
    }

    /// <summary>
    /// Creates a default 404 Not Found response
    /// </summary>
    public static FortitudeResponse NotFound(Guid requestId)
    {
        return new FortitudeResponse(
            requestId,
            status: 404,
            headers: new Dictionary<string, string>(),
            body: "Not Found"
        );
    }
}