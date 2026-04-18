namespace QuillForge.Web.Hosting;

internal sealed record BackendRuntimeInfo(
    bool DesktopMode,
    string ContentRoot,
    BackendBindMode BindMode,
    int Port,
    string? DesktopInstanceId,
    bool OpenBrowser,
    string HttpUrl)
{
    public string Mode => DesktopMode ? "desktop" : "web";
}
