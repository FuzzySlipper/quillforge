namespace QuillForge.Web.Endpoints;

/// <summary>
/// Shared helper for extracting an optional session ID from GET requests.
/// POST endpoints extract session IDs from their deserialized request body instead.
/// </summary>
public static class SessionIdExtensions
{
    public static Guid? TryGetSessionId(this HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("sessionId", out var sid)
            && Guid.TryParse(sid, out var parsed))
        {
            return parsed;
        }
        return null;
    }
}
