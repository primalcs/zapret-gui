using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace zapret_gui;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}

public static class ReleaseUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "zapret-gui" } }
    };

    public static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var release = await Http.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidOperationException("Could not read latest release information from GitHub.");

        return release;
    }

    public static string? GetZipDownloadUrl(GitHubRelease release) =>
        release.Assets
            .FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?.BrowserDownloadUrl;

    public static async Task DownloadAndExtractAsync(
        string downloadUrl,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"zapret-update-{Guid.NewGuid():N}.zip");

        try
        {
            using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var fileStream = File.Create(tempZip);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            Directory.CreateDirectory(destinationDirectory);
            ZipFile.ExtractToDirectory(tempZip, destinationDirectory, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }
}
