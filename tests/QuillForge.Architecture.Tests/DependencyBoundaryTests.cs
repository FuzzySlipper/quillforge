using System.Reflection;

namespace QuillForge.Architecture.Tests;

public class DependencyBoundaryTests
{
    private static readonly Assembly CoreAssembly = typeof(QuillForge.Core.Marker).Assembly;
    private static readonly Assembly ProvidersAssembly = typeof(QuillForge.Providers.Marker).Assembly;
    private static readonly Assembly StorageAssembly = typeof(QuillForge.Storage.Marker).Assembly;

    [Fact]
    public void Core_DoesNot_Reference_Providers()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Providers");
    }

    [Fact]
    public void Core_DoesNot_Reference_Storage()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Storage");
    }

    [Fact]
    public void Core_DoesNot_Reference_Web()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Providers_DoesNot_Reference_Storage()
    {
        var referenced = ProvidersAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Storage");
    }

    [Fact]
    public void Providers_DoesNot_Reference_Web()
    {
        var referenced = ProvidersAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

    [Fact]
    public void Storage_DoesNot_Reference_Providers()
    {
        var referenced = StorageAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Providers");
    }

    [Fact]
    public void Storage_DoesNot_Reference_Web()
    {
        var referenced = StorageAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name == "QuillForge.Web");
    }

}
