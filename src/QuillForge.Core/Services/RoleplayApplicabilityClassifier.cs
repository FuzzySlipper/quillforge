using System.Text.RegularExpressions;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Deterministic (no LLM) classifier that evaluates how a lore passage applies
/// to an active subject. Uses structural clues: subject name mentions, pronouns,
/// subject-marker patterns, and source file path heuristics.
///
/// This is a first-pass structural classifier. It may return Unknown or
/// SharedWorld for ambiguous cases that require semantic analysis — those
/// should be handled by the Librarian's higher-level synthesis.
/// </summary>
public static partial class RoleplayApplicabilityClassifier
{
    // Thresholds for classification
    private const int ActiveCharacterMentionThreshold = 2;  // mentions of active subject name
    private const int OffCharacterMentionThreshold = 1;     // mentions of off-character name

    /// <summary>
    /// Classify how a lore passage applies to the given active subject.
    /// </summary>
    /// <param name="passage">The lore passage text to classify.</param>
    /// <param name="activeSubject">The active character/subject name.</param>
    /// <param name="sourcePath">Optional source file path (for file-name heuristics).</param>
    /// <param name="offCharacterNames">Optional set of off-character names to check against.</param>
    /// <returns>Applicability classification.</returns>
    public static ActiveSubjectApplicability Classify(
        string passage,
        string? activeSubject,
        string? sourcePath = null,
        IReadOnlySet<string>? offCharacterNames = null)
    {
        if (string.IsNullOrWhiteSpace(passage))
            return ActiveSubjectApplicability.Unknown;

        // 1. Heuristic: source file path indicates character-specific lore
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var pathLower = sourcePath.ToLowerInvariant();

            // If the file name contains the active subject name, it's likely an active character file
            if (activeSubject is not null &&
                Path.GetFileNameWithoutExtension(sourcePath)
                    .Contains(activeSubject, StringComparison.OrdinalIgnoreCase))
            {
                return ActiveSubjectApplicability.ActiveCharacter;
            }

            // Check for off-character files
            if (offCharacterNames is not null)
            {
                foreach (var offName in offCharacterNames)
                {
                    if (Path.GetFileNameWithoutExtension(sourcePath)
                            .Contains(offName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ActiveSubjectApplicability.OffCharacter;
                    }
                }
            }

            // World/shared files
            if (pathLower.Contains("world") ||
                pathLower.Contains("body-tech") ||
                pathLower.Contains("shared") ||
                pathLower.Contains("faction") ||
                pathLower.Contains("setting"))
            {
                return ActiveSubjectApplicability.SharedWorld;
            }
        }

        // 2. Check passage content for subject name mentions
        var passageLower = passage.ToLowerInvariant();

        if (activeSubject is not null)
        {
            var activeLower = activeSubject.ToLowerInvariant();
            var activeNameMentions = CountNameMentions(passageLower, activeLower);

            if (activeNameMentions >= ActiveCharacterMentionThreshold)
            {
                return ActiveSubjectApplicability.ActiveCharacter;
            }
        }

        // 3. Check for off-character mentions
        if (offCharacterNames is not null)
        {
            foreach (var offName in offCharacterNames)
            {
                var offLower = offName.ToLowerInvariant();
                var offMentions = CountNameMentions(passageLower, offLower);

                if (offMentions >= OffCharacterMentionThreshold)
                {
                    return ActiveSubjectApplicability.OffCharacter;
                }
            }
        }

        // 4. If the passage mentions the active subject at all, default to active_character
        //    (checked before shared-world markers because character lore often contains
        //     words like "standard" or "common" that would otherwise match as shared_world)
        if (activeSubject is not null &&
            passageLower.Contains(activeSubject.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return ActiveSubjectApplicability.ActiveCharacter;
        }

        // 5. Check for shared-world markers
        if (HasSharedWorldMarker(passageLower))
        {
            return ActiveSubjectApplicability.SharedWorld;
        }

        return ActiveSubjectApplicability.Unknown;
    }

    /// <summary>
    /// Classify the allowed use for a passage given its applicability.
    /// </summary>
    public static AllowedUse ClassifyAllowedUse(
        ActiveSubjectApplicability applicability,
        string? activeSubject = null,
        string? passage = null,
        IReadOnlySet<string>? excludedSubjects = null)
    {
        return applicability switch
        {
            ActiveSubjectApplicability.ActiveCharacter => AllowedUse.Inline,
            ActiveSubjectApplicability.SharedWorld => AllowedUse.Context,
            ActiveSubjectApplicability.OffCharacter => IsExcluded(passage, activeSubject, excludedSubjects)
                ? AllowedUse.Excluded
                : AllowedUse.Context,
            ActiveSubjectApplicability.Unknown => AllowedUse.Unknown,
            _ => AllowedUse.Unknown,
        };
    }

    /// <summary>
    /// Classify the knowledge scope for a passage.
    /// </summary>
    public static RoleplayKnowledgeScope ClassifyScope(
        ActiveSubjectApplicability applicability)
    {
        return applicability switch
        {
            ActiveSubjectApplicability.ActiveCharacter => RoleplayKnowledgeScope.Character,
            ActiveSubjectApplicability.OffCharacter => RoleplayKnowledgeScope.Character,
            ActiveSubjectApplicability.SharedWorld => RoleplayKnowledgeScope.World,
            ActiveSubjectApplicability.Unknown => RoleplayKnowledgeScope.World,
            _ => RoleplayKnowledgeScope.World,
        };
    }

    /// <summary>
    /// Build a RoleplayEvidenceItem from raw passage content with automatic classification.
    /// </summary>
    public static RoleplayEvidenceItem ClassifyEvidenceItem(
        string passage,
        string? activeSubject,
        string? sourcePath = null,
        IReadOnlySet<string>? offCharacterNames = null,
        IReadOnlySet<string>? excludedSubjects = null)
    {
        var applicability = Classify(passage, activeSubject, sourcePath, offCharacterNames);
        var allowedUse = ClassifyAllowedUse(applicability, activeSubject, passage, excludedSubjects);
        var scope = ClassifyScope(applicability);

        var sourceRefs = string.IsNullOrWhiteSpace(sourcePath)
            ? null
            : new List<RoleplaySourceRef>
            {
                new()
                {
                    SourcePath = sourcePath,
                    Authority = MapAuthorityFromSourcePath(sourcePath, applicability),
                    SourceKind = MapSourceKindFromPath(sourcePath),
                },
            };

        RoleplaySubjectRef? subjectRef = null;
        if (applicability == ActiveSubjectApplicability.OffCharacter && offCharacterNames is not null)
        {
            var detectedOffName = offCharacterNames
                .FirstOrDefault(n =>
                    passage.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (detectedOffName is not null)
            {
                subjectRef = new RoleplaySubjectRef
                {
                    Name = detectedOffName,
                    Confidence = "medium",
                };
            }
        }

        return new RoleplayEvidenceItem
        {
            Passage = passage,
            Applicability = applicability,
            AllowedUse = allowedUse,
            SourceRefs = sourceRefs,
            SubjectRef = subjectRef,
        };
    }

    // ── Private helpers ──

    private static int CountNameMentions(string text, string nameLower)
    {
        if (string.IsNullOrWhiteSpace(nameLower))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(nameLower, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += nameLower.Length;
        }
        return count;
    }

    private static bool HasSharedWorldMarker(string passageLower)
    {
        return SharedWorldPattern().IsMatch(passageLower);
    }

    private static bool IsExcluded(
        string? passage,
        string? activeSubject,
        IReadOnlySet<string>? excludedSubjects)
    {
        if (excludedSubjects is null || excludedSubjects.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(passage))
            return false;

        // If passage is about an excluded subject and NOT about the active subject,
        // it should be excluded
        var passageLower = passage.ToLowerInvariant();
        var hasActiveSubject = activeSubject is not null &&
            passageLower.Contains(activeSubject.ToLowerInvariant(), StringComparison.Ordinal);

        foreach (var excluded in excludedSubjects)
        {
            if (passageLower.Contains(excluded.ToLowerInvariant(), StringComparison.Ordinal) &&
                !hasActiveSubject)
            {
                return true;
            }
        }

        return false;
    }

    private static CanonAuthority MapAuthorityFromSourcePath(
        string sourcePath, ActiveSubjectApplicability applicability)
    {
        var pathLower = sourcePath.ToLowerInvariant();

        if (pathLower.Contains("correction") || pathLower.Contains("override"))
            return CanonAuthority.Provisional;

        return applicability switch
        {
            ActiveSubjectApplicability.ActiveCharacter => CanonAuthority.Primary,
            ActiveSubjectApplicability.OffCharacter => CanonAuthority.Primary,
            ActiveSubjectApplicability.SharedWorld => CanonAuthority.Background,
            _ => CanonAuthority.Secondary,
        };
    }

    private static SubjectSourceKind MapSourceKindFromPath(string sourcePath)
    {
        var pathLower = sourcePath.ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);

        if (fileName.Contains("character", StringComparison.OrdinalIgnoreCase) ||
            pathLower.Contains("/characters/", StringComparison.Ordinal) ||
            pathLower.Contains("\\characters\\", StringComparison.Ordinal) ||
            pathLower.StartsWith("characters/", StringComparison.Ordinal) ||
            pathLower.StartsWith("characters\\", StringComparison.Ordinal))
            return SubjectSourceKind.CharacterFile;

        if (pathLower.Contains("world", StringComparison.Ordinal) ||
            pathLower.Contains("body-tech", StringComparison.Ordinal))
            return SubjectSourceKind.WorldFile;

        if (pathLower.Contains("faction", StringComparison.Ordinal))
            return SubjectSourceKind.FactionFile;

        if (pathLower.Contains("event", StringComparison.Ordinal))
            return SubjectSourceKind.EventFile;

        if (pathLower.Contains("location", StringComparison.Ordinal) ||
            pathLower.Contains("setting", StringComparison.Ordinal))
            return SubjectSourceKind.LocationFile;

        if (pathLower.Contains("item", StringComparison.Ordinal) ||
            pathLower.Contains("tech", StringComparison.Ordinal))
            return SubjectSourceKind.ItemFile;

        return SubjectSourceKind.Unknown;
    }

    [GeneratedRegex(
        @"\b(shared|common|standard|generic|typical|division(-wide)?|world(-wide)?|background|commonplace)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SharedWorldPattern();
}
