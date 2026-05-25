using System.Text.RegularExpressions;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Deterministic (no LLM) classifier that evaluates how a lore passage applies
/// to an active subject. Uses structural clues: subject name mentions, pronouns,
/// subject-marker patterns, and source file path heuristics.
///
/// Mapped to Den protocol enum values:
///   active-character evidence -> Applies + AssertAsFact + CharacterSpecific
///   shared world/generic equipment -> Unknown + BackgroundOnly + SharedWorld/GenericEquipment
///   off-character evidence -> DoesNotApply + OffSubjectEvidence/RejectForActiveSubject
///   ambiguous -> Ambiguous + RequiresClarification
///
/// This is a first-pass structural classifier. May return Unknown or Ambiguous
/// for cases that require semantic analysis — those should be handled by the
/// Librarian's higher-level synthesis.
/// </summary>
public static partial class RoleplayApplicabilityClassifier
{
    // Thresholds for classification
    private const int ActiveCharacterMentionThreshold = 2;  // mentions of active subject name
    private const int OffCharacterMentionThreshold = 1;     // mentions of off-character name

    /// <summary>
    /// Classify how a lore passage applies to the given active subject.
    /// Returns Den-spec protocol values: Applies, DoesNotApply, Unknown, or Ambiguous.
    /// </summary>
    /// <param name="passage">The lore passage text to classify.</param>
    /// <param name="activeSubject">The active character/subject name.</param>
    /// <param name="sourcePath">Optional source file path (for file-name heuristics).</param>
    /// <param name="offCharacterNames">Optional set of off-character names to check against.</param>
    /// <returns>Applicability classification using Den protocol values.</returns>
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
                return ActiveSubjectApplicability.Applies;
            }

            // Check for off-character files
            if (offCharacterNames is not null)
            {
                foreach (var offName in offCharacterNames)
                {
                    if (Path.GetFileNameWithoutExtension(sourcePath)
                            .Contains(offName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ActiveSubjectApplicability.DoesNotApply;
                    }
                }
            }

            // If an active subject is known and a character-file path points at a
            // different subject, classify it as off-subject even when the caller
            // does not have a complete roster of other character names. This is
            // the common query_lore path: the Librarian knows source files but not
            // every possible character in the corpus.
            if (activeSubject is not null && IsCharacterSourcePath(sourcePath))
            {
                return ActiveSubjectApplicability.DoesNotApply;
            }

            // World/shared files -> Unknown (generic, not directly applicable or inapplicable)
            if (pathLower.Contains("world") ||
                pathLower.Contains("body-tech") ||
                pathLower.Contains("shared") ||
                pathLower.Contains("faction") ||
                pathLower.Contains("setting"))
            {
                return ActiveSubjectApplicability.Unknown;
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
                return ActiveSubjectApplicability.Applies;
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
                    return ActiveSubjectApplicability.DoesNotApply;
                }
            }
        }

        // 4. If the passage mentions the active subject at all, default to Applies
        //    (checked before shared-world markers because character lore often contains
        //     words like "standard" or "common" that would otherwise match as shared_world)
        if (activeSubject is not null &&
            passageLower.Contains(activeSubject.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return ActiveSubjectApplicability.Applies;
        }

        // 5. Check for shared-world markers -> Unknown (generic world knowledge)
        if (HasSharedWorldMarker(passageLower))
        {
            return ActiveSubjectApplicability.Unknown;
        }

        return ActiveSubjectApplicability.Ambiguous;
    }

    /// <summary>
    /// Classify the allowed use for a passage given its applicability.
    /// Returns Den-spec protocol values: AssertAsFact, BackgroundOnly,
    /// OffSubjectEvidence, RequiresClarification, or RejectForActiveSubject.
    /// </summary>
    public static AllowedUse ClassifyAllowedUse(
        ActiveSubjectApplicability applicability,
        string? activeSubject = null,
        string? passage = null,
        IReadOnlySet<string>? excludedSubjects = null)
    {
        return applicability switch
        {
            ActiveSubjectApplicability.Applies => AllowedUse.AssertAsFact,
            ActiveSubjectApplicability.Unknown => AllowedUse.BackgroundOnly,
            ActiveSubjectApplicability.DoesNotApply => IsExcluded(passage, activeSubject, excludedSubjects)
                ? AllowedUse.RejectForActiveSubject
                : AllowedUse.OffSubjectEvidence,
            ActiveSubjectApplicability.Ambiguous => AllowedUse.RequiresClarification,
            ActiveSubjectApplicability.Conflicts => AllowedUse.RejectForActiveSubject,
            _ => AllowedUse.RequiresClarification,
        };
    }

    /// <summary>
    /// Classify the knowledge scope for a passage.
    /// Uses sourcePath when available for more precise scope determination.
    /// </summary>
    public static RoleplayKnowledgeScope ClassifyScope(
        ActiveSubjectApplicability applicability,
        string? sourcePath = null)
    {
        // Try to use source path for precise scope when available
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var pathLower = sourcePath.ToLowerInvariant();

            if (pathLower.Contains("body-tech") ||
                (pathLower.Contains("world") && pathLower.Contains("tech")))
                return RoleplayKnowledgeScope.GenericEquipment;

            if (pathLower.Contains("faction"))
                return RoleplayKnowledgeScope.Organization;

            if (pathLower.Contains("location") || pathLower.Contains("setting"))
                return RoleplayKnowledgeScope.Location;

            if (pathLower.Contains("rule") || pathLower.Contains("scene-rule"))
                return RoleplayKnowledgeScope.SceneRule;

            if (pathLower.Contains("session-canon") || pathLower.Contains("sticky"))
                return RoleplayKnowledgeScope.SessionCanon;

            if (pathLower.Contains("world"))
                return RoleplayKnowledgeScope.SharedWorld;
        }

        return applicability switch
        {
            ActiveSubjectApplicability.Applies => RoleplayKnowledgeScope.CharacterSpecific,
            ActiveSubjectApplicability.DoesNotApply => RoleplayKnowledgeScope.CharacterSpecific,
            ActiveSubjectApplicability.Unknown => RoleplayKnowledgeScope.SharedWorld,
            ActiveSubjectApplicability.Ambiguous => RoleplayKnowledgeScope.Unknown,
            ActiveSubjectApplicability.Conflicts => RoleplayKnowledgeScope.Unknown,
            _ => RoleplayKnowledgeScope.Unknown,
        };
    }

    /// <summary>
    /// Build a RoleplayEvidenceItem from raw passage content with automatic classification.
    /// Uses Den-spec protocol values for all classification fields.
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
        var scope = ClassifyScope(applicability, sourcePath);

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
        if (applicability == ActiveSubjectApplicability.DoesNotApply && offCharacterNames is not null)
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

        if (applicability == ActiveSubjectApplicability.DoesNotApply &&
            subjectRef is null &&
            !string.IsNullOrWhiteSpace(sourcePath) &&
            IsCharacterSourcePath(sourcePath))
        {
            var inferred = Path.GetFileNameWithoutExtension(sourcePath)
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Trim();
            if (!string.IsNullOrWhiteSpace(inferred))
            {
                subjectRef = new RoleplaySubjectRef
                {
                    Name = inferred,
                    Confidence = "low",
                };
            }
        }

        // Build ambiguity note for Ambiguous/Unknown cases
        RoleplayAmbiguity? ambiguity = null;
        if (applicability == ActiveSubjectApplicability.Ambiguous)
        {
            ambiguity = new RoleplayAmbiguity
            {
                Description = "Could not determine whether this passage applies to the active subject.",
                AskedForClarification = false,
            };
        }

        return new RoleplayEvidenceItem
        {
            Passage = passage,
            Applicability = applicability,
            AllowedUse = allowedUse,
            SourceRefs = sourceRefs,
            SubjectRef = subjectRef,
            Ambiguity = ambiguity,
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
        // it should be rejected for active subject use
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
            return CanonAuthority.UserCorrection;

        return applicability switch
        {
            ActiveSubjectApplicability.Applies => CanonAuthority.Canon,
            ActiveSubjectApplicability.DoesNotApply => CanonAuthority.Canon,
            ActiveSubjectApplicability.Unknown => CanonAuthority.Canon,
            _ => CanonAuthority.Unknown,
        };
    }

    private static bool IsCharacterSourcePath(string sourcePath)
    {
        var pathLower = sourcePath.ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return fileName.Contains("character", StringComparison.OrdinalIgnoreCase) ||
            pathLower.Contains("/characters/", StringComparison.Ordinal) ||
            pathLower.Contains("\\characters\\", StringComparison.Ordinal) ||
            pathLower.StartsWith("characters/", StringComparison.Ordinal) ||
            pathLower.StartsWith("characters\\", StringComparison.Ordinal);
    }

    private static SubjectSourceKind MapSourceKindFromPath(string sourcePath)
    {
        var pathLower = sourcePath.ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);

        if (IsCharacterSourcePath(sourcePath))
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
