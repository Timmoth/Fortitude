using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NodeDeserializers;

namespace Fortitude.Client;

/// <summary>
///     A YAML scalar together with whether the author quoted it.
/// </summary>
/// <param name="Value">The scalar's text.</param>
/// <param name="IsQuoted">
///     True when the document wrote it in quotes, which means the author asked for a string
///     rather than letting YAML infer a type.
/// </param>
public sealed record YamlScalar(string Value, bool IsQuoted);

/// <summary>
///     Deserializes untyped scalars into <see cref="YamlScalar" /> so quoting survives.
/// </summary>
/// <remarks>
///     YamlDotNet hands every untyped scalar back as a plain string, so by the time a response
///     body is rendered there is no way to tell <c>count: 3</c> from <c>id: "3"</c>. Without
///     that distinction a recorded JSON string of <c>"7"</c> replays as the number <c>7</c>,
///     and an account number loses its leading zeros.
/// </remarks>
internal sealed class StyleAwareScalarDeserializer : INodeDeserializer
{
    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer)
    {
        // Only intervene where the schema is untyped; anything with a declared type should
        // go through YamlDotNet's normal conversion.
        if (expectedType != typeof(object) || !reader.Accept<Scalar>(out var scalar))
        {
            value = null;
            return false;
        }

        reader.MoveNext();

        value = new YamlScalar(scalar.Value, scalar.Style != ScalarStyle.Plain);
        return true;
    }
}

/// <summary>Builds the deserializer used for handler definitions.</summary>
internal static class YamlDeserializer
{
    public static IDeserializer Create()
    {
        return new DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            // Before, not InsteadOf: this handles untyped scalars and declines everything
            // else, so the built-in deserializer must stay in place to convert typed ones.
            .WithNodeDeserializer(
                new StyleAwareScalarDeserializer(),
                s => s.Before<ScalarNodeDeserializer>())
            .Build();
    }
}
