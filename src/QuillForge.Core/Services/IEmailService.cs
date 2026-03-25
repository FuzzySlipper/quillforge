namespace QuillForge.Core.Services;

/// <summary>
/// Sends email to the developer for bug reports and feature requests.
/// </summary>
public interface IEmailService
{
    Task SendDeveloperEmailAsync(string subject, string body, CancellationToken ct = default);
}
