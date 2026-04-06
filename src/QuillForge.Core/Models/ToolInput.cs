using System.Text.Json;

namespace QuillForge.Core.Models;

/// <summary>
/// QuillForge-owned envelope for tool input payloads.
/// Keeps transport JSON at the boundary while giving handlers a stable,
/// discoverable API that can evolve toward typed args.
/// </summary>
public sealed class ToolInput
{
    private readonly JsonElement _json;

    public ToolInput(JsonElement json)
    {
        _json = json.Clone();
    }

    public static ToolInput Empty { get; } = new(JsonDocument.Parse("{}").RootElement);

    public JsonElement ToJsonElement() => _json.Clone();

    public string GetRawText() => _json.GetRawText();

    public string? GetOptionalString(string propertyName)
    {
        return TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    public string GetRequiredString(string propertyName)
    {
        var value = GetOptionalString(propertyName);
        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Missing required string property '{propertyName}'.");
        }

        return value;
    }

    public int? GetOptionalInt(string propertyName)
    {
        return TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;
    }

    public bool? GetOptionalBool(string propertyName)
    {
        if (!TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public IReadOnlyList<string> GetOptionalStringList(string propertyName)
    {
        if (!TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    public bool TryGetProperty(string propertyName, out JsonElement value)
    {
        if (_json.ValueKind == JsonValueKind.Object && _json.TryGetProperty(propertyName, out var prop))
        {
            value = prop.Clone();
            return true;
        }

        value = default;
        return false;
    }
}
