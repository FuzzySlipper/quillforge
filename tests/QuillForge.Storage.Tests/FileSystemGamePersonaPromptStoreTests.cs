using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemGamePersonaPromptStoreTests
{
    [Fact]
    public async Task CreateUniqueAsync_WritesCollisionSafeGenericPersonaFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            var first = await store.CreateUniqueAsync("wolf persona", "First persona");
            var second = await store.CreateUniqueAsync("wolf persona", "Second persona");

            Assert.Equal("wolf persona", first.Name);
            Assert.Equal("wolf persona-2", second.Name);
            Assert.Equal($"{ContentPaths.GamePersonas}/wolf persona.md", first.RelativePath);
            Assert.True(File.Exists(Path.Combine(root, ContentPaths.GamePersonas, "wolf persona.md")));
            Assert.True(File.Exists(Path.Combine(root, ContentPaths.GamePersonas, "wolf persona-2.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_UsesAtomicUserContentPath()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            await store.SaveAsync("villager", "Careful villager persona.");
            var document = await store.TryLoadAsync("villager");
            var list = await store.ListAsync();

            Assert.NotNull(document);
            Assert.Equal("Careful villager persona.", document.Content);
            Assert.Equal($"{ContentPaths.GamePersonas}/villager.md", document.RelativePath);
            var prompt = Assert.Single(list);
            Assert.Equal("villager", prompt.Name);
            Assert.Equal($"{ContentPaths.GamePersonas}/villager.md", prompt.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryLoadAsync_RejectsPathTraversal()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            var document = await store.TryLoadAsync("../outside");

            Assert.Null(document);
            Assert.False(File.Exists(Path.Combine(root, "outside.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileSystemGamePersonaPromptStore CreateStore(string root) =>
        new(
            root,
            new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
            NullLogger<FileSystemGamePersonaPromptStore>.Instance);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quillforge-game-personas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
