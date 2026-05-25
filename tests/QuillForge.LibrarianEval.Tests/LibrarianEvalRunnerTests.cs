using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.FileSystem;
using Xunit;

namespace QuillForge.LibrarianEval.Tests;

public sealed class LibrarianEvalRunnerTests
{
    [Fact]
    public async Task RunAsync_WithFakeService_ReturnsResultsForAllQuestions()
    {
        var corpusPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lore");
        if (!Directory.Exists(corpusPath))
        {
            // Fallback for running from repo root
            corpusPath = Path.Combine(GetRepoRoot(), "tests", "QuillForge.LibrarianEval.Tests", "Fixtures", "synthetic-lore");
        }

        var loreStore = new FileSystemLoreStore(corpusPath, LoggerFactory.Create(_ => { }).CreateLogger<FileSystemLoreStore>());
        var completion = new FakeEvalCompletionService();
        var toolLoop = CreateToolLoop(completion);
        var librarian = new LibrarianAgent(
            toolLoop,
            loreStore,
            new InMemoryPromptStore(),
            new AppConfig(),
            LoggerFactory.Create(_ => { }).CreateLogger<LibrarianAgent>());

        var runner = new LibrarianEvalRunner(
            librarian,
            loreStore,
            new LibrarianEvalScorer(),
            LoggerFactory.Create(_ => { }).CreateLogger<LibrarianEvalRunner>());

        var questions = new List<LibrarianEvalQuestion>
        {
            new()
            {
                Id = "test-01",
                Query = "What is Link's weapon?",
                ExpectedSources = ["characters/link.md"],
                ForbiddenSources = ["characters/link-dark.md"],
                ForbiddenFacts = ["cursed blade"],
            },
        };

        var results = await runner.RunAsync(questions, "default");

        Assert.Single(results);
        var result = results[0];
        Assert.Equal("test-01", result.QuestionId);
        Assert.NotNull(result.ParsedBundle);
        Assert.False(result.ParseFailed);
    }

    [Fact]
    public async Task RunAsync_LoadsSyntheticCorpusSuccessfully()
    {
        var corpusPath = Path.Combine(GetRepoRoot(), "tests", "QuillForge.LibrarianEval.Tests", "Fixtures", "synthetic-lore");
        var loreStore = new FileSystemLoreStore(corpusPath, LoggerFactory.Create(_ => { }).CreateLogger<FileSystemLoreStore>());
        var lore = await loreStore.LoadLoreSetAsync("default");

        Assert.True(lore.Count >= 6, $"Expected at least 6 lore files, got {lore.Count}");
        Assert.Contains("characters/link.md", lore.Keys);
        Assert.Contains("characters/link-dark.md", lore.Keys);
    }

    [Fact]
    public void ReportWriter_WritesAllArtifacts()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"qf-eval-test-{Guid.NewGuid()}");
        try
        {
            var questions = new List<LibrarianEvalQuestion>
            {
                new() { Id = "q1", Query = "test1" },
                new() { Id = "q2", Query = "test2" },
            };

            var results = new List<LibrarianEvalResult>
            {
                new()
                {
                    QuestionId = "q1",
                    Query = "test1",
                    RawResponse = "{}",
                    ParsedBundle = new LoreBundle { RelevantPassages = ["a"], SourceFiles = ["a.md"], Confidence = LoreConfidence.High },
                    ParseFailed = false,
                    Usage = new TokenUsage(1, 2),
                    Scores = new LibrarianEvalScores { OverallScore = 0.8 },
                },
                new()
                {
                    QuestionId = "q2",
                    Query = "test2",
                    RawResponse = "{}",
                    ParsedBundle = null,
                    ParseFailed = true,
                    Usage = new TokenUsage(0, 0),
                    Scores = new LibrarianEvalScores { OverallScore = 0.0 },
                },
            };

            var writer = new LibrarianEvalReportWriter();
            writer.WriteAll(outputDir, questions, results);

            Assert.True(File.Exists(Path.Combine(outputDir, "questions.jsonl")));
            Assert.True(File.Exists(Path.Combine(outputDir, "retrieval-trace.jsonl")));
            Assert.True(File.Exists(Path.Combine(outputDir, "answers.jsonl")));
            Assert.True(File.Exists(Path.Combine(outputDir, "evaluation.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "summary.md")));

            var evalJson = File.ReadAllText(Path.Combine(outputDir, "evaluation.json"));
            Assert.Contains("retrieval_failure_count", evalJson);
            Assert.Contains("synthesis_failure_count", evalJson);

            var summaryMd = File.ReadAllText(Path.Combine(outputDir, "summary.md"));
            Assert.Contains("Retrieval vs Synthesis Failures", summaryMd);
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResultClassification_SeparatesRetrievalAndSynthesisFailures()
    {
        var retrievalFailure = new LibrarianEvalResult
        {
            QuestionId = "q1",
            Query = "test1",
            RawResponse = "{}",
            ParsedBundle = new LoreBundle { RelevantPassages = ["wrong source text"], SourceFiles = ["wrong.md"], Confidence = LoreConfidence.High },
            ParseFailed = false,
            Usage = new TokenUsage(1, 2),
            Scores = new LibrarianEvalScores
            {
                CorrectSourceIncluded = 0.0,
                OffCharacterSourceExcluded = 1.0,
                NoForbiddenGraft = 1.0,
                OverallScore = 0.67,
            },
        };

        var synthesisFailure = new LibrarianEvalResult
        {
            QuestionId = "q2",
            Query = "test2",
            RawResponse = "{}",
            ParsedBundle = new LoreBundle { RelevantPassages = ["contains forbidden graft"], SourceFiles = ["right.md"], Confidence = LoreConfidence.High },
            ParseFailed = false,
            Usage = new TokenUsage(1, 2),
            Scores = new LibrarianEvalScores
            {
                CorrectSourceIncluded = 1.0,
                OffCharacterSourceExcluded = 1.0,
                NoForbiddenGraft = 0.0,
                OverallScore = 0.67,
            },
        };

        Assert.True(retrievalFailure.RetrievalFailed);
        Assert.False(retrievalFailure.SynthesisFailed);
        Assert.False(synthesisFailure.RetrievalFailed);
        Assert.True(synthesisFailure.SynthesisFailed);
    }

    private static ToolLoop CreateToolLoop(ICompletionService completion)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var continuation = new ContinuationStrategy(loggerFactory.CreateLogger<ContinuationStrategy>());
        var appConfig = new AppConfig
        {
            Timeouts = new TimeoutsConfig { CompletionTimeoutSeconds = 60, ToolExecutionSeconds = 30 },
            Diagnostics = new DiagnosticsConfig { LivePanel = false },
        };
        return new ToolLoop(completion, continuation, loggerFactory.CreateLogger<ToolLoop>(), appConfig);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuillForge.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // Last resort: return the test output directory
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Fake completion service that returns a simple LoreBundle-shaped JSON response.
    /// </summary>
    private sealed class FakeEvalCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent("""{"relevant_passages":["Link wields the hero's blade."],"source_files":["characters/link.md"],"confidence":"high"}"""),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(10, 20),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new TextDeltaEvent("""{"relevant_passages":["Link wields the hero's blade."],"source_files":["characters/link.md"],"confidence":"high"}""");
            yield return new DoneEvent(StopReason.EndTurn, new TokenUsage(10, 20));
            await Task.CompletedTask;
        }
    }

    private sealed class InMemoryPromptStore : ILibrarianPromptStore
    {
        public Task<string> LoadAsync(string? name, CancellationToken ct = default)
            => Task.FromResult("You are a precise lore retrieval specialist.");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}
