using System.Reflection;
using System.Xml.Linq;

namespace QuillForge.Architecture.Tests;

public class DependencyBoundaryTests
{
    private static readonly Assembly CoreAssembly = typeof(QuillForge.Core.Models.MessageNode).Assembly;
    private static readonly Assembly ProvidersAssembly = typeof(QuillForge.Providers.Registry.ProviderRegistry).Assembly;
    private static readonly Assembly StorageAssembly = typeof(QuillForge.Storage.FileSystem.FileSystemLoreStore).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(Den.Persistence.IPersistedDocumentStore<object>).Assembly;
    private static readonly Assembly RulesEngineAssembly = typeof(Den.RulesEngine.RulesEngineAssemblyMarker).Assembly;
    private static readonly Assembly WerewolfAssembly = typeof(Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker).Assembly;

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Core_DoesNot_Reference_Providers()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Providers");
    }

    [Fact]
    public void Core_DoesNot_Reference_Storage()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Storage");
    }

    [Fact]
    public void Core_DoesNot_Reference_Web()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Core_ReferencesOnlyPortableRulesEngineContractsForGameRuntimeState()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();

        Assert.Contains(referenced, a => a.Name == "Den.RulesEngine");
        Assert.DoesNotContain(referenced, a => a.Name == "Den.RulesEngine.Werewolf");
    }

    [Fact]
    public void Providers_DoesNot_Reference_Storage()
    {
        var referenced = ProvidersAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Storage");
    }

    [Fact]
    public void Providers_DoesNot_Reference_Web()
    {
        var referenced = ProvidersAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Storage_DoesNot_Reference_Providers()
    {
        var referenced = StorageAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Providers");
    }

    [Fact]
    public void Storage_DoesNot_Reference_Web()
    {
        var referenced = StorageAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Persistence_DoesNot_Reference_Core()
    {
        var referenced = PersistenceAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Core");
    }

    [Fact]
    public void Persistence_DoesNot_Reference_Storage()
    {
        var referenced = PersistenceAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Storage");
    }

    [Fact]
    public void Persistence_DoesNot_Reference_Providers()
    {
        var referenced = PersistenceAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Providers");
    }

    [Fact]
    public void Persistence_DoesNot_Reference_Web()
    {
        var referenced = PersistenceAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Solution_Includes_RulesEngineProjectsExplicitly()
    {
        var solution = XDocument.Load(Path.Combine(RepoRoot, "QuillForge.slnx"));
        var projectPaths = solution.Descendants("Project")
            .Select(element => (string?)element.Attribute("Path") ?? string.Empty)
            .ToArray();

        Assert.Contains("src/Den.RulesEngine/Den.RulesEngine.csproj", projectPaths);
        Assert.Contains("src/Den.RulesEngine.Werewolf/Den.RulesEngine.Werewolf.csproj", projectPaths);
        Assert.Contains("tests/Den.RulesEngine.Tests/Den.RulesEngine.Tests.csproj", projectPaths);
        Assert.Contains("tests/Den.RulesEngine.Werewolf.Tests/Den.RulesEngine.Werewolf.Tests.csproj", projectPaths);
    }

    [Fact]
    public void RulesEngine_DoesNot_Reference_QuillForgeOrHostFrameworks()
    {
        AssertNoReferencesStartingWith(
            RulesEngineAssembly,
            "QuillForge",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions.AI",
            "OpenAI",
            "Anthropic",
            "Azure.AI",
            "ModelContextProtocol");
    }

    [Fact]
    public void RulesEngine_DoesNot_Expose_QuillForgeNamespaces()
    {
        AssertNoTypeNamespacesStartingWith(RulesEngineAssembly, "QuillForge");
    }

    [Fact]
    public void RulesEngine_Source_DoesNotUseAdapterJsonOrUntypedDictionaryBehavior()
    {
        var sourceFiles = Directory.GetFiles(Path.Combine(RepoRoot, "src", "Den.RulesEngine"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var sourceText = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("JsonElement", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("IDictionary<", sourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesEngine_Project_HasNoProjectPackageOrFrameworkReferences()
    {
        var project = LoadProject("src/Den.RulesEngine/Den.RulesEngine.csproj");

        Assert.Empty(ProjectReferenceIncludes(project));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    [Fact]
    public void SingletonAgents_DoNotCacheAppConfigModelAliasesAtConstruction()
    {
        var agentsRoot = Path.Combine(RepoRoot, "src", "QuillForge.Core", "Agents");
        var sourceFiles = Directory.GetFiles(agentsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        foreach (var sourceFile in sourceFiles)
        {
            var sourceText = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("private readonly string _model", sourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("private readonly int _maxToolRounds", sourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("= appConfig.Models", sourceText, StringComparison.Ordinal);
            Assert.DoesNotContain("= appConfig.Agents.Orchestrator", sourceText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GameCoreServices_DoNotUseRawJsonNavigationForGameplayContracts()
    {
        var coreServicesRoot = Path.Combine(RepoRoot, "src", "QuillForge.Core", "Services");
        var sourceFiles = Directory.GetFiles(coreServicesRoot, "Game*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(Path.Combine(RepoRoot, "src", "QuillForge.Core", "Models"), "Game*.cs", SearchOption.TopDirectoryOnly))
            .Concat([
                Path.Combine(coreServicesRoot, "AgentVisibleEventsService.cs"),
                Path.Combine(coreServicesRoot, "ParticipantChannelService.cs"),
            ])
            .ToArray();
        var sourceText = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("JsonElement", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonNode", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetProperty(", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("dynamic", sourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void GameSessionEndpoints_AreTypedThinAdapters()
    {
        var sourceText = File.ReadAllText(Path.Combine(RepoRoot, "src", "QuillForge.Web", "Endpoints", "GameEndpoints.cs"));

        Assert.Contains("IGameBridgeService", sourceText, StringComparison.Ordinal);
        Assert.Contains("IGameInspectorService", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("ISessionStateStore", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("RulesEngineService", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GameRuntimeState", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonElement", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetProperty(", sourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void WerewolfModule_DoesNot_Reference_QuillForgeOrHostFrameworks()
    {
        Assert.Contains(
            WerewolfAssembly.GetReferencedAssemblies(),
            reference => reference.Name == "Den.RulesEngine");

        AssertNoReferencesStartingWith(
            WerewolfAssembly,
            "QuillForge",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions.AI",
            "OpenAI",
            "Anthropic",
            "Azure.AI",
            "ModelContextProtocol");
    }

    [Fact]
    public void WerewolfModule_DoesNot_Expose_QuillForgeNamespaces()
    {
        AssertNoTypeNamespacesStartingWith(WerewolfAssembly, "QuillForge");
    }

    [Fact]
    public void WerewolfModule_ProjectReferenceGraph_IsExplicitAndEngineOnly()
    {
        var project = LoadProject("src/Den.RulesEngine.Werewolf/Den.RulesEngine.Werewolf.csproj");

        Assert.Equal(
            ["../Den.RulesEngine/Den.RulesEngine.csproj"],
            ProjectReferenceIncludes(project));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    private static XDocument LoadProject(string relativePath)
    {
        return XDocument.Load(Path.Combine(RepoRoot, relativePath));
    }

    private static string[] ProjectReferenceIncludes(XDocument project)
    {
        return project.Descendants("ProjectReference")
            .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertNoReferencesStartingWith(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var forbidden = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbidden);
    }

    private static void AssertNoTypeNamespacesStartingWith(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var forbidden = assembly.GetTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Where(ns => forbiddenPrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbidden);
    }
}
