using System.Runtime.Versioning;

namespace Den.RulesEngine.Werewolf.Tests;

public sealed class WerewolfModuleProjectMetadataTests
{
    [Fact]
    public void WerewolfModule_ProjectMetadata_IdentifiesExplicitFirstModule()
    {
        var assembly = typeof(global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker).Assembly;

        Assert.Equal("Den.RulesEngine.Werewolf", assembly.GetName().Name);
        Assert.Equal("Den.RulesEngine.Werewolf", global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker.ProjectName);
        Assert.Equal(new global::Den.RulesEngine.GameModuleId("werewolf"), global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker.ModuleId);
        Assert.Equal(new global::Den.RulesEngine.GameModuleVersion("0.1.0"), global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker.ModuleVersion);
        Assert.IsType<global::Den.RulesEngine.Werewolf.WerewolfModule>(global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker.CreateModule());
    }

    [Fact]
    public void WerewolfModule_TargetsNet10()
    {
        var assembly = typeof(global::Den.RulesEngine.Werewolf.WerewolfModuleAssemblyMarker).Assembly;
        var targetFramework = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .OfType<TargetFrameworkAttribute>()
            .Single();

        Assert.Equal(".NETCoreApp,Version=v10.0", targetFramework.FrameworkName);
    }
}
