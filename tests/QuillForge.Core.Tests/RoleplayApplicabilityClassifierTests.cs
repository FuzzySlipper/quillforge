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
}
