using System.Text.Json;

namespace QuillForge.Core.Services;

internal static class StructuredJsonParser
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParse<T>(string json, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, s_options);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }
}
