using System.Windows;
using Updatum;

namespace zapret_gui;

public static class AppUpdateService
{
    private const string RepositoryOwner = "primalcs";
    private const string RepositoryName = "zapret-gui";

    internal static readonly UpdatumManager Updater = new(RepositoryOwner, RepositoryName)
    {
        AssetRegexPattern = $"^{RepositoryName}_",
        AssetExtensionFilter = "exe",
        InstallUpdateWindowsExeType = UpdatumWindowsExeType.Installer,
        InstallUpdateWindowsInstallerArguments =
            "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
    };

    public static string CurrentVersionDisplay =>
        Updater.CurrentVersion?.ToString(3) ?? "0.0.0";

    public static async Task CheckOnStartupAsync(Window owner)
    {
        if (!App.Settings.CheckAppUpdatesOnStartup)
            return;

        await CheckAndPromptAsync(owner, startup: true);
    }

    public static async Task CheckAndPromptAsync(Window owner, bool startup = false)
    {
        if (Updater.IsBusy)
            return;

        try
        {
            var updateFound = await Updater.CheckForUpdatesAsync();
            if (!updateFound)
            {
                if (!startup)
                {
                    System.Windows.MessageBox.Show(
                        owner,
                        Loc.AppVersionLatest(CurrentVersionDisplay),
                        Loc.AppUpdateTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var latestTag = Updater.LatestReleaseTagVersionStr;
            if (startup &&
                !string.IsNullOrWhiteSpace(latestTag) &&
                string.Equals(App.Settings.SkippedAppUpdateVersion, latestTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var changelog = Updater.GetChangelog(3, reverseDisplayOrder: true);
            var body = Loc.AppNewVersionAvailable(latestTag ?? "?");
            if (!string.IsNullOrWhiteSpace(changelog))
                body += "\n\n" + changelog;

            var result = System.Windows.MessageBox.Show(
                owner,
                body,
                Loc.AppUpdateTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                if (!string.IsNullOrWhiteSpace(latestTag))
                {
                    App.Settings.SkippedAppUpdateVersion = latestTag;
                    SettingsStore.Save(App.Settings);
                }

                return;
            }

            await DownloadAndInstallAsync(owner);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                owner,
                Loc.AppUpdateCheckFailedBody(ex.Message),
                Loc.AppUpdateCheckFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public static async Task DownloadAndInstallAsync(Window owner)
    {
        if (Updater.LatestRelease is null)
            throw new InvalidOperationException("No release information available.");

        var progress = new AppUpdateProgressWindow(Updater) { Owner = owner };
        progress.Show();

        UpdatumDownloadedAsset? downloaded;
        try
        {
            downloaded = await Updater.DownloadUpdateAsync(Updater.LatestRelease, progress.CancellationToken);
        }
        finally
        {
            progress.Close();
        }

        if (downloaded is null)
            throw new InvalidOperationException(Loc.AppUpdateDownloadFailed);

        await Updater.InstallUpdateAsync(downloaded);
    }
}
