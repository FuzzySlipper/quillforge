using System.Reflection;
using System.Text.Json;

namespace QuillForge.Web.Services;

/// <summary>
/// Background service that periodically checks GitHub for new releases.
/// Does NOT auto-install — just notifies via the /api/status endpoint.
/// </summary>
public sealed class AutoUpdateService : BackgroundService
{
    private readonly ILogger<AutoUpdateService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);

    public string? LatestVersion { get; private set; }
    public string? DownloadUrl { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public AutoUpdateService(ILogger<AutoUpdateService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first check so the app starts fast
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForUpdateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check failed");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckForUpdateAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuillForge-UpdateCheck/1.0");

        var response = await client.GetAsync(
            "https://api.github.com/repos/quillforge/quillforge/releases/latest", ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Update check: GitHub returned {StatusCode}", response.StatusCode);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrEmpty(tagName)) return;

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        LatestVersion = tagName.TrimStart('v');

        if (string.Compare(LatestVersion, currentVersion, StringComparison.Ordinal) > 0)
        {
            UpdateAvailable = true;
            DownloadUrl = root.GetProperty("html_url").GetString();
            _logger.LogInformation("Update available: {Current} → {Latest}", currentVersion, LatestVersion);
        }
        else
        {
            UpdateAvailable = false;
            _logger.LogDebug("Up to date: {Version}", currentVersion);
        }
    }
}
