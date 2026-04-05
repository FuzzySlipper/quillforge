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
        if (string.IsNullOrWhiteSpace(value))
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

    public IReadOnlyDictionary<string, object>? GetOptionalObjectMap(string propertyName)
    {
        if (!TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ConvertObject(prop);
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

    private static IReadOnlyDictionary<string, object> ConvertObject(JsonElement element)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = ConvertValue(property.Value);
        }

        return values;
    }

    private static object ConvertValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertValue).ToList(),
        JsonValueKind.Object => ConvertObject(element),
        _ => element.ToString() ?? string.Empty,
    };
}
