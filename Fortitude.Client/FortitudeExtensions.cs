using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Fortitude.Client;

/// <summary>
///     Provides extension methods for Fortitude clients.
/// </summary>
public static class FortitudeExtensions
{
    /// <summary>
    ///     Deserializes a byte array containing JSON into an object of type <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The JSON data as a byte array.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions" /> to use.</param>
    /// <returns>An instance of <typeparamref name="T" /> if successful; otherwise, null.</returns>
    public static T? ToJson<T>(this byte[]? data, JsonSerializerOptions? options = null)
    {
        if (data == null)
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(data, options ?? JsonSerializerOptions.Web);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize JSON data.", ex);
        }
    }

    /// <summary>
    ///     Converts a string into a UTF-8 encoded byte array suitable for use
    ///     as a message body.
    /// </summary>
    /// <param name="text">The input string to encode.</param>
    /// <returns>
    ///     A UTF-8 encoded <see cref="byte"/> array, or <c>null</c> if
    ///     <paramref name="text"/> is <c>null</c>.
    /// </returns>
    public static byte[]? ToMessageBody(this string? text)
    {
        if (text is null)
            return null;

        return Encoding.UTF8.GetBytes(text);
    }
    
    /// <summary>
    ///     Converts a UTF-8 encoded byte array into a string.
    /// </summary>
    /// <param name="bytes">The byte array to decode.</param>
    /// <returns>
    ///     The decoded string, or <c>null</c> if <paramref name="bytes"/> is <c>null</c>.
    /// </returns>
    public static string? FromMessageBody(this byte[]? bytes)
    {
        if (bytes is null)
            return null;

        return Encoding.UTF8.GetString(bytes);
    }
    
    /// <summary>
    ///     Serializes an object of type <typeparamref name="T"/> into JSON
    ///     and returns the UTF-8 encoded byte array suitable for use as a
    ///     message body.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">
    ///     Optional <see cref="JsonSerializerOptions"/> controlling the JSON formatting.
    ///     If not supplied, <see cref="JsonSerializerOptions.Web"/> is used.
    /// </param>
    /// <returns>
    ///     A UTF-8 encoded JSON <see cref="byte"/> array representing
    ///     <paramref name="value"/>. If <paramref name="value"/> is <c>null</c>,
    ///     the JSON literal <c>"null"</c> is returned.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if JSON serialization fails.
    /// </exception>
    public static byte[] ToMessageBody<T>(this T value, JsonSerializerOptions? options = null)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(
                value,
                options ?? JsonSerializerOptions.Web
            );
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to serialize object to JSON.", ex);
        }
    }
    
    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base URL.
    ///     Intended for use in tests.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="logger">The xUnit test output logger.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    public static async Task<FortitudeClient> CreateAsync(string fortitudeBaseUrl, ITestOutputHelper logger)
    {
        if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
            throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

        var fortitude = new FortitudeClient(new TestOutputLogger<FortitudeClient>(logger));

        try
        {
            await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
        }
        catch (Exception ex)
        {
            logger.WriteLine($"[ERROR] Failed to start FortitudeClient: {ex}");
            throw;
        }

        return fortitude;
    }
}

/// <summary>
///     Helper class to connect to a Fortitude server.
/// </summary>
public static class FortitudeServer
{
    /// <summary>
    ///     Connects to a Fortitude server and returns a started <see cref="FortitudeClient" />.
    ///     Intended for use in tests.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="logger">The xUnit test output logger.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    public static async Task<FortitudeClient> ConnectAsync(string fortitudeBaseUrl, ITestOutputHelper logger)
    {
        return await FortitudeExtensions.CreateAsync(fortitudeBaseUrl, logger);
    }
}

/// <summary>
///     An <see cref="ILogger{T}" /> implementation that writes logs to xUnit <see cref="ITestOutputHelper" />.
///     Useful for logging during test execution.
/// </summary>
/// <typeparam name="T">The type associated with the logger.</typeparam>
public class TestOutputLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestOutputLogger{T}" /> class.
    /// </summary>
    /// <param name="output">The xUnit test output helper.</param>
    public TestOutputLogger(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
    {
        return NullScope.Instance;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        try
        {
            var message = formatter(state, exception);
            if (!string.IsNullOrEmpty(message))
                _output.WriteLine($"[{logLevel}] {message}");

            if (exception != null)
                _output.WriteLine(exception.ToString());
        }
        catch
        {
            // Swallow exceptions to avoid crashing the test logger
        }
    }

    private class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}