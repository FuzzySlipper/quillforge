using Microsoft.Extensions.Logging;
using QuillForge.Core;

namespace QuillForge.Storage.Configuration;

/// <summary>
/// Detects fresh installations and creates the default build/ directory structure.
/// Populates from dev/defaults if available, otherwise creates minimal stubs.
/// </summary>
public sealed class FirstRunSetup
{
    private readonly ILogger<FirstRunSetup> _logger;

    private static readonly string[] ContentDirectories = ContentPaths.AllDirectories;

    public FirstRunSetup(ILogger<FirstRunSetup> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensures the content directory structure exists. Creates missing directories and defaults.
    /// Returns true if this was a first run (no existing content directory).
    /// </summary>
    public bool EnsureContentDirectory(string contentRoot, string? defaultsPath = null)
    {
        var isFirstRun = !Directory.Exists(contentRoot);

        foreach (var dir in ContentDirectories)
        {
            var path = Path.Combine(contentRoot, dir);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogDebug("Created directory: {Path}", path);
            }
        }

        if (isFirstRun)
        {
            _logger.LogInformation("First run detected. Created content directory at {Path}", contentRoot);

            if (defaultsPath is not null && Directory.Exists(defaultsPath))
            {
                CopyDefaults(defaultsPath, contentRoot);
            }
            else
            {
                CreateMinimalDefaults(contentRoot);
            }

            // Always create config.yaml if missing
            var configPath = Path.Combine(contentRoot, ContentPaths.ConfigFile);
            if (!File.Exists(configPath))
            {
                var configLoader = new ConfigurationLoader(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
                        .CreateLogger<ConfigurationLoader>());
                configLoader.WriteDefaults(configPath);
            }
        }

        return isFirstRun;
    }

    /// <summary>
    /// Copies the full dev/defaults directory tree into the content root.
    /// Preserves directory structure. Skips files that already exist.
    /// </summary>
    private void CopyDefaults(string defaultsPath, string contentRoot)
    {
        var fileCount = 0;

        foreach (var sourceFile in Directory.GetFiles(defaultsPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(defaultsPath, sourceFile);
            var targetPath = Path.Combine(contentRoot, relativePath);

            if (File.Exists(targetPath))
                continue;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            File.Copy(sourceFile, targetPath);
            fileCount++;
        }

        _logger.LogInformation(
            "Copied {Count} default files from {Source} to {Target}",
            fileCount, defaultsPath, contentRoot);
    }

    /// <summary>
    /// Creates minimal stub files when no defaults directory is available.
    /// </summary>
    private void CreateMinimalDefaults(string contentRoot)
    {
        WriteIfMissing(Path.Combine(contentRoot, ContentPaths.Conductor, "default.md"), """
            # Default Conductor

            You are the coordination layer for QuillForge.

            Operational rules:
            - Do not adopt a separate assistant persona unless the user explicitly asks for one.
            - Route to the right capability before answering from general intuition.
            - In roleplay, stay transparent: the scene, characters, and prose carry the voice, not you.
            - Keep direct non-fiction responses clear, concise, and task-focused.
            - If a tool or dependency fails, say so plainly instead of hiding the failure.
            """);

        WriteIfMissing(Path.Combine(contentRoot, ContentPaths.NarrativeRules, "default.md"), """
            # Default Narrative Rules

            Direct the scene with coherent consequences, responsive NPC behavior, and steady momentum.

            Rules:
            - Respect established lore, character motives, and the current story state.
            - Let user actions matter; do not negate them without an in-world cause.
            - Escalate tension through consequences, complications, and new pressure rather than random chaos.
            - Preserve continuity of injuries, promises, discoveries, and scene geography.
            - When the scene changes materially, update story state to reflect it.
            """);

        WriteIfMissing(Path.Combine(contentRoot, ContentPaths.Plots, "default.md"), """
            # Default Plot Arc

            ## Premise
            A protagonist is forced into a dangerous choice that reshapes their relationships and future.

            ## Beats
            - Opening equilibrium and the pressure already building under it
            - Inciting incident that removes the easy option
            - Rising complications that test loyalty, desire, and survival
            - Major reversal that exposes hidden costs
            - Climactic choice with lasting consequences
            - Aftermath that shows what changed

            ## Character Arcs
            - Protagonist: moves from hesitation to costly conviction
            - Closest ally: trust is strained, then redefined
            - Primary opposing force: grows more dangerous as the protagonist commits

            ## Tension Curve
            Start intimate, escalate through irreversible consequences, and peak at a choice that cannot be undone.
            """);

        WriteIfMissing(Path.Combine(contentRoot, ContentPaths.WritingStyles, "default.md"), """
            Write in a clear, engaging literary style. Use vivid sensory details and strong verbs.
            Vary sentence length for rhythm. Show, don't tell. Use dialogue to reveal character.
            Maintain a consistent narrative voice throughout.
            """);

        WriteIfMissing(Path.Combine(contentRoot, ContentPaths.Profiles, "default.yaml"), """
            conductor: default
            lore_set: default
            narrative_rules: default
            writing_style: default
            """);

        _logger.LogInformation("Created minimal default content files");
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }
}
