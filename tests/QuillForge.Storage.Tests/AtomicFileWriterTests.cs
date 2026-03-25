using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AtomicFileWriter _writer;

    public AtomicFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _writer = new AtomicFileWriter(NullLoggerFactory.Instance.CreateLogger<AtomicFileWriter>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task WritesFileSuccessfully()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        await _writer.WriteAsync(path, "hello world");

        Assert.True(File.Exists(path));
        Assert.Equal("hello world", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CreatesIntermediateDirectories()
    {
        var path = Path.Combine(_tempDir, "sub", "dir", "test.txt");
        await _writer.WriteAsync(path, "nested");

        Assert.True(File.Exists(path));
        Assert.Equal("nested", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        await _writer.WriteAsync(path, "original");
        await _writer.WriteAsync(path, "updated");

        Assert.Equal("updated", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task NoTempFileLeftAfterSuccess()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        await _writer.WriteAsync(path, "content");

        var tempFiles = Directory.GetFiles(_tempDir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public void CleanupTempFiles_RemovesOrphans()
    {
        // Create fake orphaned temp files
        File.WriteAllText(Path.Combine(_tempDir, "session.json.tmp.abc12345"), "orphan");
        File.WriteAllText(Path.Combine(_tempDir, "other.json.tmp.def67890"), "orphan2");

        _writer.CleanupTempFiles(_tempDir);

        var remaining = Directory.GetFiles(_tempDir, "*.tmp.*");
        Assert.Empty(remaining);
    }
}
