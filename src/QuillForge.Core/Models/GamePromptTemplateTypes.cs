using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

public sealed record GamePromptTemplateSelection
{
    public GamePromptTemplateSource Source { get; init; } = GamePromptTemplateSource.Default;

    public string? UserPromptName { get; init; }

    [JsonIgnore]
    public bool IsUserPrompt => Source == GamePromptTemplateSource.User
        && !string.IsNullOrWhiteSpace(UserPromptName);

    public static GamePromptTemplateSelection Default { get; } = new();

    public static GamePromptTemplateSelection ForUserPrompt(string userPromptName) => new()
    {
        Source = GamePromptTemplateSource.User,
        UserPromptName = userPromptName,
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<GamePromptTemplateSource>))]
public enum GamePromptTemplateSource
{
    Default,
    User,
}

public sealed record GameUserPromptTemplateInfo
{
    public required string ModuleId { get; init; }

    public required string Name { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required int Tokens { get; init; }

    public required long Size { get; init; }
}

public sealed record GameUserPromptTemplateDocument
{
    public required string ModuleId { get; init; }

    public required string Name { get; init; }

    public required string FileName { get; init; }

    public required string RelativePath { get; init; }

    public required string Content { get; init; }

    public int Tokens => Content.Length / 4;
}

public sealed record GameResolvedPromptTemplate
{
    public required string Content { get; init; }

    public required GamePromptTemplateSelection Selection { get; init; }

    public bool UsedFallback { get; init; }

    public string? FallbackReason { get; init; }
}
