using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionLoreCanonizationService : ISessionLoreCanonizationService
{
    private const string GenerateOperationName = "generate_lore_canonization";
    private const string ApplyOperationName = "apply_lore_canonization";
    private const string DiscardOperationName = "discard_lore_canonization";
    private const string ManagedBlockStart = "<!-- quillforge:canonize";
    private const string ManagedBlockEnd = "<!-- /quillforge:canonize -->";

    private readonly ISessionStore _sessionStore;
    private readonly ISessionStateStore _stateStore;
    private readonly ISessionStateService _sessionStateService;
    private readonly ISessionMutationGate _gate;
    private readonly ILoreStore _loreStore;
    private readonly IContentFileService _contentFileService;
    private readonly ICompletionService _completionService;
    private readonly ILogger<SessionLoreCanonizationService> _logger;
    private readonly string _model;
    private readonly CanonizerBudget _budget;

    public SessionLoreCanonizationService(
        ISessionStore sessionStore,
        ISessionStateStore stateStore,
        ISessionStateService sessionStateService,
        ISessionMutationGate gate,
        ILoreStore loreStore,
        IContentFileService contentFileService,
        ICompletionService completionService,
        AppConfig appConfig,
        ILogger<SessionLoreCanonizationService> logger)
    {
        _sessionStore = sessionStore;
        _stateStore = stateStore;
        _sessionStateService = sessionStateService;
        _gate = gate;
        _loreStore = loreStore;
        _contentFileService = contentFileService;
        _completionService = completionService;
        _logger = logger;
        _model = appConfig.Models.Canonizer;
        _budget = appConfig.Agents.Canonizer;
    }

    public async Task<SessionMutationResult<LoreCanonizationProposalGeneratedEvent>> GenerateProposalAsync(
        Guid? sessionId,
        GenerateLoreCanonizationProposalCommand command,
        CancellationToken ct = default)
    {
        if (!sessionId.HasValue || sessionId == Guid.Empty)
        {
            return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Invalid("sessionId is required.");
        }

        await using var lease = await _gate.TryAcquireAsync(sessionId, GenerateOperationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        try
        {
            var rawState = await _stateStore.LoadAsync(sessionId, ct);
            var hydratedState = await _sessionStateService.LoadViewAsync(sessionId, ct);
            var resolvedSessionId = hydratedState.SessionId ?? sessionId.Value;
            var loreSet = RequireLoreSet(hydratedState);
            var tree = await _sessionStore.LoadAsync(resolvedSessionId, ct);
            var thread = tree.ToFlatThread();
            if (thread.Count == 0)
            {
                return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Invalid(
                    "This session has no conversation history to canonize yet.");
            }

            var targetFilePath = NormalizeTargetFilePath(command.TargetFilePath, resolvedSessionId);
            var loreFiles = await _loreStore.LoadLoreSetAsync(loreSet, ct);
            loreFiles.TryGetValue(targetFilePath, out var existingTargetContent);

            var request = BuildCompletionRequest(resolvedSessionId, loreSet, targetFilePath, existingTargetContent, loreFiles, thread);
            var response = await _completionService.CompleteAsync(request, ct);
            var proposal = BuildProposal(
                resolvedSessionId,
                loreSet,
                targetFilePath,
                existingTargetContent,
                response.Content.GetText(),
                DateTimeOffset.UtcNow);

            rawState.Canonization ??= new LoreCanonizationRuntimeState();
            rawState.Canonization.PendingProposal = proposal;
            await _stateStore.SaveAsync(rawState, ct);

            _logger.LogInformation(
                "Generated lore canonization proposal: session={SessionId} loreSet={LoreSet} targetFile={TargetFile} newFacts={NewFacts} modifiedFacts={ModifiedFacts} conflicts={Conflicts} canApply={CanApply}",
                resolvedSessionId,
                loreSet,
                targetFilePath,
                proposal.NewFacts.Count,
                proposal.ModifiedFacts.Count,
                proposal.Conflicts.Count,
                proposal.CanApply);

            return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Success(new LoreCanonizationProposalGeneratedEvent
            {
                SessionId = resolvedSessionId,
                Proposal = proposal,
            });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Lore canonization preview failed because the session transcript was unavailable: session={SessionId}", sessionId);
            return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Invalid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Lore canonization preview rejected: session={SessionId}", sessionId);
            return SessionMutationResult<LoreCanonizationProposalGeneratedEvent>.Invalid(ex.Message);
        }
    }

    public async Task<SessionMutationResult<LoreCanonizationAppliedEvent>> ApplyProposalAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        if (!sessionId.HasValue || sessionId == Guid.Empty)
        {
            return SessionMutationResult<LoreCanonizationAppliedEvent>.Invalid("sessionId is required.");
        }

        await using var lease = await _gate.TryAcquireAsync(sessionId, ApplyOperationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<LoreCanonizationAppliedEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var rawState = await _stateStore.LoadAsync(sessionId, ct);
        var canonizationState = rawState.Canonization;
        var proposal = canonizationState?.PendingProposal;
        if (proposal is null)
        {
            return SessionMutationResult<LoreCanonizationAppliedEvent>.Invalid(
                "There is no pending lore canonization proposal for this session.");
        }

        if (!proposal.CanApply)
        {
            return SessionMutationResult<LoreCanonizationAppliedEvent>.Invalid(
                "The pending lore canonization proposal has no non-conflicting facts to apply.");
        }

        var hydratedState = await _sessionStateService.LoadViewAsync(sessionId, ct);
        var activeLoreSet = RequireLoreSet(hydratedState);
        if (!string.Equals(activeLoreSet, proposal.LoreSet, StringComparison.OrdinalIgnoreCase))
        {
            return SessionMutationResult<LoreCanonizationAppliedEvent>.Invalid(
                "The active lore set changed after the proposal was generated. Regenerate the proposal before applying it.");
        }

        var relativePath = BuildLoreRelativePath(proposal.LoreSet, proposal.TargetFilePath);
        await _contentFileService.WriteAsync(relativePath, proposal.ProposedFileContent, ct);
        canonizationState!.PendingProposal = null;
        await _stateStore.SaveAsync(rawState, ct);

        _logger.LogInformation(
            "Applied lore canonization proposal: session={SessionId} loreSet={LoreSet} targetFile={TargetFile} length={Length}",
            sessionId,
            proposal.LoreSet,
            proposal.TargetFilePath,
            proposal.ProposedFileContent.Length);

        return SessionMutationResult<LoreCanonizationAppliedEvent>.Success(new LoreCanonizationAppliedEvent
        {
            SessionId = sessionId.Value,
            LoreSet = proposal.LoreSet,
            TargetFilePath = proposal.TargetFilePath,
            SavedContent = proposal.ProposedFileContent,
        });
    }

    public async Task<SessionMutationResult<LoreCanonizationDiscardedEvent>> DiscardProposalAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        if (!sessionId.HasValue || sessionId == Guid.Empty)
        {
            return SessionMutationResult<LoreCanonizationDiscardedEvent>.Invalid("sessionId is required.");
        }

        await using var lease = await _gate.TryAcquireAsync(sessionId, DiscardOperationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<LoreCanonizationDiscardedEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var rawState = await _stateStore.LoadAsync(sessionId, ct);
        var discardedTarget = rawState.Canonization?.PendingProposal?.TargetFilePath;
        if (rawState.Canonization is not null)
        {
            rawState.Canonization.PendingProposal = null;
            await _stateStore.SaveAsync(rawState, ct);
        }

        _logger.LogInformation(
            "Discarded lore canonization proposal: session={SessionId} targetFile={TargetFile}",
            sessionId,
            discardedTarget);

        return SessionMutationResult<LoreCanonizationDiscardedEvent>.Success(new LoreCanonizationDiscardedEvent
        {
            SessionId = sessionId.Value,
            TargetFilePath = discardedTarget,
        });
    }

    private CompletionRequest BuildCompletionRequest(
        Guid sessionId,
        string loreSet,
        string targetFilePath,
        string? existingTargetContent,
        IReadOnlyDictionary<string, string> loreFiles,
        IReadOnlyList<MessageNode> thread)
    {
        var prompt = BuildSystemPrompt(sessionId, loreSet, targetFilePath, existingTargetContent, loreFiles);
        var transcript = BuildTranscript(thread);

        return new CompletionRequest
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            SystemPrompt = prompt,
            Messages = [new CompletionMessage("user", new MessageContent(transcript))],
        };
    }

    private string BuildSystemPrompt(
        Guid sessionId,
        string loreSet,
        string targetFilePath,
        string? existingTargetContent,
        IReadOnlyDictionary<string, string> loreFiles)
    {
        var loreContext = BuildLoreContext(targetFilePath, loreFiles);
        var targetSection = string.IsNullOrWhiteSpace(existingTargetContent)
            ? "(This file does not exist yet.)"
            : existingTargetContent;

        return $$"""
You are QuillForge's lore canonization analyst.

Your job:
1. Read the current session transcript.
2. Extract only durable, explicit facts that were actually established in the session.
3. Compare those facts against the active lore set.
4. Classify them into:
   - new_facts: facts not yet present in lore and safe to add.
   - modified_facts: facts that refine or extend the target file without conflicting with lore.
   - conflicts: possible contradictions, ambiguities, or claims that should not be auto-promoted.
5. Draft markdown for the target lore file that includes only the non-conflicting facts.

Rules:
- Do not invent facts.
- Ignore user questions, hypotheticals, jokes, and uncertain speculation.
- Treat a fact as established only if the transcript clearly commits to it.
- Prefer caution: uncertain items belong in conflicts.
- Keep the proposed markdown concise and ready to paste into a lore file.
- If no safe facts can be promoted, return an empty proposed_markdown string.
- Return JSON only.

Session: {{sessionId}}
Active lore set: {{loreSet}}
Target file: {{targetFilePath}}

## Existing Target File

{{targetSection}}

## Other Lore Context

{{loreContext}}

Respond with exactly this JSON shape:
{
  "summary": "short plain-language summary",
  "new_facts": ["fact 1", "fact 2"],
  "modified_facts": ["fact 1", "fact 2"],
  "conflicts": ["conflict 1", "conflict 2"],
  "proposed_markdown": "markdown containing only non-conflicting facts"
}
""";
    }

    private LoreCanonizationProposalState BuildProposal(
        Guid sessionId,
        string loreSet,
        string targetFilePath,
        string? existingTargetContent,
        string responseText,
        DateTimeOffset generatedAt)
    {
        var parsed = ParseResponse(responseText);
        var summary = string.IsNullOrWhiteSpace(parsed.Summary)
            ? "Reviewed the current session for durable lore facts."
            : parsed.Summary.Trim();
        var newFacts = CleanList(parsed.NewFacts);
        var modifiedFacts = CleanList(parsed.ModifiedFacts);
        var conflicts = CleanList(parsed.Conflicts);
        var proposedMarkdown = NormalizeMarkdown(parsed.ProposedMarkdown);
        var canApply = newFacts.Count > 0 || modifiedFacts.Count > 0;
        var fileContent = BuildProposedFileContent(existingTargetContent, proposedMarkdown, sessionId, generatedAt, canApply);

        return new LoreCanonizationProposalState
        {
            SessionId = sessionId,
            LoreSet = loreSet,
            TargetFilePath = targetFilePath,
            Summary = summary,
            NewFacts = newFacts,
            ModifiedFacts = modifiedFacts,
            Conflicts = conflicts,
            ProposedMarkdown = proposedMarkdown,
            ProposedFileContent = fileContent,
            CanApply = canApply,
            GeneratedAt = generatedAt,
        };
    }

    private CanonizerResponseDto ParseResponse(string responseText)
    {
        var text = responseText.Trim();
        if (TryParseResponse(text, out var parsed))
        {
            return parsed;
        }

        var stripped = LibrarianAgent.StripMarkdownFences(text);
        if (stripped != text && TryParseResponse(stripped, out parsed))
        {
            return parsed;
        }

        var extracted = LibrarianAgent.ExtractJsonObject(text);
        if (extracted is not null && TryParseResponse(extracted, out parsed))
        {
            return parsed;
        }

        _logger.LogWarning("Canonizer response was not valid structured JSON; returning a conflict-only proposal.");
        return new CanonizerResponseDto
        {
            Summary = "The canonization analysis could not be parsed cleanly.",
            Conflicts = [text],
            ProposedMarkdown = string.Empty,
        };
    }

    private static bool TryParseResponse(string json, out CanonizerResponseDto dto)
    {
        dto = default!;
        if (!StructuredJsonParser.TryParse<CanonizerResponseDto>(json, out var parsed) || parsed is null)
        {
            return false;
        }

        dto = parsed;
        return true;
    }

    private string BuildLoreContext(string targetFilePath, IReadOnlyDictionary<string, string> loreFiles)
    {
        var builder = new StringBuilder();
        var remaining = _budget.MaxLoreContextChars;

        foreach (var entry in loreFiles
            .Where(kvp => !string.Equals(NormalizeRelativePath(kvp.Key), targetFilePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining <= 0)
            {
                break;
            }

            var normalizedContent = entry.Value.ReplaceLineEndings("\n").Trim();
            if (normalizedContent.Length == 0)
            {
                continue;
            }

            var sectionHeader = $"### File: {entry.Key}\n\n";
            var availableChars = Math.Max(remaining - sectionHeader.Length - 8, 0);
            if (availableChars <= 0)
            {
                break;
            }

            var snippet = normalizedContent.Length > availableChars
                ? normalizedContent[..availableChars] + "\n\n[truncated]"
                : normalizedContent;
            var section = sectionHeader + snippet + "\n\n---\n\n";
            builder.Append(section);
            remaining -= section.Length;
        }

        return builder.Length == 0 ? "(No additional lore files were available.)" : builder.ToString().Trim();
    }

    private static string BuildTranscript(IReadOnlyList<MessageNode> thread)
    {
        var builder = new StringBuilder();

        foreach (var node in thread)
        {
            var role = node.Role.ToUpperInvariant();
            var content = node.Content.GetText().ReplaceLineEndings("\n").Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            builder.Append(role);
            builder.AppendLine(":");
            builder.AppendLine(content);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeTargetFilePath(string? requestedPath, Guid sessionId)
    {
        var trimmed = string.IsNullOrWhiteSpace(requestedPath)
            ? $"session-imports/{DateTimeOffset.UtcNow:yyyy-MM-dd}-{sessionId.ToString("N")[..8]}.md"
            : requestedPath.Trim();

        var normalized = NormalizeRelativePath(trimmed);
        if (Path.IsPathRooted(normalized))
        {
            throw new ArgumentException("Target lore file path must be relative.");
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Target lore file path cannot traverse directories.");
        }

        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrEmpty(extension))
        {
            normalized += ".md";
        }
        else if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Target lore file must use the .md extension.");
        }

        return normalized;
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    private static string RequireLoreSet(SessionState state)
    {
        var loreSet = state.Profile.ActiveLoreSet;
        if (string.IsNullOrWhiteSpace(loreSet))
        {
            throw new InvalidOperationException("The active lore set could not be resolved for this session.");
        }

        return loreSet.Trim();
    }

    private static List<string> CleanList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeMarkdown(string? markdown)
    {
        return string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : markdown.ReplaceLineEndings("\n").Trim();
    }

    private static string BuildProposedFileContent(
        string? existingTargetContent,
        string proposedMarkdown,
        Guid sessionId,
        DateTimeOffset generatedAt,
        bool canApply)
    {
        var normalizedExisting = string.IsNullOrWhiteSpace(existingTargetContent)
            ? string.Empty
            : existingTargetContent.ReplaceLineEndings("\n").TrimEnd();
        if (!canApply || string.IsNullOrWhiteSpace(proposedMarkdown))
        {
            return normalizedExisting;
        }

        var managedBlock = BuildManagedBlock(proposedMarkdown, sessionId, generatedAt);
        if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            return managedBlock;
        }

        var startIndex = normalizedExisting.IndexOf(ManagedBlockStart, StringComparison.Ordinal);
        if (startIndex >= 0)
        {
            var endIndex = normalizedExisting.IndexOf(ManagedBlockEnd, startIndex, StringComparison.Ordinal);
            if (endIndex >= 0)
            {
                var replaced = normalizedExisting[..startIndex]
                    + managedBlock
                    + normalizedExisting[(endIndex + ManagedBlockEnd.Length)..];
                return replaced.Trim();
            }
        }

        return normalizedExisting + "\n\n" + managedBlock;
    }

    private static string BuildManagedBlock(string proposedMarkdown, Guid sessionId, DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"<!-- quillforge:canonize session={sessionId} generated={generatedAt:O} -->");
        builder.AppendLine($"## Session Canon Import ({generatedAt:yyyy-MM-dd})");
        builder.AppendLine();
        builder.AppendLine($"Source session: `{sessionId}`");
        builder.AppendLine();
        builder.AppendLine(proposedMarkdown.Trim());
        builder.AppendLine();
        builder.Append(ManagedBlockEnd);
        return builder.ToString().Trim();
    }

    private static string BuildLoreRelativePath(string loreSet, string targetFilePath)
    {
        return $"{ContentPaths.Lore}/{loreSet}/{targetFilePath}";
    }

    private sealed class CanonizerResponseDto
    {
        public string? Summary { get; init; }

        [JsonPropertyName("new_facts")]
        public IReadOnlyList<string>? NewFacts { get; init; }

        [JsonPropertyName("modified_facts")]
        public IReadOnlyList<string>? ModifiedFacts { get; init; }

        public IReadOnlyList<string>? Conflicts { get; init; }

        [JsonPropertyName("proposed_markdown")]
        public string? ProposedMarkdown { get; init; }
    }
}
