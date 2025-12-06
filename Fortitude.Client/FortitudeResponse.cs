using System.Text.Json.Serialization;

namespace Fortitude.Client
{
    /// <summary>
    /// Represents a response to a Fortitude request.
    /// </summary>
    public class FortitudeResponse
    {
        /// <summary>
        /// The unique identifier of the request this response is associated with.
        /// </summary>
        [JsonPropertyName("requestId")]
        public Guid RequestId { get; set; }

        /// <summary>
        /// The HTTP-like status code of the response.
        /// </summary>
        [JsonPropertyName("status")]
        public int Status { get; set; }

        /// <summary>
        /// Optional headers for the response.
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Optional response body as a string.
        /// </summary>
        [JsonPropertyName("body")]
        public string? Body { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeResponse"/> with default values.
        /// </summary>
        public FortitudeResponse()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeResponse"/> with a request ID and default success status.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        public FortitudeResponse(Guid requestId)
            : this(requestId, 200)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeResponse"/> with specified values.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="status">The status code.</param>
        /// <param name="headers">Optional headers.</param>
        /// <param name="body">Optional body.</param>
        public FortitudeResponse(Guid requestId, int status = 200, Dictionary<string, string>? headers = null, string? body = null)
        {
            RequestId = requestId;
            Status = status;
            Headers = headers ?? new Dictionary<string, string>();
            Body = body ?? string.Empty;
        }

        /// <summary>
        /// Creates a 404 Not Found response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        public static FortitudeResponse NotFound(Guid requestId) =>
            new FortitudeResponse(requestId, 404, body: "Not Found");

        /// <summary>
        /// Creates a 501 Method Not Implemented response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        public static FortitudeResponse MethodNotImplemented(Guid requestId) =>
            new FortitudeResponse(requestId, 501, body: "Method Not Implemented");

        /// <summary>
        /// Creates a 400 Bad Request response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="message">Optional error message.</param>
        public static FortitudeResponse BadRequest(Guid requestId, string? message = null) =>
            new FortitudeResponse(requestId, 400, body: message ?? "Bad Request");

        /// <summary>
        /// Creates a 500 Internal Server Error response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="message">Optional error message.</param>
        public static FortitudeResponse InternalServerError(Guid requestId, string? message = null) =>
            new FortitudeResponse(requestId, 500, body: message ?? "Internal Server Error");

        /// <summary>
        /// Creates a 403 Forbidden response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="message">Optional error message.</param>
        public static FortitudeResponse Forbidden(Guid requestId, string? message = null) =>
            new FortitudeResponse(requestId, 403, body: message ?? "Forbidden");

        /// <summary>
        /// Creates a 401 Unauthorized response.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="message">Optional error message.</param>
        public static FortitudeResponse Unauthorized(Guid requestId, string? message = null) =>
            new FortitudeResponse(requestId, 401, body: message ?? "Unauthorized");

        /// <summary>
        /// Creates a 200 OK response with optional content.
        /// </summary>
        /// <param name="requestId">The request ID.</param>
        /// <param name="body">Optional response body.</param>
        /// <param name="headers">Optional headers.</param>
        public static FortitudeResponse Ok(Guid requestId, string? body = null, Dictionary<string, string>? headers = null) =>
            new FortitudeResponse(requestId, 200, headers, body ?? string.Empty);
    }
}
