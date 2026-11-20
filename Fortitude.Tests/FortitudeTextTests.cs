using System.Text;
using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     <see cref="FortitudeText" />: telling text from binary, and quoting for a shell.
/// </summary>
/// <remarks>
///     Both are small enough to look obviously correct, and both were previously got wrong in
///     ways that only surfaced in generated output, so they are pinned directly here.
/// </remarks>
public class FortitudeTextTests
{
    private static readonly byte[] NotUtf8 = [0xFF, 0xFE, 0x00, 0x01];

    // ---------------------------------------------------------------- decoding

    [Fact]
    public void ValidUtf8Decodes()
    {
        Assert.True(FortitudeText.TryDecodeUtf8(Encoding.UTF8.GetBytes("hello world"), out var text));
        Assert.Equal("hello world", text);
    }

    [Fact]
    public void MultiByteUtf8Decodes()
    {
        var original = "café — 日本語";

        Assert.True(FortitudeText.TryDecodeUtf8(Encoding.UTF8.GetBytes(original), out var text));
        Assert.Equal(original, text);
    }

    [Fact]
    public void AnEmptyBodyCountsAsText()
    {
        Assert.True(FortitudeText.TryDecodeUtf8([], out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ANullBodyCountsAsText()
    {
        Assert.True(FortitudeText.TryDecodeUtf8(null, out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void InvalidUtf8IsReportedRatherThanSubstituted()
    {
        Assert.False(FortitudeText.TryDecodeUtf8(NotUtf8, out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void TheLenientDecoderCannotTellTextFromBinary()
    {
        // The reason TryDecodeUtf8 exists: Encoding.UTF8 substitutes U+FFFD and never
        // throws, so "did this decode?" is not a question it can answer.
        var substituted = Encoding.UTF8.GetString(NotUtf8);

        Assert.Contains('�', substituted);
    }

    [Fact]
    public void APngHeaderIsNotText()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        Assert.False(FortitudeText.TryDecodeUtf8(png, out _));
    }

    // ---------------------------------------------------------------- quoting

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("has space", "'has space'")]
    [InlineData("a\"b", "'a\"b'")]
    [InlineData("semi; rm -rf /", "'semi; rm -rf /'")]
    public void OrdinaryValuesAreSingleQuoted(string value, string expected)
    {
        Assert.Equal(expected, FortitudeText.ShellQuote(value));
    }

    [Fact]
    public void ASingleQuoteIsClosedAndReopened()
    {
        // '\'' is the only form a POSIX shell accepts. A backslash is literal inside single
        // quotes, so the obvious \' ends the string early and the rest of the command is
        // reinterpreted as something else entirely.
        Assert.Equal("'O'\\''Brien'", FortitudeText.ShellQuote("O'Brien"));
    }

    [Fact]
    public void SeveralQuotesAreEachEscaped()
    {
        Assert.Equal("'it'\\''s '\\''fine'\\'''", FortitudeText.ShellQuote("it's 'fine'"));
    }

    [Fact]
    public void AnEmptyValueStillProducesAnArgument()
    {
        Assert.Equal("''", FortitudeText.ShellQuote(string.Empty));
    }
}
