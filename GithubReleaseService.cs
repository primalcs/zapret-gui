using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace zapret_gui;

public sealed record GithubReleaseInfo(string Version, string DownloadUrl);

public static class GithubReleaseService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<GithubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GithubReleaseResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Failed to read release information.");

        var version = string.IsNullOrWhiteSpace(release.TagName) ? release.Name : release.TagName;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Latest release has no version tag.");

        var zipAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (zipAsset is null || string.IsNullOrWhiteSpace(zipAsset.BrowserDownloadUrl))
            throw new InvalidOperationException("Latest release has no zip asset.");

        return new GithubReleaseInfo(version.Trim(), zipAsset.BrowserDownloadUrl);
    }

    public static async Task DownloadAndExtractAsync(
        string downloadUrl,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "zapret-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var tempZipPath = Path.Combine(tempDirectory, "zapret.zip");

        try
        {
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = File.Create(tempZipPath))
            {
                if (totalBytes is null or <= 0)
                {
                    await downloadStream.CopyToAsync(fileStream, cancellationToken);
                    progress?.Report(1);
                }
                else
                {
                    var buffer = new byte[81920];
                    long downloadedBytes = 0;
                    int read;

                    while ((read = await downloadStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloadedBytes += read;
                        progress?.Report((double)downloadedBytes / totalBytes.Value);
                    }
                }
            }

            ZipFile.ExtractToDirectory(tempZipPath, destinationDirectory, overwriteFiles: true);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("zapret-gui", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class GithubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GithubAssetResponse> Assets { get; set; } = [];
    }

    private sealed class GithubAssetResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
