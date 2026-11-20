using System.Text;

namespace Fortitude.Client;

/// <summary>
///     Helpers for deciding whether a captured body is text at all.
/// </summary>
public static class FortitudeText
{
    /// <summary>
    ///     A UTF-8 decoder that throws on invalid bytes.
    /// </summary>
    /// <remarks>
    ///     <see cref="Encoding.UTF8" /> substitutes U+FFFD for anything it cannot decode and
    ///     never throws, so it cannot be used to tell text from binary: a PNG decodes
    ///     "successfully" into replacement characters, and the original bytes are gone.
    /// </remarks>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    ///     Decodes a body as UTF-8, reporting failure rather than substituting replacement
    ///     characters.
    /// </summary>
    /// <param name="bytes">The body to decode. A null or empty body decodes to an empty string.</param>
    /// <param name="text">The decoded text, or an empty string when the bytes are not UTF-8.</param>
    /// <returns>True if the bytes are valid UTF-8; false if the body is binary.</returns>
    public static bool TryDecodeUtf8(byte[]? bytes, out string text)
    {
        if (bytes is not { Length: > 0 })
        {
            text = string.Empty;
            return true;
        }

        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    ///     Quotes a value for a POSIX shell.
    /// </summary>
    /// <remarks>
    ///     Single quotes protect everything except a single quote itself, which is emitted as
    ///     <c>'\''</c>: end the quoted run, add an escaped quote, start a new run. A backslash
    ///     is literal inside single quotes, so the obvious <c>\'</c> silently produces a
    ///     command the shell parses as something else entirely.
    /// </remarks>
    public static string ShellQuote(string value) =>
        "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
}
