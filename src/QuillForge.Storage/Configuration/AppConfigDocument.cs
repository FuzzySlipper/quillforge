using Den.Persistence;
using QuillForge.Core;
using QuillForge.Core.Models;

namespace QuillForge.Storage.Configuration;

/// <summary>
/// Persisted document definition for the application configuration (config.yaml).
/// Defines the file path, default state, and normalization for AppConfig.
/// </summary>
public sealed class AppConfigDocument : PersistedDocumentBase<AppConfig>
{
    public override string RelativePath => ContentPaths.ConfigFile;

    public override AppConfig CreateDefault() => new();

    public override AppConfig Normalize(AppConfig value) => value with
    {
        Profiles = value.Profiles with
        {
            Default = string.IsNullOrWhiteSpace(value.Profiles.Default) ? "default" : value.Profiles.Default.Trim(),
        },
        Forge = value.Forge with
        {
            ReviewPassThreshold = Math.Clamp(value.Forge.ReviewPassThreshold, 1, 10),
            MaxRevisions = Math.Max(value.Forge.MaxRevisions, 0),
            StageTimeoutMinutes = Math.Max(value.Forge.StageTimeoutMinutes, 1),
        },
        Persona = value.Persona with
        {
            MaxTokens = Math.Max(value.Persona.MaxTokens, 100),
        },
        Agents = value.Agents with
        {
            Orchestrator = value.Agents.Orchestrator with
            {
                MaxToolRounds = Math.Max(value.Agents.Orchestrator.MaxToolRounds, 1),
            },
            Librarian = value.Agents.Librarian with
            {
                MaxTokens = Math.Max(value.Agents.Librarian.MaxTokens, 256),
            },
            ProseWriter = value.Agents.ProseWriter with
            {
                MaxTokens = Math.Max(value.Agents.ProseWriter.MaxTokens, 256),
            },
            Council = value.Agents.Council with
            {
                Temperature = Math.Clamp(value.Agents.Council.Temperature, 0, 2),
            },
        },
        Timeouts = value.Timeouts with
        {
            ToolExecutionSeconds = Math.Max(value.Timeouts.ToolExecutionSeconds, 5),
            ProviderHttpSeconds = Math.Max(value.Timeouts.ProviderHttpSeconds, 1),
            UpdateCheckHours = Math.Max(value.Timeouts.UpdateCheckHours, 1),
        },
    };
}
