using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

internal static class ToolInputSchemaValidator
{
    public static bool TryValidate(ToolInput input, JsonElement schema, out string? error)
    {
        return TryValidateElement(input.ToJsonElement(), schema, "$", out error);
    }

    private static bool TryValidateElement(JsonElement value, JsonElement schema, string path, out string? error)
    {
        error = null;

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!schema.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var schemaType = typeElement.GetString();
        switch (schemaType)
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    error = $"{path} must be an object.";
                    return false;
                }

                if (schema.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in requiredElement.EnumerateArray())
                    {
                        var propertyName = item.GetString();
                        if (string.IsNullOrWhiteSpace(propertyName))
                        {
                            continue;
                        }

                        if (!value.TryGetProperty(propertyName, out _))
                        {
                            error = $"{path}.{propertyName} is required.";
                            return false;
                        }
                    }
                }

                if (schema.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in propertiesElement.EnumerateObject())
                    {
                        if (!value.TryGetProperty(property.Name, out var propertyValue))
                        {
                            continue;
                        }

                        if (!TryValidateElement(propertyValue, property.Value, $"{path}.{property.Name}", out error))
                        {
                            return false;
                        }
                    }
                }

                return true;

            case "array":
                if (value.ValueKind != JsonValueKind.Array)
                {
                    error = $"{path} must be an array.";
                    return false;
                }

                if (schema.TryGetProperty("items", out var itemsElement))
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (!TryValidateElement(item, itemsElement, $"{path}[{index}]", out error))
                        {
                            return false;
                        }

                        index++;
                    }
                }

                return true;

            case "string":
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = $"{path} must be a string.";
                    return false;
                }

                return true;

            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _))
                {
                    error = $"{path} must be an integer.";
                    return false;
                }

                return true;

            case "number":
                if (value.ValueKind != JsonValueKind.Number)
                {
                    error = $"{path} must be a number.";
                    return false;
                }

                return true;

            case "boolean":
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                {
                    error = $"{path} must be a boolean.";
                    return false;
                }

                return true;

            case "null":
                if (value.ValueKind != JsonValueKind.Null)
                {
                    error = $"{path} must be null.";
                    return false;
                }

                return true;

            default:
                return true;
        }
    }
}
