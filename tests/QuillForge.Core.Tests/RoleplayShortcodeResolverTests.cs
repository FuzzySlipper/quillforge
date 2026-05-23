using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class RoleplayShortcodeResolverTests
{
    [Fact]
    public void Substitute_ReplacesCharAndUser_WhenBothProvided()
    {
        var input = "{{char}} smiles at {{user}}. {{char}} is a mage.";
        var result = RoleplayShortcodeResolver.Substitute(input, "Aurora", "Zayne");

        Assert.Equal("Aurora smiles at Zayne. Aurora is a mage.", result);
    }

    [Fact]
    public void Substitute_ReplacesOnlyChar_WhenUserIsMissing()
    {
        var input = "{{char}} is tall. {{user}} is their friend.";
        var result = RoleplayShortcodeResolver.Substitute(input, "Aurora", null);

        Assert.Equal("Aurora is tall. {{user}} is their friend.", result);
    }

    [Fact]
    public void Substitute_ReplacesOnlyUser_WhenCharIsMissing()
    {
        var input = "{{char}} speaks to {{user}}.";
        var result = RoleplayShortcodeResolver.Substitute(input, null, "Zayne");

        Assert.Equal("{{char}} speaks to Zayne.", result);
    }

    [Fact]
    public void Substitute_LeavesAllUnresolved_WhenBothMissing()
    {
        var input = "{{char}} and {{user}} walk together.";
        var result = RoleplayShortcodeResolver.Substitute(input, null, null);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Substitute_ReturnsEmptyString_ForEmptyInput()
    {
        var result = RoleplayShortcodeResolver.Substitute("", "Aurora", "Zayne");
        Assert.Equal("", result);
    }

    [Fact]
    public void Substitute_ReturnsNull_ForNullInput()
    {
        var result = RoleplayShortcodeResolver.Substitute(null!, "Aurora", "Zayne");
        Assert.Null(result);
    }

    [Fact]
    public void Substitute_DoesNotTouchOtherBraces()
    {
        var input = "{{char}} says: {{{reaction}}} to {{user}}.";
        var result = RoleplayShortcodeResolver.Substitute(input, "Aurora", "Zayne");

        Assert.Equal("Aurora says: {{{reaction}}} to Zayne.", result);
    }

    [Fact]
    public void Substitute_DoesNotTouchUnrelatedTemplates()
    {
        var input = "{{char}} uses {{template}} and {{user}}.";
        var result = RoleplayShortcodeResolver.Substitute(input, "Aurora", "Zayne");

        Assert.Equal("Aurora uses {{template}} and Zayne.", result);
    }

    [Fact]
    public void FindUnresolved_ReturnsEmpty_WhenNonePresent()
    {
        var result = RoleplayShortcodeResolver.FindUnresolved("Aurora smiles at Zayne.");
        Assert.Empty(result);
    }

    [Fact]
    public void FindUnresolved_ReturnsChar_WhenCharUnresolved()
    {
        var result = RoleplayShortcodeResolver.FindUnresolved("{{char}} smiles.");
        Assert.Equal(["{{char}}"], result);
    }

    [Fact]
    public void FindUnresolved_ReturnsUser_WhenUserUnresolved()
    {
        var result = RoleplayShortcodeResolver.FindUnresolved("{{user}} nods.");
        Assert.Equal(["{{user}}"], result);
    }

    [Fact]
    public void FindUnresolved_ReturnsBoth_WhenBothUnresolved()
    {
        var result = RoleplayShortcodeResolver.FindUnresolved("{{char}} speaks to {{user}}.");
        Assert.Equal(["{{char}}", "{{user}}"], result);
    }

    [Fact]
    public void FindUnresolved_ReturnsEmpty_ForNullOrEmpty()
    {
        Assert.Empty(RoleplayShortcodeResolver.FindUnresolved(""));
        Assert.Empty(RoleplayShortcodeResolver.FindUnresolved(null!));
    }
}
