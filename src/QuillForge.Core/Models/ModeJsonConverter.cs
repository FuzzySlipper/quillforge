using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

/// <summary>
/// Persists modes using QuillForge's wire strings while accepting the legacy
/// "general" value as an alias for the guide mode.
/// </summary>
public sealed class ModeJsonConverter : JsonConverter<Mode>
{
    public override Mode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token when reading {nameof(Mode)}.");
        }

        var value = reader.GetString();
        var parsed = ModeExtensions.TryParseMode(value);
        if (parsed.HasValue)
        {
            return parsed.Value;
        }

        throw new JsonException($"Unknown mode value '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, Mode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToWireString());
    }
}
