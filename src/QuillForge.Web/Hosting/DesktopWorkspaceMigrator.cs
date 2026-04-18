using Microsoft.Extensions.Logging;

namespace QuillForge.Web.Hosting;

internal sealed record WorkspaceMigrationResult(
    string SourceContentRoot,
    string TargetContentRoot,
    int CopiedFileCount);

internal static class DesktopWorkspaceMigrator
{
    public static WorkspaceMigrationResult? ImportIfNeeded(
        WorkspaceMigrationPlan? migrationPlan,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (migrationPlan is null)
        {
            return null;
        }

        var sourceRoot = migrationPlan.SourceContentRoot;
        var targetRoot = migrationPlan.TargetContentRoot;

        logger.LogInformation(
            "Importing legacy portable workspace from {SourceRoot} to desktop workspace {TargetRoot}",
            sourceRoot,
            targetRoot);

        Directory.CreateDirectory(targetRoot);

        foreach (var sourceDirectory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceRoot, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativeDirectory));
        }

        var copiedFileCount = 0;
        foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var targetPath = Path.Combine(targetRoot, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (!File.Exists(targetPath))
            {
                File.Copy(sourceFile, targetPath);
                copiedFileCount++;
            }
        }

        logger.LogInformation(
            "Imported {CopiedFileCount} files from legacy portable workspace {SourceRoot} into {TargetRoot}",
            copiedFileCount,
            sourceRoot,
            targetRoot);

        return new WorkspaceMigrationResult(sourceRoot, targetRoot, copiedFileCount);
    }
}
