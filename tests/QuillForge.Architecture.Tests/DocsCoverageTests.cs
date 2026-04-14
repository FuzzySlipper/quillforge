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
}
