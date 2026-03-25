using Microsoft.Extensions.Logging;

namespace QuillForge.Storage.Utilities;

/// <summary>
/// Writes files atomically using write-to-temp-then-rename.
/// Ensures a crash mid-write never corrupts the target file.
/// </summary>
public sealed class AtomicFileWriter
{
    private readonly ILogger<AtomicFileWriter> _logger;

    public AtomicFileWriter(ILogger<AtomicFileWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Writes content to the target path atomically.
    /// </summary>
    public async Task WriteAsync(string targetPath, string content, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await File.WriteAllTextAsync(tempPath, content, ct);
            File.Move(tempPath, targetPath, overwrite: true);
            _logger.LogDebug("Atomic write to {Path} ({Length} chars)", targetPath, content.Length);
        }
        catch
        {
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Writes bytes to the target path atomically.
    /// </summary>
    public async Task WriteBytesAsync(string targetPath, byte[] data, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await File.WriteAllBytesAsync(tempPath, data, ct);
            File.Move(tempPath, targetPath, overwrite: true);
            _logger.LogDebug("Atomic write (bytes) to {Path} ({Length} bytes)", targetPath, data.Length);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Cleans up any orphaned temp files in a directory (from interrupted writes).
    /// </summary>
    public void CleanupTempFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var tempFile in Directory.GetFiles(directory, "*.tmp.*"))
        {
            try
            {
                File.Delete(tempFile);
                _logger.LogInformation("Cleaned up orphaned temp file: {Path}", tempFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp file: {Path}", tempFile);
            }
        }
    }
}
