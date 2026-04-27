using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.Docs;

namespace QuillForge.Architecture.Tests;

public sealed class DocsCoverageTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task AppDocs_ExposeOwnershipTopicForProfilesSessionsAndConversations()
    {
        var docsRoot = Path.Combine(RepoRoot, "dev", "app-docs");
        var service = new FileSystemDocsService(
            docsRoot,
            NullLogger<FileSystemDocsService>.Instance);

        var topic = await service.GetTopicAsync("profile-session-conversation-ownership");

        Assert.NotNull(topic);
        Assert.Contains("AppConfig", topic.Content, StringComparison.Ordinal);
        Assert.Contains("ProfileConfig", topic.Content, StringComparison.Ordinal);
        Assert.Contains("SessionState", topic.Content, StringComparison.Ordinal);
        Assert.Contains("ConversationTree", topic.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppDocs_Search_FindsOwnershipVocabulary()
    {
        var docsRoot = Path.Combine(RepoRoot, "dev", "app-docs");
        var service = new FileSystemDocsService(
            docsRoot,
            NullLogger<FileSystemDocsService>.Instance);

        var results = await service.SearchAsync("AppConfig ProfileConfig SessionState ConversationTree");

        Assert.Contains(results, result => result.Slug == "profile-session-conversation-ownership");
    }

    [Fact]
    public async Task ArchitectureDocs_IncludeSocialGamesFrameworkSpec()
    {
        var specPath = Path.Combine(
            RepoRoot,
            "docs",
            "architecture",
            "social-games-framework-architecture.md");

        var content = await File.ReadAllTextAsync(specPath);

        Assert.Contains("RulesEngineService", content, StringComparison.Ordinal);
        Assert.Contains("RulesGameStateSnapshot", content, StringComparison.Ordinal);
        Assert.Contains("AgentVisibleEvents", content, StringComparison.Ordinal);
        Assert.Contains("GameRuntimeState", content, StringComparison.Ordinal);
        Assert.Contains("GameIntentTranslationAgent", content, StringComparison.Ordinal);
        Assert.Contains("GameVisibilityProjector", content, StringComparison.Ordinal);
        Assert.Contains("IRulesEngineObserver", content, StringComparison.Ordinal);
        Assert.Contains("GameEventJournal", content, StringComparison.Ordinal);
        Assert.Contains("GameReplayService", content, StringComparison.Ordinal);
        Assert.Contains("GameTemplateStore", content, StringComparison.Ordinal);
        Assert.Contains("GameSetupValidationService", content, StringComparison.Ordinal);
        Assert.Contains("ParticipantCommunicationState", content, StringComparison.Ordinal);
        Assert.Contains("AgentPromptEnvelope", content, StringComparison.Ordinal);
        Assert.Contains("GameRuntimeForkedEvent", content, StringComparison.Ordinal);
        Assert.Contains("Mode.Games", content, StringComparison.Ordinal);
        Assert.Contains("ModeExtensions.ToWireString()", content, StringComparison.Ordinal);
        Assert.Contains("src/Den.RulesEngine/", content, StringComparison.Ordinal);
        Assert.Contains("private-to-player", content, StringComparison.Ordinal);
        Assert.Contains("private-to-set", content, StringComparison.Ordinal);
        Assert.Contains("hidden/system-only", content, StringComparison.Ordinal);
        Assert.Contains("prompt envelopes", content, StringComparison.Ordinal);
        Assert.Contains("mode switch is rejected during an active game", content, StringComparison.Ordinal);
        Assert.Contains("SessionRuntimeService.SetModeAsync()", content, StringComparison.Ordinal);
        Assert.Contains("Forking a session with an active game", content, StringComparison.Ordinal);
        Assert.Contains("Deleting a session deletes", content, StringComparison.Ordinal);
        Assert.Contains("50 KiB", content, StringComparison.Ordinal);
        Assert.Contains("200 ms p95", content, StringComparison.Ordinal);
        Assert.Contains("baseline Werewolf", content, StringComparison.Ordinal);
        Assert.Contains("RuleWeaver Architecture Survey", content, StringComparison.Ordinal);
        Assert.Contains("Assumptions Not To Copy", content, StringComparison.Ordinal);
    }
}
