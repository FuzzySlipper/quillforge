using Microsoft.Extensions.Configuration;
using QuillForge.Core;

namespace QuillForge.Web;

internal enum StartupContentRootKind
{
    ExplicitOverride,
    SourceDevelopment,
    PortablePublished,
    DesktopDefaultDocuments,
}

internal sealed record WorkspaceMigrationPlan(
    string SourceContentRoot,
    string TargetContentRoot);

internal sealed record StartupPaths(
    string? SolutionRoot,
    string ContentRoot,
    string DefaultsPath,
    string DocsRoot,
    StartupContentRootKind ContentRootKind = StartupContentRootKind.PortablePublished,
    WorkspaceMigrationPlan? MigrationPlan = null);

internal static class StartupPathResolver
{
    public static StartupPaths Resolve(
        IConfiguration configuration,
        string appBaseDirectory,
        string currentDirectory,
        string? documentsDirectoryOverride = null)
    {
        var solutionRoot = FindSolutionRoot(appBaseDirectory)
            ?? FindSolutionRoot(currentDirectory);

        var desktopMode = configuration.GetValue<bool>("QuillForge:Startup:DesktopMode");
        var explicitContentRoot = configuration.GetValue<string>("QuillForge:ContentRoot");

        var defaultsPath = solutionRoot is not null
            ? Path.Combine(solutionRoot, "dev", "defaults")
            : Path.Combine(appBaseDirectory, "dev", "defaults");

        var docsRoot = solutionRoot is not null
            ? Path.Combine(solutionRoot, "dev", "app-docs")
            : Path.Combine(appBaseDirectory, "app-docs");

        if (!string.IsNullOrWhiteSpace(explicitContentRoot))
        {
            return new StartupPaths(
                solutionRoot,
                explicitContentRoot,
                defaultsPath,
                docsRoot,
                StartupContentRootKind.ExplicitOverride,
                MigrationPlan: null);
        }

        if (solutionRoot is not null)
        {
            return new StartupPaths(
                solutionRoot,
                Path.Combine(solutionRoot, "user"),
                defaultsPath,
                docsRoot,
                StartupContentRootKind.SourceDevelopment,
                MigrationPlan: null);
        }

        if (desktopMode)
        {
            var documentsDirectory = ResolveDocumentsDirectory(documentsDirectoryOverride);
            var desktopContentRoot = Path.Combine(documentsDirectory, "QuillForge");
            var legacyPortableRoot = Path.Combine(appBaseDirectory, "user");

            return new StartupPaths(
                solutionRoot,
                desktopContentRoot,
                defaultsPath,
                docsRoot,
                StartupContentRootKind.DesktopDefaultDocuments,
                BuildMigrationPlan(legacyPortableRoot, desktopContentRoot));
        }

        return new StartupPaths(
            solutionRoot,
            Path.Combine(appBaseDirectory, "user"),
            defaultsPath,
            docsRoot,
            StartupContentRootKind.PortablePublished,
            MigrationPlan: null);
    }

    public static string ResolveDocumentsDirectory(string? documentsDirectoryOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(documentsDirectoryOverride))
        {
            return documentsDirectoryOverride;
        }

        var xdgDocuments = ExpandUserPath(Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR"));
        if (!string.IsNullOrWhiteSpace(xdgDocuments) && Path.IsPathRooted(xdgDocuments))
        {
            return xdgDocuments;
        }

        var platformDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(platformDocuments))
        {
            return platformDocuments;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Unable to resolve a desktop Documents directory for QuillForge.");
        }

        return Path.Combine(userProfile, "Documents");
    }

    public static string? FindSolutionRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "QuillForge.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static WorkspaceMigrationPlan? BuildMigrationPlan(string sourceContentRoot, string targetContentRoot)
    {
        if (PathsEqual(sourceContentRoot, targetContentRoot))
        {
            return null;
        }

        if (!LooksLikeContentRoot(sourceContentRoot))
        {
            return null;
        }

        if (DirectoryHasEntries(targetContentRoot))
        {
            return null;
        }

        return new WorkspaceMigrationPlan(sourceContentRoot, targetContentRoot);
    }

    private static bool LooksLikeContentRoot(string contentRoot)
    {
        if (!Directory.Exists(contentRoot))
        {
            return false;
        }

        if (File.Exists(Path.Combine(contentRoot, ContentPaths.ConfigFile)))
        {
            return true;
        }

        foreach (var relativeDirectory in ContentPaths.AllDirectories)
        {
            if (Directory.Exists(Path.Combine(contentRoot, relativeDirectory)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DirectoryHasEntries(string path)
    {
        return Directory.Exists(path)
            && Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? ExpandUserPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return path;
        }

        if (path.StartsWith("$HOME", StringComparison.Ordinal))
        {
            return Path.Combine(userProfile, path["$HOME".Length..].TrimStart('/', '\\'));
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || string.Equals(path, "~", StringComparison.Ordinal))
        {
            return Path.Combine(userProfile, path[1..].TrimStart('/', '\\'));
        }

        return path;
    }
}
