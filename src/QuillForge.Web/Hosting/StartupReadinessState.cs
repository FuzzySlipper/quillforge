namespace QuillForge.Web.Hosting;

internal sealed class StartupReadinessState
{
    public bool IsReady { get; private set; }

    public void MarkReady()
    {
        IsReady = true;
    }
}
