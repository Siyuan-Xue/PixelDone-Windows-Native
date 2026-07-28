using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PixelDone.Core;

namespace PixelDone.Infrastructure;

public enum UpdateState
{
    Current,
    Available,
    Unavailable,
}

public sealed record AppUpdateResult(
    UpdateState State,
    string Message,
    string? Version = null,
    Uri? ReleasePage = null,
    Uri? Download = null);

public sealed class AppUpdateService(HttpClient? httpClient = null)
{
    private const string GitHubApi =
        "https://api.github.com/repos/Siyuan-Xue/PixelDone-Windows-Native/releases?per_page=30";
    private const string GiteeApi =
        "https://gitee.com/api/v5/repos/milesxue/PixelDone-Windows-Native/releases?per_page=30";
    private readonly HttpClient _http = httpClient ?? CreateHttpClient();

    public async Task<AppUpdateResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ProductVersion.TryParse(PixelDoneProduct.Version, out var current))
        {
            return new(UpdateState.Unavailable, "CURRENT VERSION IS INVALID");
        }

        var github = await FetchAsync(GitHubApi, cancellationToken);
        if (github is not null)
        {
            return Select(github, current);
        }

        var gitee = await FetchAsync(GiteeApi, cancellationToken);
        return gitee is null
            ? new(UpdateState.Unavailable, "UPDATE SERVICE UNAVAILABLE")
            : Select(gitee, current);
    }

    public static AppUpdateResult Select(
        IEnumerable<ReleaseDocument> releases,
        ProductVersion current)
    {
        var candidate = releases
            .Where(release => !release.Draft)
            .Select(release => (Release: release, Parsed:
                ProductVersion.TryParse(release.TagName, out var parsed)
                    ? parsed
                    : (ProductVersion?)null))
            .Where(value =>
                value.Parsed is not null && value.Parsed.Value.CompareTo(current) > 0)
            .OrderByDescending(value => value.Parsed)
            .FirstOrDefault(value => value.Release.Assets.Any(IsWindowsInstaller));

        if (candidate.Parsed is null)
        {
            return new(UpdateState.Current, $"PIXELDONE {current} IS CURRENT");
        }

        var asset = candidate.Release.Assets.First(IsWindowsInstaller);
        return new(
            UpdateState.Available,
            $"PIXELDONE {candidate.Parsed} IS AVAILABLE",
            candidate.Parsed.ToString(),
            Uri.TryCreate(candidate.Release.HtmlUrl, UriKind.Absolute, out var page)
                ? page
                : null,
            Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var download)
                ? download
                : null);
    }

    private async Task<IReadOnlyList<ReleaseDocument>?> FetchAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<ReleaseDocument>>(
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool IsWindowsInstaller(ReleaseAsset asset) =>
        asset.Name.EndsWith("-win-x64-setup.exe", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"PixelDone-Windows/{PixelDoneProduct.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

public sealed record ReleaseDocument(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets);

public sealed record ReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
