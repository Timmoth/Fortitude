using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;

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
}

    /// <summary>
    /// Provides factory methods for creating and starting <see cref="FortitudeClient"/> instances
    /// connected to a Fortitude server. These helpers are primarily intended for use in tests.
    /// </summary>
    public static class FortitudeServer
    {
        /// <summary>
        /// Creates and starts a <see cref="FortitudeClient"/> connected to the specified base URL,
        /// using an xUnit <see cref="ITestOutputHelper"/> for logging.
        /// </summary>
        /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
        /// <param name="logger">The xUnit test output logger.</param>
        /// <returns>A connected <see cref="FortitudeClient"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="fortitudeBaseUrl"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when an error occurs while starting the client.
        /// </exception>
        public static async Task<FortitudeClient> ConnectAsync(
            string fortitudeBaseUrl,
            ITestOutputHelper logger)
        {
            if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
                throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

            if (logger is null)
                throw new ArgumentNullException(nameof(logger));
            
            var fortitude = new FortitudeClient(new TestOutputLogger<FortitudeClient>(logger));

            try
            {
                Console.WriteLine(fortitudeBaseUrl);
                await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
            }
            catch (Exception ex)
            {
                logger.WriteLine($"[ERROR] Failed to start FortitudeClient: {ex}");
                throw;
            }

            var uiServerUrl = $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude";
            logger.WriteLine("Fortitude server UI live at {0}...", uiServerUrl);

            return fortitude;
        }

        /// <summary>
        /// Creates and starts a <see cref="FortitudeClient"/> connected to the specified base URL,
        /// using the provided <see cref="ILogger{TCategoryName}"/> or a default console logger
        /// if none is supplied.
        /// </summary>
        /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
        /// <param name="logger">An optional <see cref="ILogger{FortitudeClient}"/> instance.</param>
        /// <returns>A connected <see cref="FortitudeClient"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="fortitudeBaseUrl"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when an error occurs while starting the client.
        /// </exception>
        public static async Task<FortitudeClient> ConnectAsync(
            string fortitudeBaseUrl,
            ILogger<FortitudeClient> logger)
        {
            if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
                throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

            logger ??= LoggerFactory
                .Create(builder => builder.AddSimpleConsole())
                .CreateLogger<FortitudeClient>();

            var fortitude = new FortitudeClient(logger);

            try
            {
                await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start FortitudeClient.");
                throw;
            }
            logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

            return fortitude;
        }
        
        /// <summary>
        /// Creates and starts a <see cref="FortitudeClient"/> connected to the specified base URL
        /// </summary>
        /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
        /// <returns>A connected <see cref="FortitudeClient"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="fortitudeBaseUrl"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when an error occurs while starting the client.
        /// </exception>
        public static async Task<FortitudeClient> ConnectAsync(
            string fortitudeBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
                throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

            var logger = LoggerFactory
                .Create(builder => builder.AddSimpleConsole())
                .CreateLogger<FortitudeClient>();

            var fortitude = new FortitudeClient(logger);

            try
            {
                await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start FortitudeClient.");
                throw;
            }
            logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

            return fortitude;
        }

        /// <summary>
        /// Creates and starts a <see cref="FortitudeClient"/> connected to the specified base URL,
        /// using the provided <see cref="ILoggerFactory"/> or a default console logger factory if none is supplied.
        /// </summary>
        /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
        /// <param name="loggerFactory">An optional <see cref="ILoggerFactory"/> instance.</param>
        /// <returns>A connected <see cref="FortitudeClient"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="fortitudeBaseUrl"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown when an error occurs while starting the client.
        /// </exception>
        public static async Task<FortitudeClient> ConnectAsync(
            string fortitudeBaseUrl,
            ILoggerFactory loggerFactory)
        {
            if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
                throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

            loggerFactory ??= LoggerFactory.Create(builder => builder.AddSimpleConsole());
            var logger = loggerFactory.CreateLogger<FortitudeClient>();

            var fortitude = new FortitudeClient(logger);

            try
            {
                await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start FortitudeClient.");
                throw;
            }
            
            logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

            return fortitude;
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