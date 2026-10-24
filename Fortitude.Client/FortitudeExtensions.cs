using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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
    ///     Attempts to extract a named route parameter from a route value given a route template.
    /// </summary>
    /// <param name="routeValue">The actual route value, e.g. "/users/123"</param>
    /// <param name="pattern">The route template, e.g. "/users/{id}"</param>
    /// <param name="parameterName">The name of the parameter to extract, e.g. "id"</param>
    /// <returns>The parameter value if matched; otherwise null.</returns>
    public static object? GetRouteParameter(this string routeValue, string pattern, string parameterName)
    {
        if (string.IsNullOrEmpty(routeValue))
            throw new ArgumentNullException(nameof(routeValue));
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentNullException(nameof(pattern));
        if (string.IsNullOrEmpty(parameterName))
            throw new ArgumentNullException(nameof(parameterName));

        // Parse the template
        var template = TemplateParser.Parse(pattern);
        var matcher = new TemplateMatcher(template, new RouteValueDictionary());

        var values = new RouteValueDictionary();
        var matched = matcher.TryMatch(routeValue, values);

        if (!matched) return null;

        return values.GetValueOrDefault(parameterName);
    }

    /// <summary>
    ///     Converts a string into a UTF-8 encoded byte array suitable for use
    ///     as a message body.
    /// </summary>
    /// <param name="text">The input string to encode.</param>
    /// <returns>
    ///     A UTF-8 encoded <see cref="byte" /> array, or <c>null</c> if
    ///     <paramref name="text" /> is <c>null</c>.
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
    ///     The decoded string, or <c>null</c> if <paramref name="bytes" /> is <c>null</c>.
    /// </returns>
    public static string? FromMessageBody(this byte[]? bytes)
    {
        if (bytes is null)
            return null;

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    ///     Serializes an object of type <typeparamref name="T" /> into JSON
    ///     and returns the UTF-8 encoded byte array suitable for use as a
    ///     message body.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">
    ///     Optional <see cref="JsonSerializerOptions" /> controlling the JSON formatting.
    ///     If not supplied, <see cref="JsonSerializerOptions.Web" /> is used.
    /// </param>
    /// <returns>
    ///     A UTF-8 encoded JSON <see cref="byte" /> array representing
    ///     <paramref name="value" />. If <paramref name="value" /> is <c>null</c>,
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
///     Provides factory methods for creating and starting <see cref="FortitudeClient" /> instances
///     connected to a Fortitude server. These helpers are primarily intended for use in tests.
/// </summary>
public static class FortitudeServer
{
    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base URL,
    ///     using an xUnit <see cref="ITestOutputHelper" /> for logging.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="logger">The xUnit test output logger.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="fortitudeBaseUrl" /> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs while starting the client.
    /// </exception>
    public static async Task<(FortitudeClient, string)> ConnectAsync(
        string fortitudeBaseUrl,
        ITestOutputHelper logger)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        return await ConnectAsync(fortitudeBaseUrl, logger.WriteLine).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base
    ///     URL, writing diagnostics to <paramref name="writeLine" />.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="writeLine">
    ///     Receives one line per log entry — <c>TestContext.WriteLine</c> under NUnit or
    ///     MSTest, or any other sink.
    /// </param>
    /// <returns>The connected client, and the URL the system under test should be pointed at.</returns>
    public static async Task<(FortitudeClient, string)> ConnectAsync(
        string fortitudeBaseUrl,
        Action<string> writeLine)
    {
        if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
            throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

        if (writeLine is null)
            throw new ArgumentNullException(nameof(writeLine));

        var logger = writeLine;
        var fortitude = new FortitudeClient(new TestOutputLogger<FortitudeClient>(logger));

        var port = -1;
        try
        {
            port = await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
        }
        catch (Exception ex)
        {
            logger($"[ERROR] Failed to start FortitudeClient: {ex}");
            throw;
        }

        var uiServerUrl = $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude";
        logger($"Fortitude server UI live at {uiServerUrl}...");

        return (fortitude, ReplacePort(fortitudeBaseUrl.TrimEnd('/'), port));
    }

    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base URL,
    ///     using the provided <see cref="ILogger{TCategoryName}" /> or a default console logger
    ///     if none is supplied.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="logger">An optional <see cref="ILogger{FortitudeClient}" /> instance.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="fortitudeBaseUrl" /> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs while starting the client.
    /// </exception>
    public static async Task<(FortitudeClient, string)> ConnectAsync(
        string fortitudeBaseUrl,
        ILogger<FortitudeClient> logger)
    {
        if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
            throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

        logger ??= LoggerFactory
            .Create(builder => builder.AddSimpleConsole())
            .CreateLogger<FortitudeClient>();

        var fortitude = new FortitudeClient(logger);
        var port = -1;

        try
        {
            port = await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start FortitudeClient.");
            throw;
        }

        logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

        return (fortitude, ReplacePort(fortitudeBaseUrl.TrimEnd('/'), port));
    }

    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base URL
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="fortitudeBaseUrl" /> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs while starting the client.
    /// </exception>
    public static async Task<(FortitudeClient, string)> ConnectAsync(
        string fortitudeBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
            throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

        var logger = LoggerFactory
            .Create(builder => builder.AddSimpleConsole())
            .CreateLogger<FortitudeClient>();

        var fortitude = new FortitudeClient(logger);
        var port = -1;

        try
        {
            port = await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start FortitudeClient.");
            throw;
        }

        logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

        return (fortitude, ReplacePort(fortitudeBaseUrl.TrimEnd('/'), port));
    }

    /// <summary>
    ///     Creates and starts a <see cref="FortitudeClient" /> connected to the specified base URL,
    ///     using the provided <see cref="ILoggerFactory" /> or a default console logger factory if none is supplied.
    /// </summary>
    /// <param name="fortitudeBaseUrl">The base URL of the Fortitude server.</param>
    /// <param name="loggerFactory">An optional <see cref="ILoggerFactory" /> instance.</param>
    /// <returns>A connected <see cref="FortitudeClient" /> instance.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="fortitudeBaseUrl" /> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs while starting the client.
    /// </exception>
    public static async Task<(FortitudeClient, string)> ConnectAsync(
        string fortitudeBaseUrl,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(fortitudeBaseUrl))
            throw new ArgumentException("Fortitude base URL cannot be null or empty.", nameof(fortitudeBaseUrl));

        loggerFactory ??= LoggerFactory.Create(builder => builder.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger<FortitudeClient>();

        var fortitude = new FortitudeClient(logger);
        var port = -1;

        try
        {
            port = await fortitude.StartAsync($"{fortitudeBaseUrl.TrimEnd('/')}/fortitude/hub");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start FortitudeClient.");
            throw;
        }

        logger.LogInformation("Fortitude server UI live at {Url}...", $"{fortitudeBaseUrl.TrimEnd('/')}/fortitude");

        return (fortitude, ReplacePort(fortitudeBaseUrl.TrimEnd('/'), port));
    }

    /// <summary>
    ///     Replaces the port of a given URL with a new port.
    ///     If the original URL has no port, the port will be added.
    /// </summary>
    /// <param name="url">The input URL.</param>
    /// <param name="newPort">The port number to set.</param>
    /// <returns>The URL with the updated port.</returns>
    /// <exception cref="ArgumentNullException">If url is null or empty.</exception>
    /// <exception cref="UriFormatException">If the URL is invalid.</exception>
    public static string ReplacePort(string url, int newPort)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url));

        var uri = new Uri(url, UriKind.Absolute);

        // Create a new UriBuilder from the original Uri
        var builder = new UriBuilder(uri)
        {
            Port = newPort
        };

        return builder.Uri.ToString();
    }
}

/// <summary>
///     An <see cref="ILogger{T}" /> that writes each entry to a callback.
/// </summary>
/// <typeparam name="T">The type associated with the logger.</typeparam>
/// <remarks>
///     Bridges Fortitude's logging to whatever the test framework offers. xUnit callers can
///     pass an <see cref="ITestOutputHelper" /> directly; NUnit, MSTest and TUnit users can
///     pass their own write callback, for example <c>TestContext.WriteLine</c>.
/// </remarks>
public class TestOutputLogger<T> : ILogger<T>
{
    private readonly Action<string> _write;

    /// <summary>
    ///     Initializes a new instance writing to an arbitrary callback.
    /// </summary>
    /// <param name="write">Receives one line per log entry.</param>
    public TestOutputLogger(Action<string> write)
    {
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    /// <summary>
    ///     Initializes a new instance writing to an xUnit test output helper.
    /// </summary>
    /// <param name="output">The xUnit test output helper.</param>
    public TestOutputLogger(ITestOutputHelper output)
        : this((output ?? throw new ArgumentNullException(nameof(output))).WriteLine)
    {
    }

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
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
                _write($"[{logLevel}] {message}");

            if (exception != null)
                _write(exception.ToString());
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