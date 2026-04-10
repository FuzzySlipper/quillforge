namespace QuillForge.Storage.Utilities;

internal static class PathBoundaryGuard
{
    public static bool TryResolvePath(string rootPath, string relativePath, out string resolvedPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        resolvedPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));

        var relativeToRoot = Path.GetRelativePath(normalizedRoot, resolvedPath);
        if (IsOutsideRoot(relativeToRoot))
        {
            resolvedPath = string.Empty;
            return false;
        }

        return true;
    }

    public static string ResolvePathOrThrow(string rootPath, string relativePath)
    {
        if (!TryResolvePath(rootPath, relativePath, out var resolvedPath))
        {
            throw new ArgumentException($"Path traversal detected: {relativePath}");
        }

        return resolvedPath;
    }

    private static bool IsOutsideRoot(string relativeToRoot)
    {
        if (string.IsNullOrEmpty(relativeToRoot))
        {
            return false;
        }

        if (Path.IsPathRooted(relativeToRoot))
        {
            return true;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(relativeToRoot, "..", comparison))
        {
            return true;
        }

        if (relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, comparison))
        {
            return true;
        }

        if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar
            && relativeToRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison))
        {
            return true;
        }

        return false;
    }
}
