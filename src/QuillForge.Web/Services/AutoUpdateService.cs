using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.Web.Services;

/// <summary>
/// Background service that periodically checks GitHub for new releases.
/// Does NOT auto-install — just notifies via the /api/status endpoint.
/// </summary>
public sealed class AutoUpdateService : BackgroundService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/FuzzySlipper/quillforge/releases/latest";
    private readonly ILogger<AutoUpdateService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _startupDelay;

    public string? LatestVersion { get; private set; }
    public string? DownloadUrl { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public AutoUpdateService(ILogger<AutoUpdateService> logger, IHttpClientFactory httpClientFactory, AppConfig appConfig)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _checkInterval = TimeSpan.FromHours(appConfig.Timeouts.UpdateCheckHours);
        _startupDelay = TimeSpan.FromSeconds(appConfig.Timeouts.UpdateStartupDelaySeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first check so the app starts fast
        await Task.Delay(_startupDelay, stoppingToken);

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

        var response = await client.GetAsync(LatestReleaseApiUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Update check: GitHub returned {StatusCode}", response.StatusCode);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (string.IsNullOrEmpty(tagName)) return;

        var currentVersion = BuildInfo.Version;
        LatestVersion = tagName.TrimStart('v');

        if (TryParseVersion(LatestVersion, out var latestVersion) &&
            TryParseVersion(currentVersion, out var parsedCurrentVersion)
            ? latestVersion > parsedCurrentVersion
            : !string.Equals(LatestVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            UpdateAvailable = true;
            DownloadUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            _logger.LogInformation("Update available: {Current} → {Latest}", currentVersion, LatestVersion);
        }
        else
        {
            UpdateAvailable = false;
            DownloadUrl = null;
            _logger.LogDebug("Up to date: {Version}", currentVersion);
        }
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        if (Version.TryParse(value.Trim().TrimStart('v'), out var parsedVersion) &&
            parsedVersion is not null)
        {
            version = parsedVersion;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }
}
