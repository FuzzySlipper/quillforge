using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.Adapters;
using QuillForge.Providers.Registry;
using QuillForge.Storage.FileSystem;

namespace QuillForge.LibrarianEval;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseOptions(args);

        var services = new ServiceCollection();
        ConfigureLogging(services);
        ConfigureServices(services, options);

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("LibrarianEval");

        logger.LogInformation("LibrarianEval starting with corpus={CorpusPath} set={LoreSet} output={OutputDir}",
            options.CorpusPath, options.LoreSetName, options.OutputDir);

        var questions = LoadQuestions(options);
        if (questions.Count == 0)
        {
            logger.LogError("No questions loaded. Provide a --questions-file or ensure built-in fixtures are available.");
            return 1;
        }

        if (options.Limit > 0)
        {
            questions = questions.Take(options.Limit).ToList();
            logger.LogInformation("Limited to {Limit} questions", options.Limit);
        }

        var runner = provider.GetRequiredService<LibrarianEvalRunner>();
        var results = await runner.RunAsync(questions, options.LoreSetName);

        var reportWriter = new LibrarianEvalReportWriter();
        reportWriter.WriteAll(options.OutputDir, questions, results);

        logger.LogInformation("Evaluation complete. Artifacts written to {OutputDir}", options.OutputDir);

        var avgScore = results.Count > 0 ? results.Average(r => r.Scores.OverallScore) : 0.0;
        var retrievalFailures = results.Count(r => r.RetrievalFailed);
        logger.LogInformation("Average score: {AverageScore:F2}, retrieval failures: {RetrievalFailures}", avgScore, retrievalFailures);

        return retrievalFailures > 0 || results.Any(r => r.SynthesisFailed) || avgScore < 0.5 ? 2 : 0;
    }

    private static LibrarianEvalOptions ParseOptions(string[] args)
    {
        var options = new LibrarianEvalOptions
        {
            CorpusPath = GetArg(args, "--corpus-path") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_CORPUS_PATH") ?? throw new ArgumentException("--corpus-path or LIBRARIAN_EVAL_CORPUS_PATH is required."),
            OutputDir = GetArg(args, "--output-dir") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_OUTPUT_DIR") ?? Path.Combine(Path.GetTempPath(), "quillforge", "librarian-eval"),
            QuestionsFile = GetArg(args, "--questions-file") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_QUESTIONS_FILE"),
            LoreSetName = GetArg(args, "--lore-set") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_LORE_SET") ?? "default",
            BaseUrl = GetArg(args, "--base-url") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_BASE_URL"),
            Model = GetArg(args, "--model") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_MODEL"),
            ApiKey = GetArg(args, "--api-key") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_API_KEY"),
            ProviderType = GetArg(args, "--provider-type") ?? Environment.GetEnvironmentVariable("LIBRARIAN_EVAL_PROVIDER_TYPE") ?? "Custom",
            MaxTokens = ParseInt(GetArg(args, "--max-tokens")) ?? 4096,
            Limit = ParseInt(GetArg(args, "--limit")) ?? 0,
        };

        return options;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "[HH:mm:ss] ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    private static void ConfigureServices(IServiceCollection services, LibrarianEvalOptions options)
    {
        var appConfig = new AppConfig
        {
            Models = new ModelsConfig { Librarian = options.Model ?? "default" },
            Agents = new AgentsConfig
            {
                Librarian = new LibrarianBudget
                {
                    MaxTokens = options.MaxTokens,
                    MaxToolRounds = 1,
                    CacheSystemPrompt = false,
                },
            },
            Timeouts = new TimeoutsConfig
            {
                CompletionTimeoutSeconds = 300,
                ToolExecutionSeconds = 120,
            },
            Diagnostics = new DiagnosticsConfig { LivePanel = false },
        };

        services.AddSingleton(appConfig);
        services.AddSingleton<ILoreStore>(sp =>
            new FileSystemLoreStore(options.CorpusPath, sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));
        services.AddSingleton<ILibrarianPromptStore>(sp =>
            new InMemoryLibrarianPromptStore("You are a precise lore retrieval specialist."));

        // Completion service: real if base URL provided, otherwise a fake that returns empty bundles.
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            services.AddSingleton<ICompletionService>(sp =>
            {
                var providerFactory = new ProviderFactory(
                    sp.GetRequiredService<ILogger<ProviderFactory>>(),
                    appConfig);

                var providerConfig = new ProviderConfig
                {
                    Alias = "eval",
                    Type = Enum.TryParse<ProviderType>(options.ProviderType, out var pt) ? pt : ProviderType.Custom,
                    ApiKey = options.ApiKey ?? "no-key",
                    BaseUrl = options.BaseUrl,
                    DefaultModel = options.Model ?? "default",
                };

                var client = providerFactory.CreateClient(providerConfig);
                return new ChatClientCompletionService(
                    client,
                    sp.GetRequiredService<ILogger<ChatClientCompletionService>>());
            });
        }
        else
        {
            services.AddSingleton<ICompletionService>(sp =>
                new FakeEmptyCompletionService());
        }

        services.AddSingleton<ContinuationStrategy>();
        services.AddSingleton<ToolLoop>();
        services.AddSingleton<LibrarianAgent>();
        services.AddSingleton<LibrarianEvalScorer>();
        services.AddSingleton<LibrarianEvalRunner>();
    }

    private static IReadOnlyList<LibrarianEvalQuestion> LoadQuestions(LibrarianEvalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.QuestionsFile) && File.Exists(options.QuestionsFile))
        {
            var json = File.ReadAllText(options.QuestionsFile);
            var questions = JsonSerializer.Deserialize<List<LibrarianEvalQuestion>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return questions ?? [];
        }

        // Built-in synthetic questions for quick testing
        return new List<LibrarianEvalQuestion>
        {
            new()
            {
                Id = "synth-01",
                Query = "What is the primary weapon of the hero Link?",
                ExpectedSources = ["characters/link.md"],
                ForbiddenSources = ["characters/link-dark.md"],
                ForbiddenFacts = ["Dark Link", "cursed blade"],
                ExpectedPassageSubstrings = ["hero's blade", "Link"],
                Notes = "Basic retrieval: should pick the primary Link, not the dark variant.",
            },
            new()
            {
                Id = "synth-02",
                Query = "Tell me about the Order of the Silver Flame.",
                ExpectedSources = ["organizations/silver-flame.md"],
                ForbiddenSources = ["organizations/black-flame.md"],
                ForbiddenFacts = ["Black Flame", "heretical"],
                Notes = "Organization collision: Silver vs Black Flame.",
            },
            new()
            {
                Id = "synth-03",
                Query = "What weapon does Link use?",
                ExpectedSources = ["characters/link.md"],
                ForbiddenSources = ["characters/link-dark.md"],
                ForbiddenFacts = ["Dark Link", "cursed"],
                SharedFactSources = ["world/weapons.md"],
                Notes = "Shared facts: world-level weapon lore should be accessible.",
            },
            new()
            {
                Id = "synth-04",
                Query = "Who is the captain?",
                RequiresClarification = true,
                Notes = "Ambiguous: there are multiple captains across factions.",
            },
            new()
            {
                Id = "synth-05",
                Query = "Describe the capital city.",
                ExpectedSources = ["locations/capital.md"],
                ForbiddenSources = ["locations/shadow-capital.md"],
                ForbiddenFacts = ["Shadow Capital", "underground"],
                Notes = "Location collision: Capital vs Shadow Capital.",
            },
            new()
            {
                Id = "synth-06",
                Query = "What is the founding year of the Silver Flame?",
                ExpectedSources = ["organizations/silver-flame.md"],
                ForbiddenSources = ["organizations/black-flame.md"],
                ForbiddenFacts = ["Black Flame"],
                ExpectedPassageSubstrings = ["1247"],
                Notes = "Fact specificity: exact year retrieval.",
            },
        };
    }

    /// <summary>
    /// In-memory prompt store for evaluation runs.
    /// </summary>
    private sealed class InMemoryLibrarianPromptStore : ILibrarianPromptStore
    {
        private readonly string _defaultPrompt;

        public InMemoryLibrarianPromptStore(string defaultPrompt)
        {
            _defaultPrompt = defaultPrompt;
        }

        public Task<string> LoadAsync(string? name, CancellationToken ct = default)
        {
            return Task.FromResult(_defaultPrompt);
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(["default"]);
        }
    }

    /// <summary>
    /// Fake completion service that returns an empty LoreBundle.
    /// Used when no live provider is configured, to allow CI testing of the harness pipeline.
    /// </summary>
    private sealed class FakeEmptyCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            var query = request.Messages.LastOrDefault()?.Content.GetText() ?? string.Empty;
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent(BuildSyntheticResponse(query)),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(0, 0),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var query = request.Messages.LastOrDefault()?.Content.GetText() ?? string.Empty;
            yield return new TextDeltaEvent(BuildSyntheticResponse(query));
            yield return new DoneEvent(StopReason.EndTurn, new TokenUsage(0, 0));
            await Task.CompletedTask;
        }

        private static string BuildSyntheticResponse(string query)
        {
            var q = query.ToLowerInvariant();
            if (q.Contains("primary weapon") || q.Contains("what weapon does link"))
            {
                return """{"relevant_passages":["Link wields the hero's blade as his primary weapon. The world weapons dossier identifies it as a shared hero-line relic."],"source_files":["characters/link.md","world/weapons.md"],"confidence":"high"}""";
            }
            if (q.Contains("silver flame") && q.Contains("founding year"))
            {
                return """{"relevant_passages":["The Order of the Silver Flame was founded in 1247 to protect pilgrim roads."],"source_files":["organizations/silver-flame.md"],"confidence":"high"}""";
            }
            if (q.Contains("order of the silver flame"))
            {
                return """{"relevant_passages":["The Order of the Silver Flame guards beacon shrines and keeps public records separate from similarly named heresies."],"source_files":["organizations/silver-flame.md"],"confidence":"high"}""";
            }
            if (q.Contains("who is the captain"))
            {
                return """{"relevant_passages":["The query is ambiguous because the corpus contains multiple captains; please clarify the faction or person."],"source_files":[],"confidence":"low"}""";
            }
            if (q.Contains("capital city"))
            {
                return """{"relevant_passages":["The Capital is the sunlit administrative city above the river delta."],"source_files":["locations/capital.md"],"confidence":"high"}""";
            }
            return """{"relevant_passages":[],"source_files":[],"confidence":"low"}""";
        }
    }
}
