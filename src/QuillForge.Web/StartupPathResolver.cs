using Microsoft.Extensions.Configuration;

namespace QuillForge.Web;

internal sealed record StartupPaths(
    string? SolutionRoot,
    string ContentRoot,
    string DefaultsPath,
    string DocsRoot);

internal static class StartupPathResolver
{
    public static StartupPaths Resolve(
        IConfiguration configuration,
        string appBaseDirectory,
        string currentDirectory)
    {
        var solutionRoot = FindSolutionRoot(appBaseDirectory)
            ?? FindSolutionRoot(currentDirectory);

        var contentRoot = configuration.GetValue<string>("QuillForge:ContentRoot")
            ?? (solutionRoot is not null
                ? Path.Combine(solutionRoot, "user")
                : Path.Combine(appBaseDirectory, "user"));

        var defaultsPath = solutionRoot is not null
            ? Path.Combine(solutionRoot, "dev", "defaults")
            : Path.Combine(appBaseDirectory, "dev", "defaults");

        var docsRoot = solutionRoot is not null
            ? Path.Combine(solutionRoot, "dev", "app-docs")
            : Path.Combine(appBaseDirectory, "app-docs");

        return new StartupPaths(solutionRoot, contentRoot, defaultsPath, docsRoot);
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
}
