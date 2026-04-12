using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Architecture.Tests.Scenarios;

/// <summary>
/// Discovers .scenario.yaml files and runs each as an xUnit test case
/// through the in-process scenario runner with fake LLM responses.
/// </summary>
public sealed class ScenarioTests : IDisposable
{
    private readonly string _tempDir;

    public ScenarioTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "sessions"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "session-state"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "conductor"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "writing-styles"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "lore"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "profiles"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "story"));
        File.WriteAllText(Path.Combine(_tempDir, "conductor", "default.md"), "You are a helpful assistant.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [MemberData(nameof(GetScenarioFiles))]
    public async Task RunScenario(string scenarioPath)
    {
        var yaml = await File.ReadAllTextAsync(scenarioPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        var scenario = deserializer.Deserialize<ScenarioDefinition>(yaml);

        var (runner, _) = BuildRunner();
        var report = await runner.RunAsync(scenario);

        // Assert all steps passed
        foreach (var step in report.Steps)
        {
            Assert.True(step.Passed,
                $"Scenario '{scenario.Name}' step {step.StepIndex} ({step.Action}) failed:\n" +
                string.Join("\n", step.Failures));
        }

        Assert.True(report.Passed, $"Scenario '{scenario.Name}' had failures.");
    }

    public static IEnumerable<object[]> GetScenarioFiles()
    {
        var scenariosDir = FindScenariosDirectory();
        if (scenariosDir is null || !Directory.Exists(scenariosDir))
            yield break;

        foreach (var file in Directory.GetFiles(scenariosDir, "*.scenario.yaml"))
        {
            yield return [file];
        }
    }

    private static string? FindScenariosDirectory()
    {
        // Walk up from the test assembly directory to find tests/scenarios/
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "tests", "scenarios");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return null;
    }

    private (ScenarioRunner runner, ScriptedCompletionService completionService) BuildRunner()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var completionService = new ScriptedCompletionService();
        var continuation = new ContinuationStrategy(loggerFactory.CreateLogger<ContinuationStrategy>());
        var appConfig = new AppConfig { Diagnostics = new DiagnosticsConfig { LivePanel = true } };
        var toolLoop = new ToolLoop(completionService, continuation,
            loggerFactory.CreateLogger<ToolLoop>(), appConfig);

        var modes = new IMode[]
        {
            new GuideMode(),
            new WriterMode(),
        };

        var assistantPromptStore = new FakeAssistantPromptStore();
        var sessionContextService = new NoOpSessionContextService();

        var orchestrator = new OrchestratorAgent(
            toolLoop, modes, assistantPromptStore, sessionContextService, appConfig,
            loggerFactory.CreateLogger<OrchestratorAgent>());

        var atomicWriter = new QuillForge.Storage.Utilities.AtomicFileWriter(
            loggerFactory.CreateLogger<QuillForge.Storage.Utilities.AtomicFileWriter>());
        var sessionStore = new QuillForge.Storage.FileSystem.FileSystemSessionStore(
            _tempDir, atomicWriter, loggerFactory.CreateLogger<QuillForge.Storage.FileSystem.FileSystemSessionStore>(), loggerFactory);
        var runtimeStore = new QuillForge.Storage.FileSystem.FileSystemSessionRuntimeStore(
            _tempDir, atomicWriter, loggerFactory.CreateLogger<QuillForge.Storage.FileSystem.FileSystemSessionRuntimeStore>());
        var gate = new QuillForge.Core.Services.InMemorySessionMutationGate(
            loggerFactory.CreateLogger<QuillForge.Core.Services.InMemorySessionMutationGate>());
        var profileService = new NoOpProfileConfigService();
        var runtimeService = new SessionRuntimeService(
            runtimeStore, gate, profileService, modes,
            loggerFactory.CreateLogger<SessionRuntimeService>());
        var bootstrapService = new SessionBootstrapService(
            sessionStore, runtimeStore, profileService, loggerFactory, loggerFactory.CreateLogger<SessionBootstrapService>());
        var profileReadService = new FakeProfileReadService(runtimeService);

        IToolHandler[] tools = [];

        var runner = new ScenarioRunner(
            orchestrator, runtimeService, bootstrapService, sessionStore,
            profileReadService, tools, loggerFactory.CreateLogger<ScenarioRunner>());

        return (runner, completionService);
    }

    /// <summary>
    /// A completion service that returns canned responses for scenario testing.
    /// Returns incrementing text responses for each call.
    /// </summary>
    private sealed class ScriptedCompletionService : ICompletionService
    {
        private int _callCount;

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            var count = Interlocked.Increment(ref _callCount);
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent($"Scripted response #{count}. I can help with many things including world-building and storytelling."),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(50, 100),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class FakeConductorStore : IConductorStore
    {
        private readonly string _contentRoot;
        public FakeConductorStore(string contentRoot) { _contentRoot = contentRoot; }
        public Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default)
        {
            var path = Path.Combine(_contentRoot, "conductor", $"{conductorName}.md");
            return File.Exists(path) ? File.ReadAllTextAsync(path, ct) : Task.FromResult("You are a helpful assistant.");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);
    }

    private sealed class NoOpSessionContextService : IInteractiveSessionContextService
    {
        public Task<InteractiveSessionContext> BuildAsync(SessionState state, CancellationToken ct = default)
        {
            return Task.FromResult(new InteractiveSessionContext
            {
                ActiveMode = state.Mode.ActiveMode,
                ProjectName = state.Mode.ProjectName ?? "default",
                StoryStatePath = "default/.state.yaml",
            });
        }

        public Task<InteractiveSessionContext> LoadAsync(Guid? sessionId, CancellationToken ct = default)
        {
            return Task.FromResult(new InteractiveSessionContext
            {
                ActiveMode = Mode.Guide,
                ProjectName = "default",
                StoryStatePath = "default/.state.yaml",
            });
        }
    }

    private sealed class NoOpProfileConfigService : IProfileConfigService
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);

        public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
            => Task.FromResult("default");

        public Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProfileConfig
            {
                ProfileId = profileId ?? "default",
                Config = new ProfileConfig(),
                Persisted = false,
            });

        public Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProfileConfig
            {
                ProfileId = profileId,
                Config = config,
                Persisted = true,
            });

        public Task<ResolvedProfileConfig> CloneAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string profileId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
            => Task.FromResult(new ProfileState
            {
                ProfileId = profileId ?? "default",
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                ActiveWritingStyle = "default",
            });
    }

    private sealed class FakeProfileReadService : ISessionProfileReadService
    {
        private readonly ISessionStateService _runtimeService;
        public FakeProfileReadService(ISessionStateService runtimeService)
        {
            _runtimeService = runtimeService;
        }

        public Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QuillForge.Web.Contracts.ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async Task<PreparedInteractiveRequest> PrepareInteractiveRequestAsync(
            Guid? sessionId, PrepareInteractiveRequestOptions options, CancellationToken ct = default)
        {
            var state = await _runtimeService.LoadViewAsync(sessionId, ct);
            var resolvedSessionId = sessionId ?? Guid.CreateVersion7();
            var sessionContext = new InteractiveSessionContext
            {
                ActiveMode = state.Mode.ActiveMode,
                ProjectName = state.Mode.ProjectName ?? "default",
                StoryStatePath = "default/.state.yaml",
            };

            return new PreparedInteractiveRequest
            {
                ProfileView = new SessionProfileReadView
                {
                    SessionState = state,
                    DefaultProfileId = "default",
                    ActiveProfileId = "default",
                    ActiveLoreSet = "default",
                    ActiveNarrativeRules = "default",
                    ActiveWritingStyle = "default",
                    ActiveLibrarianPrompt = "default",
                },
                SessionContext = sessionContext,
                AgentContext = new AgentContext
                {
                    SessionId = resolvedSessionId,
                    ActiveMode = state.Mode.ActiveMode,
                    ActiveLoreSet = "default",
                    ActiveWritingStyle = "default",
                    ActiveNarrativeRules = "default",
                    SessionContext = sessionContext,
                },
            };
        }
    }

    private sealed class FakeAssistantPromptStore : IAssistantPromptStore
    {
        public Task<string> LoadAsync(string promptName, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}
