using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public static class ToolArgs<T> where T : notnull
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static T Parse(ToolInput input)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(input.GetRawText(), s_options);
            if (value is null)
            {
                throw new ToolArgsParseException($"Tool input could not be deserialized into {typeof(T).Name}.");
            }

            return value;
        }
        catch (JsonException ex) when (ex is not ToolArgsParseException)
        {
            throw new ToolArgsParseException(
                $"Tool input could not be deserialized into {typeof(T).Name}.",
                ex);
        }
    }
}

public sealed class ToolArgsParseException : JsonException
{
    public ToolArgsParseException(string message)
        : base(message)
    {
    }

    public ToolArgsParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
