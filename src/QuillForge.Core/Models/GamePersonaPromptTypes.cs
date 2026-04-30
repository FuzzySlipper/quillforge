using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

public sealed record GamePersonaPromptSelection
{
    public GamePersonaPromptSource Source { get; init; } = GamePersonaPromptSource.None;

    public string? UserPromptName { get; init; }

    [JsonIgnore]
    public bool IsUserPrompt => Source == GamePersonaPromptSource.User
        && !string.IsNullOrWhiteSpace(UserPromptName);

    public static GamePersonaPromptSelection None { get; } = new();

    public static GamePersonaPromptSelection ForUserPrompt(string userPromptName) => new()
    {
        Source = GamePersonaPromptSource.User,
        UserPromptName = userPromptName,
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<GamePersonaPromptSource>))]
public enum GamePersonaPromptSource
{
    None,
    User,
}

public sealed record GameUserPersonaPromptInfo
{
    public required string Name { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required int Tokens { get; init; }

    public required long Size { get; init; }
}

public sealed record GameUserPersonaPromptDocument
{
    public required string Name { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required string Content { get; init; }

    public int Tokens => Content.Length / 4;
}

public sealed record GameResolvedPersonaPrompt
{
    public required string? Content { get; init; }

    public required GamePersonaPromptSelection Selection { get; init; }

    public bool UsedFallback { get; init; }

    public string? FallbackReason { get; init; }
}
