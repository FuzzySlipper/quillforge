using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Endpoints;

namespace QuillForge.Architecture.Tests;

public sealed class ForgeDocumentAvailabilityTests
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [Fact]
    public async Task TryLoadStatusResponseAsync_IncludesAvailableDocumentsFromBackendContract()
    {
        var files = new InMemoryContentFileService();
        var manifest = new ForgeManifest
        {
            ProjectName = "space forge",
            Stage = ForgeStage.Writing,
            ChapterCount = 1,
            Chapters = new Dictionary<string, ChapterStatus>
            {
                ["ch-01"] = new()
                {
                    State = ChapterState.Done,
                    WordCount = 1200,
                },
            },
            Stats = new ForgeStats
            {
                TotalInputTokens = 100,
                TotalOutputTokens = 200,
                AgentCalls = 2,
            },
        };

        files.Seed("forge/space forge/manifest.json", JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        files.Seed("forge/space forge/plan/outline.md", "# Outline");
        files.Seed("forge/space forge/output/story.md", "# Story");

        var response = await ForgeEndpoints.TryLoadStatusResponseAsync("space forge", files, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(["Outline", "Output story"], response.Documents.Select(document => document.Label).ToArray());
        Assert.Equal(["outline", "outputStory"], response.Documents.Select(document => document.Kind).ToArray());
        Assert.Equal("forge/space forge/plan/outline.md", response.Documents[0].RelativePath);
        Assert.Equal("/content/forge/space%20forge/plan/outline.md", response.Documents[0].Href);
        Assert.Equal("forge/space forge/output/story.md", response.Documents[1].RelativePath);
        Assert.Equal("/content/forge/space%20forge/output/story.md", response.Documents[1].Href);
    }

    [Fact]
    public async Task ListAvailableForgeDocumentsAsync_PreservesShelfOrderAndSkipsMissingFiles()
    {
        var files = new InMemoryContentFileService();
        files.Seed("forge/ember/plan/style.md", "style");
        files.Seed("forge/ember/run-lore.md", "lore");

        var documents = await ForgeEndpoints.ListAvailableForgeDocumentsAsync("ember", files, CancellationToken.None);

        Assert.Equal(["Style spec", "Run lore"], documents.Select(document => document.Label).ToArray());
        Assert.All(documents, document => Assert.StartsWith("/content/forge/ember/", document.Href, StringComparison.Ordinal));
    }

    private sealed class InMemoryContentFileService : IContentFileService
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string relativePath, string content)
        {
            _files[relativePath] = content;
        }

        public Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
        {
            if (!_files.TryGetValue(relativePath, out var content))
            {
                throw new FileNotFoundException($"Content file not found: {relativePath}");
            }

            return Task.FromResult(content);
        }

        public Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
        {
            _files[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
        {
            var files = _files.Keys
                .Where(path => path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(files);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        {
            return Task.FromResult(_files.ContainsKey(relativePath));
        }

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            _files.Remove(relativePath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
            string directory,
            string query,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<(string, string)>>([]);
        }
    }
}
