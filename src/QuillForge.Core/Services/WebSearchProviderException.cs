namespace QuillForge.Core.Services;

/// <summary>
/// Describes a web-search provider failure with enough metadata for tool handlers
/// to decide whether retrying the same request in the same tool loop is useful.
/// </summary>
public sealed class WebSearchProviderException : Exception
{
    public WebSearchProviderException(
        string provider,
        string message,
        int? statusCode = null,
        bool canRetrySameRequest = true,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
        CanRetrySameRequest = canRetrySameRequest;
    }

    public string Provider { get; }

    public int? StatusCode { get; }

    /// <summary>
    /// False when feeding the same tool call back to the model would likely spin
    /// until max tool rounds. Callers may still retry later after changing the
    /// request, configuration, or external rate-limit state.
    /// </summary>
    public bool CanRetrySameRequest { get; }
}
