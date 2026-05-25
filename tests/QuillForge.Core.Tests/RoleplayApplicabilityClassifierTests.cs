using QuillForge.Core.Models;
using QuillForge.Core.Services;
using Xunit;

namespace QuillForge.Core.Tests;

public sealed class RoleplayApplicabilityClassifierTests
{
    [Fact]
    public void Classify_ActiveCharacterPassage_ReturnsApplies()
    {
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair and sharp grey eyes. " +
                      "He wears standard Division-issue tactical gear.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier");

        Assert.Equal(ActiveSubjectApplicability.Applies, result);
    }

    [Fact]
    public void Classify_ActiveCharacterSourceFile_ReturnsApplies()
    {
        var passage = "Generic character description without name mention.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "characters/xavier.md");

        Assert.Equal(ActiveSubjectApplicability.Applies, result);
    }

    [Fact]
    public void Classify_OffCharacterSourceFile_ReturnsDoesNotApply()
    {
        var passage = "This character has a custom cybernetic arm.";
        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier", "characters/caleb.md",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, result);
    }

    [Fact]
    public void Classify_OffCharacterSourceFileWithoutRoster_ReturnsDoesNotApply()
    {
        var passage = "This character has a custom cybernetic arm.";

        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier", "characters/caleb.md");

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, result);
    }

    [Fact]
    public void Classify_SharedWorldSourceFile_ReturnsUnknown()
    {
        var passage = "Standard Division neural interfaces are common among all hunter personnel. " +
                      "They provide basic tactical data and communication links.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "world/body-tech.md");

        Assert.Equal(ActiveSubjectApplicability.Unknown, result);
    }

    [Fact]
    public void Classify_OffCharacterMentionInPassage_ReturnsDoesNotApply()
    {
        var passage = "Caleb is known for his advanced prosthetic arm and custom Toring Chip interface. " +
                      "These augmentations set him apart from standard Division operatives.";

        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier",
            offCharacterNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, result);
    }

    [Fact]
    public void Classify_EmptyPassage_ReturnsUnknown()
    {
        var result = RoleplayApplicabilityClassifier.Classify("", "Xavier");
        Assert.Equal(ActiveSubjectApplicability.Unknown, result);
    }

    [Fact]
    public void Classify_NoActiveSubject_ReturnsUnknown()
    {
        var passage = "The city of Linkon is a sprawling metropolis.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, null, "world/setting.md");
        Assert.Equal(ActiveSubjectApplicability.Unknown, result);
    }

    [Fact]
    public void ClassifyAllowedUse_Applies_ReturnsAssertAsFact()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.Applies);
        Assert.Equal(AllowedUse.AssertAsFact, result);
    }

    [Fact]
    public void ClassifyAllowedUse_Unknown_ReturnsBackgroundOnly()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.Unknown);
        Assert.Equal(AllowedUse.BackgroundOnly, result);
    }

    [Fact]
    public void ClassifyAllowedUse_Ambiguous_ReturnsRequiresClarification()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.Ambiguous);
        Assert.Equal(AllowedUse.RequiresClarification, result);
    }

    [Fact]
    public void ClassifyAllowedUse_ExcludedSubject_ReturnsRejectForActiveSubject()
    {
        var passage = "Caleb's Toring Chip interfaces with the Division network.";
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.DoesNotApply,
            "Xavier",
            passage,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(AllowedUse.RejectForActiveSubject, result);
    }

    [Fact]
    public void ClassifyAllowedUse_OffCharacterNotExcluded_ReturnsOffSubjectEvidence()
    {
        var passage = "Caleb has a standard Division carbine.";
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.DoesNotApply,
            "Xavier",
            passage,
            null); // no excluded subjects

        Assert.Equal(AllowedUse.OffSubjectEvidence, result);
    }

    [Fact]
    public void ClassifyScope_Applies_ReturnsCharacterSpecific()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.Applies);
        Assert.Equal(RoleplayKnowledgeScope.CharacterSpecific, result);
    }

    [Fact]
    public void ClassifyScope_Unknown_ReturnsSharedWorld()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.Unknown);
        Assert.Equal(RoleplayKnowledgeScope.SharedWorld, result);
    }

    [Fact]
    public void ClassifyScope_Ambiguous_ReturnsUnknown()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.Ambiguous);
        Assert.Equal(RoleplayKnowledgeScope.Unknown, result);
    }

    [Fact]
    public void ClassifyEvidenceItem_BuildsStructuredItem()
    {
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "characters/xavier.md");

        Assert.Equal(ActiveSubjectApplicability.Applies, item.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, item.AllowedUse);
        Assert.NotNull(item.SourceRefs);
        var ref1 = Assert.Single(item.SourceRefs);
        Assert.Equal("characters/xavier.md", ref1.SourcePath);
        Assert.Equal(CanonAuthority.Canon, ref1.Authority);
        Assert.Equal(SubjectSourceKind.CharacterFile, ref1.SourceKind);
    }

    [Fact]
    public void ClassifyEvidenceItem_SharedWorld_NoSubjectRef()
    {
        var passage = "Standard neural interfaces are common among Division personnel.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "world/body-tech.md");

        Assert.Equal(ActiveSubjectApplicability.Unknown, item.Applicability);
        Assert.Equal(AllowedUse.BackgroundOnly, item.AllowedUse);
        Assert.Null(item.SubjectRef);
    }

    [Fact]
    public void ClassifyEvidenceItem_OffCharacter_HasSubjectRef()
    {
        var passage = "Caleb has a custom prosthetic arm with combat functionality.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "characters/caleb.md",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, item.Applicability);
        Assert.NotNull(item.SubjectRef);
        Assert.Equal("Caleb", item.SubjectRef.Name);
    }

    [Fact]
    public void ClassifyEvidenceItem_OffCharacterSourceWithoutRoster_InferredSubjectRef()
    {
        var passage = "This character has a custom prosthetic arm with combat functionality.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "characters/caleb.md");

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, item.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, item.AllowedUse);
        Assert.NotNull(item.SubjectRef);
        Assert.Equal("caleb", item.SubjectRef.Name);
    }

    [Fact]
    public void ClassifyEvidenceItem_SharedWorldSource_IsCanonBackgroundNotDeprecated()
    {
        var passage = "Standard neural interfaces are common among Division personnel.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "world/body-tech.md");

        var sourceRef = Assert.Single(item.SourceRefs!);
        Assert.Equal(CanonAuthority.Canon, sourceRef.Authority);
        Assert.Equal(AllowedUse.BackgroundOnly, item.AllowedUse);
    }

    [Fact]
    public void XavierCalebCleanScenario_ClassifiesCorrectly()
    {
        // Simulate the Xavier/Caleb clean scenario passages from the drift harness
        var xavierLore = "Xavier is a Deepspace Hunter with silver-streaked black hair and sharp grey eyes. " +
                         "He wears standard Division-issue tactical gear, with recorded equipment limited to documented hunter tools.";

        var sharedTech = "Shared Division records describe standard neural interface equipment common to hunter personnel. " +
                         "Those shared records do not create a unique body-tech detail for Xavier.";

        var offCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        // Xavier lore should be Applies / AssertAsFact / CharacterSpecific
        var xavierResult = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            xavierLore, "Xavier", "characters/xavier.md", offCharacterNames);
        Assert.Equal(ActiveSubjectApplicability.Applies, xavierResult.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, xavierResult.AllowedUse);

        // Shared tech should be Unknown / BackgroundOnly / SharedWorld
        var sharedResult = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            sharedTech, "Xavier", "world/body-tech.md", offCharacterNames);
        Assert.Equal(ActiveSubjectApplicability.Unknown, sharedResult.Applicability);
        Assert.Equal(AllowedUse.BackgroundOnly, sharedResult.AllowedUse);
    }

    [Fact]
    public void Classify_SharedWorldMarkers_Detected()
    {
        var passage = "The standard neural interface is a common augmentation found across all Division operatives.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier");

        Assert.Equal(ActiveSubjectApplicability.Unknown, result);
    }

    [Fact]
    public void Classify_FactionFile_ReturnsUnknown()
    {
        var passage = "The Deepspace Hunter Division is an elite branch of the military.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "factions/hunters.md");

        Assert.Equal(ActiveSubjectApplicability.Unknown, result);
    }

    [Fact]
    public void Classify_AmbiguousPassage_ReturnsAmbiguous()
    {
        // No active subject mentioned, no off-character mentioned, no shared-world markers
        var passage = "The room was dimly lit and smelled of ozone.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier");

        Assert.Equal(ActiveSubjectApplicability.Ambiguous, result);
    }

    [Fact]
    public void Classify_GenericEquipmentScope_UsesSourcePath()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.Unknown, "world/body-tech.md");

        Assert.Equal(RoleplayKnowledgeScope.GenericEquipment, result);
    }

    // ── Active-character-awareness regression tests (#1641) ──

    [Fact]
    public void ClassifyWithDiagnostics_ActiveSubjectApplies_RecordsSourceFileRule()
    {
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair.";
        var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
            passage, "Xavier", "characters/xavier.md");

        Assert.Equal(ActiveSubjectApplicability.Applies, diag.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, diag.AllowedUse);
        Assert.NotEmpty(diag.RulesFired);
        Assert.Contains(diag.RulesFired, r => r.Contains("source-file-matches-active-subject"));
        Assert.Equal("characters/xavier.md", diag.SourcePath);
        Assert.Equal(SubjectSourceKind.CharacterFile, diag.SourceKind);
    }

    [Fact]
    public void ClassifyWithDiagnostics_OffCharacter_RecordsOffNameRule()
    {
        var passage = "Caleb is known for his advanced prosthetic arm and Toring Chip.";
        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };
        var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
            passage, "Xavier", offCharacterNames: offChars);

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, diag.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, diag.AllowedUse);
        Assert.NotEmpty(diag.RulesFired);
    }

    [Fact]
    public void ClassifyWithDiagnostics_SharedWorld_RecordsWorldSourceRule()
    {
        var passage = "Standard Division neural interfaces are common among all hunter personnel.";
        var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
            passage, "Xavier", "world/body-tech.md");

        Assert.Equal(ActiveSubjectApplicability.Unknown, diag.Applicability);
        Assert.Equal(AllowedUse.BackgroundOnly, diag.AllowedUse);
        Assert.NotEmpty(diag.RulesFired);
        Assert.Contains(diag.RulesFired, r => r.Contains("shared-world-source"));
    }

    [Fact]
    public void ClassifyWithDiagnostics_TruncatesLongPassages()
    {
        var longPassage = new string('X', 1000);
        var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
            longPassage, "Xavier");

        Assert.NotNull(diag);
        Assert.True(diag.Passage.Length <= 503); // 500 + "..."
    }

    [Fact]
    public void XavierBodyTechQuery_DoesNotImportCalebProsthetic_Clean()
    {
        // Xavier body/tech scene — Caleb's prosthetic arm is NOT permitted
        var xavierGear = "Xavier uses a standard-issue hunter carbine and carries a combat knife. " +
                         "Like all Division operatives, he's equipped with a standard neural interface.";
        var calebProsthetic = "Caleb has a custom prosthetic arm with advanced combat functionality.";

        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var xavierItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            xavierGear, "Xavier", "characters/xavier.md", offChars);

        var calebItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            calebProsthetic, "Xavier", "characters/caleb.md", offChars);

        // Xavier's own gear is AssertAsFact
        Assert.Equal(ActiveSubjectApplicability.Applies, xavierItem.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, xavierItem.AllowedUse);

        // Caleb's prosthetic is DoesNotApply + OffSubjectEvidence (not excluded explicitly)
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, calebItem.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, calebItem.AllowedUse);
    }

    [Fact]
    public void CalebExplicitlyQueried_AllowsCalebProsthetic()
    {
        // When Caleb IS the active character, his prosthetic arm is AssertAsFact
        var calebLore = "Caleb has a custom prosthetic arm with advanced combat functionality. " +
                        "His Toring Chip interfaces with the Division tactical network.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            calebLore, "Caleb", "characters/caleb.md");

        Assert.Equal(ActiveSubjectApplicability.Applies, item.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, item.AllowedUse);
    }

    [Fact]
    public void CrossCharacterQuery_AllowsBothCharacterDetails()
    {
        // When the query compares Xavier to Caleb, both character details may appear
        // but the classifier should still correctly attribute them
        var xavierGear = "Xavier uses a standard-issue hunter carbine and combat knife.";
        var calebGear = "Caleb has a custom prosthetic arm and Toring Chip.";

        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var xavierItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            xavierGear, "Xavier", "characters/xavier.md", offChars);

        var calebItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            calebGear, "Xavier", "characters/caleb.md", offChars);

        // Xavier's gear applies to Xavier
        Assert.Equal(ActiveSubjectApplicability.Applies, xavierItem.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, xavierItem.AllowedUse);

        // Caleb's gear does not apply to Xavier (it's about Caleb)
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, calebItem.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, calebItem.AllowedUse);
        Assert.NotNull(calebItem.SubjectRef);
        Assert.Equal("Caleb", calebItem.SubjectRef.Name);
    }

    // ── Content-based off-character detection in shared/world files (#1641/#807) ──

    [Fact]
    public void Classify_SharedWorldFileWithOffCharacterMention_ReturnsDoesNotApply()
    {
        // A shared/world file whose content describes an off-character should
        // be classified as DoesNotApply via content-based detection (not via
        // file-path heuristics, which would return Unknown for world paths).
        var passage = "Caleb's custom Toring Chip interface is one of the most advanced " +
                      "neural augmentations in the Division. His prosthetic arm integrates " +
                      "with the tactical network seamlessly.";

        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier", "world/body-tech.md", offChars);

        // Must be DoesNotApply (content mentions off-character Caleb >= 1 time),
        // NOT Unknown (world-file path heuristic).
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, result);
    }

    [Fact]
    public void ClassifyWithDiagnostics_SharedWorldFileOffCharacterMention_RecordsOffNameRule()
    {
        var passage = "Caleb's prosthetic arm interfaces with the Division tactical network. " +
                      "It provides combat functionality beyond standard issue equipment.";
        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
            passage, "Xavier", "world/body-tech.md", offChars, offChars);

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, diag.Applicability);
        // With excludedSubjects = offChars, this passage about Caleb (not Xavier)
        // should be RejectForActiveSubject, not just OffSubjectEvidence
        Assert.Equal(AllowedUse.RejectForActiveSubject, diag.AllowedUse);
        Assert.NotEmpty(diag.RulesFired);
        Assert.Contains(diag.RulesFired, r => r.Contains("off-name-mentions"));
    }

    [Fact]
    public void ClassifyEvidenceItem_SharedWorldFileOffCharacterMention_RejectsForActiveSubject()
    {
        var passage = "Caleb's custom prosthetic arm and Toring Chip interface are " +
                      "unique augmentations not found in standard Division equipment.";
        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "world/body-tech.md", offChars, offChars);

        // Content-based off-character detection overrides the world-file heuristic
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, item.Applicability);
        // Off-character content in a world file that is about Caleb (not Xavier)
        // should be RejectForActiveSubject
        Assert.Equal(AllowedUse.RejectForActiveSubject, item.AllowedUse);
        Assert.NotNull(item.SubjectRef);
        Assert.Equal("Caleb", item.SubjectRef.Name);
    }

    [Fact]
    public void ClassifyEvidenceItem_SharedWorldFileOffCharacterNotExcluded_OffSubjectEvidence()
    {
        // When offCharacterNames is populated but excludedSubjects is NOT passed,
        // DoesNotApply should map to OffSubjectEvidence (not RejectForActiveSubject)
        var passage = "Caleb's custom prosthetic arm interfaces with the Division network.";
        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "world/body-tech.md", offChars, excludedSubjects: null);

        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, item.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, item.AllowedUse);
    }

    [Fact]
    public void BuildStructuredPacket_WithExcludedSubjects_RejectsOffCharacterWorldContent()
    {
        // Simulate what BuildStructuredPacket does: pass offCharacterNames as
        // both the set for applicability classification and as excludedSubjects
        // for allowed-use classification.
        var xavierPassage = "Xavier is a Deepspace Hunter with silver-streaked black hair.";
        var calebWorldPassage = "Caleb's prosthetic arm is a custom combat augmentation.";

        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var xavierItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            xavierPassage, "Xavier", "characters/xavier.md", offChars, offChars);

        var calebItem = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            calebWorldPassage, "Xavier", "world/body-tech.md", offChars, offChars);

        // Xavier's own character file: Applies + AssertAsFact
        Assert.Equal(ActiveSubjectApplicability.Applies, xavierItem.Applicability);
        Assert.Equal(AllowedUse.AssertAsFact, xavierItem.AllowedUse);

        // Caleb mention in a world file: DoesNotApply + RejectForActiveSubject
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, calebItem.Applicability);
        Assert.Equal(AllowedUse.RejectForActiveSubject, calebItem.AllowedUse);
        Assert.NotNull(calebItem.SubjectRef);
        Assert.Equal("Caleb", calebItem.SubjectRef.Name);
    }
}
