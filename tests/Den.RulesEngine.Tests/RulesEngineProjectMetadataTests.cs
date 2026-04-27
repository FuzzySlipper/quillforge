using System.Runtime.Versioning;

namespace Den.RulesEngine.Tests;

public sealed class RulesEngineProjectMetadataTests
{
    [Fact]
    public void RulesEngine_ProjectMetadata_IdentifiesPortableAssembly()
    {
        var assembly = typeof(global::Den.RulesEngine.RulesEngineAssemblyMarker).Assembly;

        Assert.Equal("Den.RulesEngine", assembly.GetName().Name);
        Assert.Equal("Den.RulesEngine", global::Den.RulesEngine.RulesEngineAssemblyMarker.ProjectName);
    }

    [Fact]
    public void RulesEngine_TargetsNet10()
    {
        var assembly = typeof(global::Den.RulesEngine.RulesEngineAssemblyMarker).Assembly;
        var targetFramework = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .OfType<TargetFrameworkAttribute>()
            .Single();

        Assert.Equal(".NETCoreApp,Version=v10.0", targetFramework.FrameworkName);
    }
}
