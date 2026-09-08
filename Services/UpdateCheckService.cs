using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ExCord.Services;

public sealed record UpdateCheckResult(bool IsNewVersionAvailable, string? LatestVersionTag, string? ReleaseUrl);

public sealed class UpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/DA0M912/ExCord/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly object CacheLock = new();
    private static Task<UpdateCheckResult>? _cachedCheckTask;

    static UpdateCheckService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExCord-UpdateChecker");
    }

    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        lock (CacheLock)
        {
            if (_cachedCheckTask is null || _cachedCheckTask.IsCanceled || _cachedCheckTask.IsFaulted)
            {
                _cachedCheckTask = CheckForUpdateCoreAsync(cancellationToken);
            }

            return _cachedCheckTask;
        }
    }

    private static async Task<UpdateCheckResult> CheckForUpdateCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(contentStream);
            var root = document.RootElement;
            var tagName = root.GetProperty("tag_name").GetString();
            var releaseUrl = root.GetProperty("html_url").GetString();

            if (string.IsNullOrWhiteSpace(tagName))
            {
                throw new JsonException("Latest release does not contain a tag name.");
            }

            var versionText = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var latestVersion))
            {
                throw new FormatException($"Invalid release version tag: {tagName}");
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            static Version Normalize(Version? version) =>
                new(Math.Max(version?.Major ?? 0, 0), Math.Max(version?.Minor ?? 0, 0), Math.Max(version?.Build ?? 0, 0));

            var isNewVersionAvailable = Normalize(latestVersion) > Normalize(currentVersion);
            return new UpdateCheckResult(isNewVersionAvailable, tagName, releaseUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "UpdateCheckService");
            throw;
        }
    }
}
