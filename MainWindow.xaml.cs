using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace zapret_gui;

public partial class MainWindow : Window
{
    private GitHubRelease? _latestRelease;
    private bool _isBusy;
    private CancellationTokenSource? _bannerHideCts;
    private readonly List<UIElement> _zapretActionControls = [];
    private CancellationTokenSource? _zapretOperationCts;
    private System.Windows.Controls.Button? _runningZapretButton;
    private string? _runningZapretButtonOriginalContent;

    public MainWindow()
    {
        InitializeComponent();
        ZapretPathTextBox.Text = App.Settings.ZapretPath;
        UpdateInstalledVersionFromFolder(App.Settings.ZapretPath);

        RegisterZapretActionControl(InstallServiceButton);
        RegisterZapretActionControl(RemoveServiceButton);
        RegisterZapretActionControl(CheckStatusButton);
        RegisterZapretActionControl(StrategyComboBox);
        RegisterZapretActionControl(UpdateIpSetListButton);
        RegisterZapretActionControl(UpdateHostsFileButton);
        RegisterZapretActionControl(RunDiagnosticsButton);
        RegisterZapretActionControl(RunConnectivityTestsButton);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        RefreshZapretSections();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder containing zapret",
            UseDescriptionForTitle = true
        };

        var path = ZapretPathTextBox.Text.Trim();
        if (Directory.Exists(path))
            dialog.SelectedPath = path;

        var owner = new WindowInteropHelper(this).Handle;
        if (dialog.ShowDialog(new Win32Window(owner)) != WinForms.DialogResult.OK)
            return;

        ZapretPathTextBox.Text = dialog.SelectedPath;
        SaveZapretPath();
        UpdateInstalledVersionFromFolder(dialog.SelectedPath);
        RefreshZapretSections();
    }

    private void ZapretPathTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveZapretPath();
        RefreshZapretSections();
    }

    private void ZapretPathTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        SaveZapretPath();
        RefreshZapretSections();
    }

    private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdateAsync();

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) =>
        await UpdateAsync();

    private async Task CheckForUpdateAsync()
    {
        if (_isBusy)
            return;

        SetUpdateBusy(true, "Checking...");
        try
        {
            _latestRelease = await ReleaseUpdateService.GetLatestReleaseAsync();
            var latestVersion = _latestRelease.TagName;
            var installedVersion = App.Settings.InstalledVersion;

            if (ZapretVersion.IsNewer(latestVersion, installedVersion))
                ShowUpdateBanner(latestVersion);
            else
                await ShowLatestVersionBannerAsync(installedVersion);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to check for updates.\n\n{ex.Message}",
                "Update check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private async Task UpdateAsync()
    {
        if (_isBusy)
            return;

        var destinationPath = GetZapretPath();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            System.Windows.MessageBox.Show(
                "Set path to desired zapret location first",
                "Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetUpdateBusy(true, "Updating...");
        try
        {
            _latestRelease ??= await ReleaseUpdateService.GetLatestReleaseAsync();

            var downloadUrl = ReleaseUpdateService.GetZipDownloadUrl(_latestRelease);
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new InvalidOperationException("Latest release does not include a .zip download.");

            await ReleaseUpdateService.DownloadAndExtractAsync(downloadUrl, destinationPath);

            App.Settings.InstalledVersion = _latestRelease.TagName;
            SettingsStore.Save(App.Settings);
            await ShowBannerBrieflyAsync("Updated successfully");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Update failed.\n\n{ex.Message}",
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private Task ShowLatestVersionBannerAsync(string version) =>
        ShowBannerBrieflyAsync($"Stored version is {version} - latest");

    private void ShowUpdateBanner(string version)
    {
        _bannerHideCts?.Cancel();
        SetBannerStyle(isSuccess: false);
        UpdateBannerText.Text = $"New version {version} is available.";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async Task ShowBannerBrieflyAsync(string message)
    {
        _bannerHideCts?.Cancel();
        _bannerHideCts = new CancellationTokenSource();
        var cancellationToken = _bannerHideCts.Token;

        SetBannerStyle(isSuccess: true);
        UpdateBannerText.Text = message;
        UpdateBanner.Visibility = Visibility.Visible;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HideUpdateBanner()
    {
        _bannerHideCts?.Cancel();
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void SetBannerStyle(bool isSuccess)
    {
        if (isSuccess)
        {
            UpdateBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD1, 0xE7, 0xDD));
            UpdateBanner.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA3, 0xCF, 0xBB));
        }
        else
        {
            UpdateBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF3, 0xCD));
            UpdateBanner.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE6, 0x9C));
        }
    }

    public void RegisterZapretActionControl(UIElement control)
    {
        if (!_zapretActionControls.Contains(control))
            _zapretActionControls.Add(control);
        control.IsEnabled = !_isBusy;
    }

    public async Task<ZapretBatResult?> RunZapretBatAsync(
        Func<ZapretBatRunner, CancellationToken, Task<ZapretBatResult>> run,
        System.Windows.Controls.Button? runningButton = null,
        bool showErrorDialog = true,
        bool treatNonZeroExitAsFailure = false,
        Func<ZapretBatResult, bool>? indicatesFailure = null)
    {
        if (_isBusy)
            return null;

        var runner = new ZapretBatRunner(GetZapretPath());
        if (!runner.ValidateFolderOrWarn())
            return null;

        indicatesFailure ??= ZapretServiceCommands.IndicatesFailureForMenuAction;
        SetBusy(true);
        ServiceOutputTextBox.Text = WindowsElevation.RunningStatusMessage;
        _zapretOperationCts = new CancellationTokenSource();
        if (runningButton is not null)
            SetZapretOperationUi(runningButton, isRunning: true);

        try
        {
            var result = await run(runner, _zapretOperationCts.Token).ConfigureAwait(true);
            if (showErrorDialog &&
                result.ExitCode != -1 &&
                (indicatesFailure(result) || (treatNonZeroExitAsFailure && !result.Success)))
                ShowZapretBatError(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return new ZapretBatResult(-1, "", "Operation cancelled.");
        }
        finally
        {
            if (runningButton is not null)
                SetZapretOperationUi(runningButton, isRunning: false);
            _zapretOperationCts?.Dispose();
            _zapretOperationCts = null;
            SetBusy(false);
            RefreshZapretSections();
        }
    }

    private void CancelZapretOperationButton_Click(object sender, RoutedEventArgs e) =>
        _zapretOperationCts?.Cancel();

    private void SetZapretOperationUi(System.Windows.Controls.Button runningButton, bool isRunning)
    {
        if (isRunning)
        {
            _runningZapretButton = runningButton;
            _runningZapretButtonOriginalContent = runningButton.Content?.ToString() ?? "";
            runningButton.Content = "Running...";
            CancelZapretOperationButton.Visibility = Visibility.Visible;
            CancelZapretOperationButton.IsEnabled = true;
            return;
        }

        if (_runningZapretButton is not null && _runningZapretButtonOriginalContent is not null)
            _runningZapretButton.Content = _runningZapretButtonOriginalContent;
        _runningZapretButton = null;
        _runningZapretButtonOriginalContent = null;
        CancelZapretOperationButton.Visibility = Visibility.Collapsed;
    }

    private static void ShowZapretBatError(ZapretBatResult result)
    {
        var details = string.Join(
            "\n\n",
            new[] { result.StdOut, result.StdErr }
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(details))
            details = $"Exit code: {result.ExitCode}";

        System.Windows.MessageBox.Show(
            details,
            "Zapret command failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        CheckForUpdateButton.IsEnabled = !busy;
        UpdateButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        ZapretPathTextBox.IsEnabled = !busy;

        foreach (var control in _zapretActionControls)
            control.IsEnabled = !busy;
    }

    private void SetUpdateBusy(bool busy, string? checkButtonText = null)
    {
        SetBusy(busy);
        CheckForUpdateButton.Content = checkButtonText ?? "Check for update";
    }

    private void UpdateInstalledVersionFromFolder(string path)
    {
        if (IsZapretFolder(path))
            return;

        if (App.Settings.InstalledVersion == "0.0.0")
            return;

        App.Settings.InstalledVersion = "0.0.0";
        SettingsStore.Save(App.Settings);
    }

    private static bool IsZapretFolder(string path) =>
        ZapretBatRunner.IsZapretFolder(path);

    private void SaveZapretPath()
    {
        var path = GetZapretPath();
        if (App.Settings.ZapretPath == path)
            return;

        App.Settings.ZapretPath = path;
        SettingsStore.Save(App.Settings);
    }

    private async void InstallServiceButton_Click(object sender, RoutedEventArgs e) =>
        await InstallServiceAsync();

    private async void RemoveServiceButton_Click(object sender, RoutedEventArgs e) =>
        await RemoveServiceAsync();

    private async void CheckStatusButton_Click(object sender, RoutedEventArgs e) =>
        await CheckServiceStatusAsync();

    private async void UpdateIpSetListButton_Click(object sender, RoutedEventArgs e) =>
        await UpdateIpSetListAsync();

    private async void UpdateHostsFileButton_Click(object sender, RoutedEventArgs e) =>
        await UpdateHostsFileAsync();

    private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e) =>
        await RunDiagnosticsAsync();

    private async void RunConnectivityTestsButton_Click(object sender, RoutedEventArgs e) =>
        await RunConnectivityTestsAsync();

    private async Task InstallServiceAsync()
    {
        var selectedBat = StrategyComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedBat))
        {
            System.Windows.MessageBox.Show(
                "Select a strategy .bat file first.",
                "Install service",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var path = GetZapretPath();
        if (ZapretServiceCommands.GetInstallMenuIndex(path, selectedBat) is null)
        {
            System.Windows.MessageBox.Show(
                $"\"{selectedBat}\" is not in the service.bat install file list.",
                "Install service",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await RunZapretBatAsync(
                (runner, ct) => ZapretServiceCommands.InstallServiceAsync(runner, selectedBat, ct));
            if (result is not null)
            {
                SetZapretOutput("Install service", result.Value);
                if (!ZapretServiceCommands.IndicatesInstallCompleted(result.Value))
                {
                    var hasStatusOnly = ZapretServiceCommands.FormatCheckStatusOutput(result.Value)
                        .Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                    System.Windows.MessageBox.Show(
                        hasStatusOnly
                            ? "The log shows Check status output, not install (Pick one / Final args / sc create). " +
                              "The service may already be installed — use Check status, or retry Install."
                            : "The install step did not finish (no service registration in the log). " +
                              "Check the output panel and %LocalAppData%\\zapret-gui\\last-run.log, then retry.",
                        "Install service",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            RefreshInstalledStrategy();
        }
        catch (ArgumentException ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Install service",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task RemoveServiceAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.RemoveServicesAsync(runner, ct));
        if (result is not null)
        {
            ServiceOutputTextBox.Text =
                $"=== Remove service ==={Environment.NewLine}{ZapretServiceCommands.FormatRemoveServiceOutput(result.Value)}";

            if (!ZapretServiceCommands.IndicatesRemoveCompleted(result.Value))
            {
                var hasStatusOnly = ZapretServiceCommands.FormatCheckStatusOutput(result.Value)
                    .Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                System.Windows.MessageBox.Show(
                    hasStatusOnly
                        ? "The log shows Check status output, not remove (no sc delete / taskkill). " +
                          "Try Remove again, or run service.bat → option 2 manually as administrator."
                        : "Remove did not finish (no sc delete in the log). " +
                          "See the output panel and %LocalAppData%\\zapret-gui\\last-run.log.",
                    "Remove service",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        RefreshInstalledStrategy();
    }

    private async Task CheckServiceStatusAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.CheckStatusAsync(runner, ct));
        if (result is not null)
        {
            ServiceOutputTextBox.Text =
                $"=== Check status ==={Environment.NewLine}{ZapretServiceCommands.FormatCheckStatusOutput(result.Value)}";

            var statusText = $"{result.Value.StdOut}\n{result.Value.StdErr}";
            var hasStatus =
                statusText.Contains("service is ", StringComparison.OrdinalIgnoreCase) &&
                (statusText.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
                 statusText.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase));
            if (!hasStatus)
            {
                System.Windows.MessageBox.Show(
                    "Check status did not finish (expected service / WinDivert / winws lines in the log). " +
                    "See %LocalAppData%\\zapret-gui\\last-run.log.",
                    "Check status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        RefreshInstalledStrategy();
    }

    private void RefreshZapretSections()
    {
        RefreshServiceSection();
        RefreshListsSection();
        RefreshDiagnosticsSection();
    }

    private void RefreshServiceSection()
    {
        var path = GetZapretPath();
        var isValid = IsZapretFolder(path);

        InstallServiceButton.IsEnabled = isValid && !_isBusy;
        RemoveServiceButton.IsEnabled = isValid && !_isBusy;
        CheckStatusButton.IsEnabled = isValid && !_isBusy;
        StrategyComboBox.IsEnabled = isValid && !_isBusy;

        if (!isValid)
        {
            StrategyComboBox.ItemsSource = null;
            RefreshInstalledStrategy();
            return;
        }

        var runner = new ZapretBatRunner(path);
        StrategyComboBox.ItemsSource = runner.InstallableBatFiles;
        var defaultBat = runner.InstallableBatFiles.FirstOrDefault(
            f => string.Equals(f, "general.bat", StringComparison.OrdinalIgnoreCase));
        StrategyComboBox.SelectedItem = defaultBat ?? runner.InstallableBatFiles.FirstOrDefault();
        RefreshInstalledStrategy();
    }

    private void RefreshListsSection()
    {
        var path = GetZapretPath();
        var isValid = IsZapretFolder(path);

        UpdateIpSetListButton.IsEnabled = isValid && !_isBusy;
        UpdateHostsFileButton.IsEnabled = isValid && !_isBusy;

        if (!isValid)
        {
            IpSetTimestampTextBlock.Text = "lists\\ipset-all.txt: (invalid zapret folder)";
            HostsTimestampTextBlock.Text = "System hosts file: (invalid zapret folder)";
            return;
        }

        RefreshListTimestamps(path);
    }

    private void RefreshListTimestamps(string zapretPath)
    {
        var ipsetPath = ZapretServiceCommands.GetIpSetListPath(zapretPath);
        var hostsPath = ZapretServiceCommands.GetSystemHostsPath();
        IpSetTimestampTextBlock.Text =
            $"lists\\ipset-all.txt: {ZapretServiceCommands.FormatFileTimestamp(ipsetPath)}";
        HostsTimestampTextBlock.Text =
            $"System hosts file: {ZapretServiceCommands.FormatFileTimestamp(hostsPath)}";
    }

    private async Task UpdateIpSetListAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.UpdateIpSetListAsync(runner, ct));
        if (result is null)
            return;

        SetZapretOutput("Update IPSet list", result.Value);
        RefreshListTimestamps(GetZapretPath());

        if (!ZapretServiceCommands.IndicatesUpdateIpSetCompleted(result.Value))
        {
            System.Windows.MessageBox.Show(
                "IPSet update did not finish (expected \"Updating ipset\" and \"Finished\" in the log). " +
                "See the output panel and %LocalAppData%\\zapret-gui\\last-run.log.",
                "Update IPSet list",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (IsZapretListActionSuccess(result.Value))
        {
            await ShowBannerBrieflyAsync("IPSet list updated");
        }
    }

    private async Task UpdateHostsFileAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "This runs service.bat option 8, which may open Notepad with zapret hosts entries " +
            "for you to merge into the system hosts file:\n\n" +
            $"{ZapretServiceCommands.GetSystemHostsPath()}\n\nContinue?",
            "Update hosts file",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.UpdateHostsFileAsync(runner, ct));
        if (result is null)
            return;

        SetZapretOutput("Update hosts file", result.Value);
        RefreshListTimestamps(GetZapretPath());

        if (!ZapretServiceCommands.IndicatesUpdateHostsCompleted(result.Value))
        {
            System.Windows.MessageBox.Show(
                "Hosts update check did not finish (expected \"Checking hosts file\" in the log). " +
                "See the output panel and %LocalAppData%\\zapret-gui\\last-run.log.",
                "Update hosts file",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (IsZapretListActionSuccess(result.Value))
        {
            await ShowBannerBrieflyAsync("Hosts file check completed");
        }

        if (!ZapretServiceCommands.IndicatesFailure(result.Value))
        {
            ServiceOutputTextBox.Text +=
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Note: service.bat may open Notepad for manual merge into the system hosts file.";
        }
    }

    private static bool IsZapretListActionSuccess(ZapretBatResult result) =>
        ZapretServiceCommands.IsMenuActionSuccess(result);

    private void RefreshDiagnosticsSection()
    {
        var isValid = IsZapretFolder(GetZapretPath());
        RunDiagnosticsButton.IsEnabled = isValid && !_isBusy;
        RunConnectivityTestsButton.IsEnabled = isValid && !_isBusy;
    }

    private async Task RunDiagnosticsAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.RunDiagnosticsAsync(runner, ct),
            runningButton: RunDiagnosticsButton);
        if (result is null)
            return;

        SetZapretOutput("Run diagnostics", result.Value);

        if (!ZapretServiceCommands.IndicatesDiagnosticsCompleted(result.Value))
        {
            System.Windows.MessageBox.Show(
                "Diagnostics did not finish (expected BFE check and Discord cache prompt in the log). " +
                "See the output panel and %LocalAppData%\\zapret-gui\\last-run.log.",
                "Run diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task RunConnectivityTestsAsync()
    {
        var path = GetZapretPath();
        if (!ZapretServiceCommands.ConnectivityTestsScriptExists(path))
        {
            System.Windows.MessageBox.Show(
                $"Connectivity test script was not found:\n\n" +
                $"{ZapretServiceCommands.GetConnectivityTestsScriptPath(path)}\n\n" +
                "Install or update zapret to include utils\\test zapret.ps1.",
                "Run connectivity tests",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.RunTestsAsync(runner, ct),
            runningButton: RunConnectivityTestsButton,
            treatNonZeroExitAsFailure: false,
            indicatesFailure: ZapretServiceCommands.IndicatesFailureForRunTests);
        if (result is null)
            return;

        SetZapretOutput("Run connectivity tests", result.Value);
        if (result.Value.ExitCode == -1)
            return;

        if (!ZapretServiceCommands.IndicatesTestsCompleted(result.Value))
        {
            System.Windows.MessageBox.Show(
                "Tests menu did not run (expected \"Starting configuration tests\" in the log). " +
                "See %LocalAppData%\\zapret-gui\\last-run.log.",
                "Run connectivity tests",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ServiceOutputTextBox.Text +=
            $"{Environment.NewLine}{Environment.NewLine}" +
            "Note: service.bat launches tests in a separate PowerShell window. " +
            "Watch that window for live test output.";
    }

    private void RefreshInstalledStrategy() =>
        InstalledStrategyTextBlock.Text = BuildInstalledStrategyText();

    private string BuildInstalledStrategyText()
    {
        if (!IsZapretFolder(GetZapretPath()))
            return "Installed strategy: (invalid zapret folder)";

        var strategy = ZapretServiceCommands.ReadInstalledStrategy();
        var strategyText = strategy is null ? "(none)" : strategy;
        return $"Installed strategy: {strategyText}";
    }

    private void SetZapretOutput(string action, ZapretBatResult result)
    {
        ServiceOutputTextBox.Text =
            $"=== {action} ==={Environment.NewLine}{ZapretServiceCommands.FormatServiceMenuOutput(result)}";
    }

    private string GetZapretPath() =>
        ZapretPathTextBox.Text.Trim();

    private sealed class Win32Window(nint handle) : WinForms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
