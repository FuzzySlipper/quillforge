using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemGamePromptTemplateStoreTests
{
    [Fact]
    public async Task CreateUniqueAsync_WritesMarkdownUnderGamePromptsModuleDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            var first = await store.CreateUniqueAsync("werewolf", "werewolf-prompt-20260429", "Default content");
            var second = await store.CreateUniqueAsync("werewolf", "werewolf-prompt-20260429", "Default content 2");

            Assert.Equal("werewolf-prompt-20260429", first.Name);
            Assert.Equal("werewolf-prompt-20260429-2", second.Name);
            Assert.Equal($"{ContentPaths.GamePrompts}/werewolf/werewolf-prompt-20260429.md", first.RelativePath);
            Assert.True(File.Exists(Path.Combine(root, ContentPaths.GamePrompts, "werewolf", "werewolf-prompt-20260429.md")));
            Assert.True(File.Exists(Path.Combine(root, ContentPaths.GamePrompts, "werewolf", "werewolf-prompt-20260429-2.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLoadAndList_RoundTripsExistingUserPromptWithoutCreatingDuplicate()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            await store.SaveAsync("werewolf", "custom", "First content");
            var opened = await store.TryLoadAsync("werewolf", "custom");
            await store.SaveAsync("werewolf", "custom", "Updated content");
            var reopened = await store.TryLoadAsync("werewolf", "custom");
            var listed = await store.ListAsync("werewolf");

            Assert.NotNull(opened);
            Assert.NotNull(reopened);
            Assert.Equal("Updated content", reopened.Content);
            Assert.Single(listed);
            Assert.Equal("custom", listed[0].Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryLoadAsync_ReturnsNullForInvalidPromptPath()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            var loaded = await store.TryLoadAsync("werewolf", "../escape");

            Assert.Null(loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileSystemGamePromptTemplateStore CreateStore(string root) =>
        new(
            root,
            new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
            NullLogger<FileSystemGamePromptTemplateStore>.Instance);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quillforge-game-prompts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
