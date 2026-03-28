using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Email;

/// <summary>
/// IEmailService implementation using the Resend API (https://resend.com).
/// Sends emails to the configured developer address.
/// </summary>
public sealed class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _developerEmail;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        HttpClient http,
        string apiKey,
        string developerEmail,
        ILogger<ResendEmailService> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _developerEmail = developerEmail;
        _logger = logger;
    }

    public async Task SendDeveloperEmailAsync(string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("Sending email to {Email}: {Subject}", _developerEmail, subject);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = "QuillForge <noreply@quillforge.app>",
                to = new[] { _developerEmail },
                subject,
                text = body,
            }),
        };
        request.Headers.Authorization = new("Bearer", _apiKey);

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend API error: {Status} {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Email send failed: {response.StatusCode}");
        }

        _logger.LogInformation("Email sent successfully to {Email}", _developerEmail);
    }
}
