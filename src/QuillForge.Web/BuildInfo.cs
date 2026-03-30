using System.Reflection;

namespace QuillForge.Web;

/// <summary>
/// Build-time metadata embedded via MSBuild properties.
/// Provides version, build timestamp, and git commit hash.
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// Semantic version. Bump manually in QuillForge.Web.csproj.
    /// </summary>
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>
    /// Informational version includes git hash if available.
    /// </summary>
    public static string InformationalVersion { get; } =
        typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? Version;

    /// <summary>
    /// UTC timestamp when this binary was built.
    /// </summary>
    public static DateTimeOffset BuildDate { get; } = GetBuildDate();

    /// <summary>
    /// How long ago this binary was built, for staleness detection.
    /// </summary>
    public static TimeSpan Age => DateTimeOffset.UtcNow - BuildDate;

    /// <summary>
    /// UTC timestamp when this process started.
    /// </summary>
    public static DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// How long this process has been running.
    /// </summary>
    public static TimeSpan Uptime => DateTimeOffset.UtcNow - StartTime;

    private static DateTimeOffset GetBuildDate()
    {
        // Try to get from the assembly's metadata
        var attr = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyMetadataAttribute>();

        // Fall back to the assembly file's last write time
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, typeof(BuildInfo).Assembly.GetName().Name + ".dll");
        if (File.Exists(assemblyPath))
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(assemblyPath), TimeSpan.Zero);
        }

        return DateTimeOffset.MinValue;
    }
}
