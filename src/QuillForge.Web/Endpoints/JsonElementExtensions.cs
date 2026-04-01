using System.Text.Json;

namespace QuillForge.Web.Endpoints;

/// <summary>
/// Null-safe accessors for <see cref="JsonElement"/> properties.
/// Every method returns null (or the supplied default) when the property
/// is missing, is JSON null, or has the wrong ValueKind — no exceptions.
/// </summary>
public static class JsonElementExtensions
{
    public static string? GetOptionalString(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    public static string GetStringOrDefault(this JsonElement el, string propertyName, string defaultValue)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? defaultValue
            : defaultValue;

    public static Guid? GetOptionalGuid(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var guid)
            ? guid
            : null;

    public static int? GetOptionalInt(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var val)
            ? val
            : null;

    public static int GetIntOrDefault(this JsonElement el, string propertyName, int defaultValue)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var val)
            ? val
            : defaultValue;

    public static float? GetOptionalFloat(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetSingle(out var val)
            ? val
            : null;

    public static double? GetOptionalDouble(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDouble(out var val)
            ? val
            : null;

    public static bool? GetOptionalBool(this JsonElement el, string propertyName)
        => el.TryGetProperty(propertyName, out var prop)
            ? prop.ValueKind == JsonValueKind.True ? true
            : prop.ValueKind == JsonValueKind.False ? false
            : null
            : null;
}
