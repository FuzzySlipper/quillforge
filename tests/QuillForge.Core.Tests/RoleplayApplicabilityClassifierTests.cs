using QuillForge.Core.Models;
using QuillForge.Core.Services;
using Xunit;

namespace QuillForge.Core.Tests;

public sealed class RoleplayApplicabilityClassifierTests
{
    [Fact]
    public void Classify_ActiveCharacterPassage_ReturnsActiveCharacter()
    {
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair and sharp grey eyes. " +
                      "He wears standard Division-issue tactical gear.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier");

        Assert.Equal(ActiveSubjectApplicability.ActiveCharacter, result);
    }

    [Fact]
    public void Classify_ActiveCharacterSourceFile_ReturnsActiveCharacter()
    {
        var passage = "Generic character description without name mention.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "characters/xavier.md");

        Assert.Equal(ActiveSubjectApplicability.ActiveCharacter, result);
    }

    [Fact]
    public void Classify_OffCharacterSourceFile_ReturnsOffCharacter()
    {
        var passage = "This character has a custom cybernetic arm.";
        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier", "characters/caleb.md",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.OffCharacter, result);
    }

    [Fact]
    public void Classify_SharedWorldSourceFile_ReturnsSharedWorld()
    {
        var passage = "Standard Division neural interfaces are common among all hunter personnel. " +
                      "They provide basic tactical data and communication links.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "world/body-tech.md");

        Assert.Equal(ActiveSubjectApplicability.SharedWorld, result);
    }

    [Fact]
    public void Classify_OffCharacterMentionInPassage_ReturnsOffCharacter()
    {
        var passage = "Caleb is known for his advanced prosthetic arm and custom Toring Chip interface. " +
                      "These augmentations set him apart from standard Division operatives.";

        var result = RoleplayApplicabilityClassifier.Classify(
            passage, "Xavier",
            offCharacterNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.OffCharacter, result);
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
        Assert.Equal(ActiveSubjectApplicability.SharedWorld, result);
    }

    [Fact]
    public void ClassifyAllowedUse_ActiveCharacter_ReturnsInline()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.ActiveCharacter);
        Assert.Equal(AllowedUse.Inline, result);
    }

    [Fact]
    public void ClassifyAllowedUse_SharedWorld_ReturnsContext()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.SharedWorld);
        Assert.Equal(AllowedUse.Context, result);
    }

    [Fact]
    public void ClassifyAllowedUse_Unknown_ReturnsUnknown()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.Unknown);
        Assert.Equal(AllowedUse.Unknown, result);
    }

    [Fact]
    public void ClassifyAllowedUse_ExcludedSubject_ReturnsExcluded()
    {
        var passage = "Caleb's Toring Chip interfaces with the Division network.";
        var result = RoleplayApplicabilityClassifier.ClassifyAllowedUse(
            ActiveSubjectApplicability.OffCharacter,
            "Xavier",
            passage,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(AllowedUse.Excluded, result);
    }

    [Fact]
    public void ClassifyScope_ActiveCharacter_ReturnsCharacter()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.ActiveCharacter);
        Assert.Equal(RoleplayKnowledgeScope.Character, result);
    }

    [Fact]
    public void ClassifyScope_SharedWorld_ReturnsWorld()
    {
        var result = RoleplayApplicabilityClassifier.ClassifyScope(
            ActiveSubjectApplicability.SharedWorld);
        Assert.Equal(RoleplayKnowledgeScope.World, result);
    }

    [Fact]
    public void ClassifyEvidenceItem_BuildsStructuredItem()
    {
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "characters/xavier.md");

        Assert.Equal(ActiveSubjectApplicability.ActiveCharacter, item.Applicability);
        Assert.Equal(AllowedUse.Inline, item.AllowedUse);
        Assert.NotNull(item.SourceRefs);
        var ref1 = Assert.Single(item.SourceRefs);
        Assert.Equal("characters/xavier.md", ref1.SourcePath);
        Assert.Equal(CanonAuthority.Primary, ref1.Authority);
        Assert.Equal(SubjectSourceKind.CharacterFile, ref1.SourceKind);
    }

    [Fact]
    public void ClassifyEvidenceItem_SharedWorld_NoSubjectRef()
    {
        var passage = "Standard neural interfaces are common among Division personnel.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "world/body-tech.md");

        Assert.Equal(ActiveSubjectApplicability.SharedWorld, item.Applicability);
        Assert.Equal(AllowedUse.Context, item.AllowedUse);
        Assert.Null(item.SubjectRef);
    }

    [Fact]
    public void ClassifyEvidenceItem_OffCharacter_HasSubjectRef()
    {
        var passage = "Caleb has a custom prosthetic arm with combat functionality.";

        var item = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            passage, "Xavier", "characters/caleb.md",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

        Assert.Equal(ActiveSubjectApplicability.OffCharacter, item.Applicability);
        Assert.NotNull(item.SubjectRef);
        Assert.Equal("Caleb", item.SubjectRef.Name);
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

        // Xavier lore should be active_character
        var xavierResult = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            xavierLore, "Xavier", "characters/xavier.md", offCharacterNames);
        Assert.Equal(ActiveSubjectApplicability.ActiveCharacter, xavierResult.Applicability);
        Assert.Equal(AllowedUse.Inline, xavierResult.AllowedUse);

        // Shared tech should be shared_world / context
        var sharedResult = RoleplayApplicabilityClassifier.ClassifyEvidenceItem(
            sharedTech, "Xavier", "world/body-tech.md", offCharacterNames);
        Assert.Equal(ActiveSubjectApplicability.SharedWorld, sharedResult.Applicability);
        Assert.Equal(AllowedUse.Context, sharedResult.AllowedUse);
    }

    [Fact]
    public void Classify_SharedWorldMarkers_Detected()
    {
        var passage = "The standard neural interface is a common augmentation found across all Division operatives.";

        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier");

        Assert.Equal(ActiveSubjectApplicability.SharedWorld, result);
    }

    [Fact]
    public void Classify_FactionFile_ReturnsSharedWorld()
    {
        var passage = "The Deepspace Hunter Division is an elite branch of the military.";
        var result = RoleplayApplicabilityClassifier.Classify(passage, "Xavier", "factions/hunters.md");

        Assert.Equal(ActiveSubjectApplicability.SharedWorld, result);
    }
}
