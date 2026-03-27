using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// File-system backed artifact service.
/// Generates in-world documents (newspapers, letters, etc.) displayed in a UI side panel.
/// </summary>
public sealed partial class FileSystemArtifactService : IArtifactService
{
    private static readonly FrozenDictionary<ArtifactFormat, string> FormatInstructions =
        new Dictionary<ArtifactFormat, string>
        {
            [ArtifactFormat.Newspaper] =
                "Write in journalistic style. Include a headline, byline, and follow the inverted pyramid structure "
                + "(most important information first, then supporting details, then background). "
                + "Use a formal, objective tone appropriate for a newspaper article.",

            [ArtifactFormat.Letter] =
                "Write as a letter with proper salutation, body paragraphs, and closing. "
                + "Match the formality level to the relationship between sender and recipient "
                + "(formal for officials, informal for close friends/family). Include date and signature.",

            [ArtifactFormat.Texts] =
                "Write as a series of chat/text messages between characters. Include character names before each message. "
                + "Keep messages short and conversational. Use timestamps to show passage of time. "
                + "Reflect each character's voice and texting style.",

            [ArtifactFormat.Social] =
                "Write as social media posts. Include username/handle, post content, and engagement metrics "
                + "(likes, shares, comments). Match the tone and format of the platform "
                + "(short and punchy for micro-posts, longer for blog-style). Include hashtags if appropriate.",

            [ArtifactFormat.Journal] =
                "Write as a personal journal or diary entry. Use first person perspective. "
                + "Include the date at the top. Capture private thoughts, emotions, and reflections. "
                + "The tone should feel intimate and unguarded, as if not meant for others to read.",

            [ArtifactFormat.Report] =
                "Write as an official report or formal document. Use headers and structured sections. "
                + "Include classification level if appropriate (e.g., CONFIDENTIAL, FOR INTERNAL USE ONLY). "
                + "Maintain a formal, objective, bureaucratic tone. Use bullet points and numbered lists where appropriate.",

            [ArtifactFormat.Wanted] =
                "Write as a wanted poster or bounty notice. Include a physical description of the subject, "
                + "list of crimes or charges, reward amount, and any warnings (e.g., ARMED AND DANGEROUS). "
                + "Use bold, attention-grabbing language appropriate to the setting.",

            [ArtifactFormat.Prose] =
                "Write as standalone prose. Focus on quality narrative writing without meta-commentary. "
                + "Do not include any framing like 'Here is the document' or explanations outside the artifact itself. "
                + "The output should read as a self-contained piece of writing.",
        }.ToFrozenDictionary();

    private static readonly Regex FormatHeaderRegex = FormatHeaderPattern();

    private readonly string _artifactsPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemArtifactService> _logger;
    private readonly object _lock = new();
    private Artifact? _currentArtifact;

    public FileSystemArtifactService(
        string artifactsPath,
        AtomicFileWriter writer,
        ILogger<FileSystemArtifactService> logger)
    {
        _artifactsPath = artifactsPath;
        _writer = writer;
        _logger = logger;
    }

    public Artifact? GetCurrent()
    {
        lock (_lock)
        {
            return _currentArtifact;
        }
    }

    public void SetCurrent(Artifact artifact)
    {
        lock (_lock)
        {
            _currentArtifact = artifact;
        }

        // Fire-and-forget save; errors are logged but don't block the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                await SaveAsync(artifact);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist artifact to disk");
            }
        });
    }

    public void ClearCurrent()
    {
        lock (_lock)
        {
            _currentArtifact = null;
        }

        _logger.LogDebug("Current artifact cleared");
    }

    public Task<IReadOnlyList<ArtifactSummary>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_artifactsPath))
        {
            return Task.FromResult<IReadOnlyList<ArtifactSummary>>([]);
        }

        var files = Directory.GetFiles(_artifactsPath, "*.md")
            .OrderByDescending(f => f)
            .ToList();

        var summaries = new List<ArtifactSummary>(files.Count);

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content = File.ReadAllText(filePath);
                var format = ExtractFormat(content);
                var fi = new FileInfo(filePath);

                summaries.Add(new ArtifactSummary
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    Format = format,
                    Path = System.IO.Path.GetFileName(filePath),
                    Size = (int)fi.Length,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read artifact file: {Path}", filePath);
            }
        }

        return Task.FromResult<IReadOnlyList<ArtifactSummary>>(summaries);
    }

    public async Task SaveAsync(Artifact artifact, CancellationToken ct = default)
    {
        var timestamp = artifact.CreatedAt.ToString("yyyyMMdd-HHmmss");
        var formatName = artifact.Format.ToString().ToLowerInvariant();
        var fileName = artifact.FileName ?? $"{timestamp}-{formatName}.md";
        var filePath = Path.Combine(_artifactsPath, fileName);

        var markdown = $"<!-- format:{formatName} -->\n{artifact.Content}";

        await _writer.WriteAsync(filePath, markdown, ct);
        _logger.LogInformation("Saved artifact {FileName} ({Format})", fileName, formatName);
    }

    public string BuildPrompt(string userPrompt, ArtifactFormat format)
    {
        var instructions = FormatInstructions.TryGetValue(format, out var fmt)
            ? fmt
            : FormatInstructions[ArtifactFormat.Prose];

        return $"""
            Create an in-world artifact document based on the following request.

            FORMAT: {format.ToString().ToLowerInvariant()}
            FORMAT INSTRUCTIONS: {instructions}

            USER REQUEST: {userPrompt}

            Write ONLY the artifact content. Do not include any meta-commentary, explanations, or framing outside the document itself. Use the write_prose tool to return the artifact content.
            """;
    }

    public IReadOnlyDictionary<ArtifactFormat, string> GetFormatInstructions() => FormatInstructions;

    private static string ExtractFormat(string content)
    {
        var match = FormatHeaderRegex.Match(content);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    [GeneratedRegex(@"<!--\s*format:(\w+)\s*-->", RegexOptions.Compiled)]
    private static partial Regex FormatHeaderPattern();
}
