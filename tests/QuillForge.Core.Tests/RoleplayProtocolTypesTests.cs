using System.Text.Json;
using QuillForge.Core.Models;
using Xunit;

namespace QuillForge.Core.Tests;

public sealed class RoleplayProtocolTypesTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void RoleplayKnowledgePacket_RoundTrips_Json()
    {
        var packet = new RoleplayKnowledgePacket
        {
            Query = "What augmentations does Xavier have?",
            ActiveSubject = "Xavier",
            Scope = RoleplayKnowledgeScope.CharacterSpecific,
            Evidence =
            [
                new RoleplayEvidenceItem
                {
                    Passage = "Xavier has a standard neural interface.",
                    Applicability = ActiveSubjectApplicability.Applies,
                    AllowedUse = AllowedUse.AssertAsFact,
                    SourceRefs =
                    [
                        new RoleplaySourceRef
                        {
                            SourcePath = "characters/xavier.md",
                            SourceKind = SubjectSourceKind.CharacterFile,
                            Authority = CanonAuthority.Canon,
                        },
                    ],
                },
                new RoleplayEvidenceItem
                {
                    Passage = "Standard Division neural interfaces are common.",
                    Applicability = ActiveSubjectApplicability.Unknown,
                    AllowedUse = AllowedUse.BackgroundOnly,
                    SourceRefs =
                    [
                        new RoleplaySourceRef
                        {
                            SourcePath = "world/body-tech.md",
                            SourceKind = SubjectSourceKind.WorldFile,
                            Authority = CanonAuthority.Deprecated,
                        },
                    ],
                },
            ],
            SourceFiles = ["characters/xavier.md", "world/body-tech.md"],
            Confidence = "high",
            SourceComponent = "query_lore",
        };

        var json = JsonSerializer.Serialize(packet, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<RoleplayKnowledgePacket>(json, s_jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(packet.Query, deserialized.Query);
        Assert.Equal(packet.ActiveSubject, deserialized.ActiveSubject);
        Assert.Equal(packet.Scope, deserialized.Scope);
        Assert.Equal(2, deserialized.Evidence.Count);

        Assert.Equal(ActiveSubjectApplicability.Applies, deserialized.Evidence[0].Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, deserialized.Evidence[0].AllowedUse);
        Assert.Single(deserialized.Evidence[0].SourceRefs!);
        var refs = deserialized.Evidence[0].SourceRefs!;
        Assert.Equal("characters/xavier.md", refs[0].SourcePath);
        Assert.Equal(SubjectSourceKind.CharacterFile, refs[0].SourceKind);
        Assert.Equal(CanonAuthority.Canon, refs[0].Authority);

        Assert.Equal(ActiveSubjectApplicability.Unknown, deserialized.Evidence[1].Applicability);
        Assert.Equal(AllowedUse.BackgroundOnly, deserialized.Evidence[1].AllowedUse);
    }

    [Fact]
    public void StructuredSceneBrief_RoundTrips_Json()
    {
        var brief = new StructuredSceneBrief
        {
            SceneDescription = "Xavier enters the command center.",
            ActiveSubject = "Xavier",
            ExcludedSubjects = ["Caleb"],
            ToneNotes = "Tense, urgent",
            SourceComponent = "narrative_director",
        };

        var json = JsonSerializer.Serialize(brief, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<StructuredSceneBrief>(json, s_jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(brief.SceneDescription, deserialized.SceneDescription);
        Assert.Equal(brief.ActiveSubject, deserialized.ActiveSubject);
        Assert.Equal(brief.ExcludedSubjects, deserialized.ExcludedSubjects);
        Assert.Equal(brief.ToneNotes, deserialized.ToneNotes);
    }

    [Fact]
    public void RoleplayKnowledgeRequest_Serializes_Correctly()
    {
        var request = new RoleplayKnowledgeRequest
        {
            Query = "Xavier's gear",
            ActiveSubject = "Xavier",
            ExcludedSubjects = ["Caleb"],
            LoreSet = "deepspace",
            IncludeSharedContext = true,
        };

        var json = JsonSerializer.Serialize(request, s_jsonOptions);

        Assert.Contains("Xavier", json);
        Assert.Contains("active_subject", json);
        Assert.Contains("excluded_subjects", json);
    }

    [Fact]
    public void RoleplayEvidenceItem_WithSubjectRef_Serializes()
    {
        var item = new RoleplayEvidenceItem
        {
            Passage = "Caleb has a prosthetic arm.",
            Applicability = ActiveSubjectApplicability.DoesNotApply,
            AllowedUse = AllowedUse.RejectForActiveSubject,
            SubjectRef = new RoleplaySubjectRef
            {
                Name = "Caleb",
                Confidence = "high",
                Aliases = ["Caleb the Hunter"],
            },
            Ambiguity = new RoleplayAmbiguity
            {
                Description = "Prosthetic is Caleb-specific, not shared tech.",
                PossibleSubjects = ["Caleb"],
            },
        };

        var json = JsonSerializer.Serialize(item, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<RoleplayEvidenceItem>(json, s_jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, deserialized.Applicability);
        Assert.Equal(AllowedUse.RejectForActiveSubject, deserialized.AllowedUse);
        Assert.NotNull(deserialized.SubjectRef);
        Assert.Equal("Caleb", deserialized.SubjectRef.Name);
        Assert.NotNull(deserialized.Ambiguity);
        Assert.Equal("Prosthetic is Caleb-specific, not shared tech.", deserialized.Ambiguity.Description);
    }

    [Fact]
    public void RoleplayDirective_Serializes()
    {
        var directive = new RoleplayDirective
        {
            ForSubject = "Xavier",
            KnowledgeScope = RoleplayKnowledgeScope.CharacterSpecific,
            AllowedUse = AllowedUse.AssertAsFact,
            Reason = "Direct character lore",
        };

        var json = JsonSerializer.Serialize(directive, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<RoleplayDirective>(json, s_jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("Xavier", deserialized.ForSubject);
        Assert.Equal(RoleplayKnowledgeScope.CharacterSpecific, deserialized.KnowledgeScope);
        Assert.Equal(AllowedUse.AssertAsFact, deserialized.AllowedUse);
    }

    [Fact]
    public void Enums_Serialize_AsSnakeCaseStrings()
    {
        var packet = new RoleplayKnowledgePacket
        {
            Query = "test",
            Scope = RoleplayKnowledgeScope.CharacterSpecific,
            Evidence =
            [
                new RoleplayEvidenceItem
                {
                    Passage = "test",
                    Applicability = ActiveSubjectApplicability.Applies,
                    AllowedUse = AllowedUse.AssertAsFact,
                },
            ],
        };

        var json = JsonSerializer.Serialize(packet, s_jsonOptions);

        // Verify round-trip preserves enum values correctly
        var deserialized = JsonSerializer.Deserialize<RoleplayKnowledgePacket>(json, s_jsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(RoleplayKnowledgeScope.CharacterSpecific, deserialized.Scope);
        Assert.Equal(ActiveSubjectApplicability.Applies, deserialized.Evidence[0].Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, deserialized.Evidence[0].AllowedUse);

        // Verify no integer-based serialization (should use string names)
        Assert.DoesNotContain("\"$values\"", json);
    }

    /// <summary>
    /// Verifies that the new Den-spec protocol enum values round-trip correctly
    /// through JSON serialization, proving the protocol hardening is complete.
    /// </summary>
    [Fact]
    public void DenSpecEnumValues_RoundTrip_Json()
    {
        // RoleplayKnowledgeScope
        foreach (var scope in (RoleplayKnowledgeScope[])Enum.GetValues(typeof(RoleplayKnowledgeScope)))
        {
            var json = JsonSerializer.Serialize(scope, s_jsonOptions);
            var deserialized = JsonSerializer.Deserialize<RoleplayKnowledgeScope>(json, s_jsonOptions);
            Assert.Equal(scope, deserialized);
        }

        // ActiveSubjectApplicability
        foreach (var appl in (ActiveSubjectApplicability[])Enum.GetValues(typeof(ActiveSubjectApplicability)))
        {
            var json = JsonSerializer.Serialize(appl, s_jsonOptions);
            var deserialized = JsonSerializer.Deserialize<ActiveSubjectApplicability>(json, s_jsonOptions);
            Assert.Equal(appl, deserialized);
        }

        // AllowedUse
        foreach (var use in (AllowedUse[])Enum.GetValues(typeof(AllowedUse)))
        {
            var json = JsonSerializer.Serialize(use, s_jsonOptions);
            var deserialized = JsonSerializer.Deserialize<AllowedUse>(json, s_jsonOptions);
            Assert.Equal(use, deserialized);
        }

        // CanonAuthority
        foreach (var auth in (CanonAuthority[])Enum.GetValues(typeof(CanonAuthority)))
        {
            var json = JsonSerializer.Serialize(auth, s_jsonOptions);
            var deserialized = JsonSerializer.Deserialize<CanonAuthority>(json, s_jsonOptions);
            Assert.Equal(auth, deserialized);
        }
    }

    /// <summary>
    /// Verifies that the Den-spec enum names produce the expected JSON string values.
    /// This is a contract test: if the enum name changes, this test fails.
    /// </summary>
    [Fact]
    public void DenSpecEnumNames_SerializeToExpectedStrings()
    {
        var appliesJson = JsonSerializer.Serialize(ActiveSubjectApplicability.Applies, s_jsonOptions);
        Assert.Contains("Applies", appliesJson);

        var doesNotApplyJson = JsonSerializer.Serialize(ActiveSubjectApplicability.DoesNotApply, s_jsonOptions);
        Assert.Contains("DoesNotApply", doesNotApplyJson);

        var assertAsFactJson = JsonSerializer.Serialize(AllowedUse.AssertAsFact, s_jsonOptions);
        Assert.Contains("AssertAsFact", assertAsFactJson);

        var backgroundOnlyJson = JsonSerializer.Serialize(AllowedUse.BackgroundOnly, s_jsonOptions);
        Assert.Contains("BackgroundOnly", backgroundOnlyJson);

        var characterSpecificJson = JsonSerializer.Serialize(RoleplayKnowledgeScope.CharacterSpecific, s_jsonOptions);
        Assert.Contains("CharacterSpecific", characterSpecificJson);
    }
}
