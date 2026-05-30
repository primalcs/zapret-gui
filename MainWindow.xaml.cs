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
    private bool _isApplyingLanguage;
    private string? _checkForUpdateBusyLabel;

    public MainWindow()
    {
        InitializeComponent();

        LanguageComboBox.ItemsSource = LanguageOption.All;
        LanguageComboBox.SelectedItem = LanguageOption.All.First(o => o.Language == Loc.Current);

        Loc.Changed += (_, _) => Dispatcher.Invoke(ApplyLocalization);

        ApplyLocalization();
        ZapretPathTextBox.Text = App.Settings.ZapretPath;
        UpdateInstalledVersionFromFolder(App.Settings.ZapretPath);

        RegisterZapretActionControl(InstallServiceButton);
        RegisterZapretActionControl(RemoveServiceButton);
        RegisterZapretActionControl(TurnOffButton);
        RegisterZapretActionControl(CheckStatusButton);
        RegisterZapretActionControl(StrategyComboBox);
        RegisterZapretActionControl(UpdateIpSetListButton);
        RegisterZapretActionControl(UpdateHostsFileButton);
        RegisterZapretActionControl(RunDiagnosticsButton);
        RegisterZapretActionControl(RunConnectivityTestsButton);
        RegisterZapretActionControl(DoEverythingButton);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        RefreshZapretSections();

    private async void DoEverythingButton_Click(object sender, RoutedEventArgs e) =>
        await DoEverythingAsync();

    private async Task DoEverythingAsync()
    {
        if (_isBusy)
            return;

        var zapretPath = ResolveDoEverythingZapretPath();
        RefreshZapretSections();
        AdvancedExpander.IsExpanded = true;

        SetBusy(true);
        ServiceOutputTextBox.Text = Loc.Running;
        _zapretOperationCts = new CancellationTokenSource();
        SetZapretOperationUi(DoEverythingButton, isRunning: true);
        var cancellationToken = _zapretOperationCts.Token;

        try
        {
            if (ZapretBatRunner.IsZapretFolder(zapretPath))
                await RemoveServicesQuietlyAsync(cancellationToken);

            if (!await CheckAndUpdateIfNeededAsync(zapretPath, cancellationToken, manageBusy: false))
                return;

            var runner = new ZapretBatRunner(zapretPath);
            if (!runner.ValidateFolderOrWarn())
                return;

            await RemoveServicesQuietlyAsync(cancellationToken);

            var strategyCount = runner.InstallableBatFiles.Count;
            const int firstServiceNumber = 9;
            var succeeded = false;

            for (var serviceNumber = firstServiceNumber; serviceNumber <= strategyCount; serviceNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var installResult = await RunZapretBatAsync(
                    (r, ct) => ZapretServiceCommands.InstallServiceByMenuIndexAsync(r, serviceNumber, ct),
                    showErrors: false,
                    manageBusy: false,
                    cancellationToken: cancellationToken);
                if (installResult is null)
                    return;

                SetZapretOutput(
                    Loc.InstallService,
                    installResult.Value,
                    ServiceMenuOperation.Install);

                var statusResult = await RunZapretBatAsync(
                    (r, ct) => ZapretServiceCommands.CheckStatusAsync(r, ct),
                    showErrors: false,
                    manageBusy: false,
                    cancellationToken: cancellationToken);
                if (statusResult is null)
                    return;

                SetZapretOutput(Loc.CheckStatus, statusResult.Value, ServiceMenuOperation.CheckStatus);

                if (ZapretServiceCommands.IsServiceRunningSuccessfully(statusResult.Value))
                {
                    await ShowBannerBrieflyAsync(Loc.DoEverythingSuccess, isSuccess: true);
                    succeeded = true;
                    break;
                }

                await ShowBannerBrieflyAsync(
                    Loc.DoEverythingServiceFailed(serviceNumber),
                    isSuccess: false);

                if (serviceNumber < strategyCount)
                    await RemoveServicesQuietlyAsync(cancellationToken);
            }

            if (!succeeded)
                await ShowBannerBrieflyAsync(Loc.DoEverythingFailed, isSuccess: false);
        }
        catch (OperationCanceledException)
        {
            ServiceOutputTextBox.Text = Loc.OperationCancelled;
        }
        finally
        {
            SetZapretOperationUi(DoEverythingButton, isRunning: false);
            _zapretOperationCts?.Dispose();
            _zapretOperationCts = null;
            SetBusy(false);
            RefreshZapretSections();
        }
    }

    private async Task RemoveServicesQuietlyAsync(CancellationToken cancellationToken)
    {
        var removeResult = await RunZapretBatAsync(
            (r, ct) => ZapretServiceCommands.RemoveServicesAsync(r, ct),
            showErrors: false,
            manageBusy: false,
            cancellationToken: cancellationToken);
        if (removeResult is not null)
            SetZapretOutput(Loc.RemoveServices, removeResult.Value, ServiceMenuOperation.Remove);
    }

    private async Task<bool> CheckAndUpdateIfNeededAsync(
        string destinationPath,
        CancellationToken cancellationToken,
        bool manageBusy = true)
    {
        SetUpdateProgressUi(true, Loc.Checking, manageBusy);
        try
        {
            _latestRelease = await ReleaseUpdateService.GetLatestReleaseAsync(cancellationToken);
            var latestVersion = _latestRelease.TagName;
            var installedVersion = App.Settings.InstalledVersion;
            var folderMissing = !IsZapretFolder(destinationPath);

            if (!folderMissing && !ZapretVersion.IsNewer(latestVersion, installedVersion))
                return true;

            SetUpdateProgressUi(true, Loc.Updating, manageBusy);
            var downloadUrl = ReleaseUpdateService.GetZipDownloadUrl(_latestRelease);
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new InvalidOperationException(Loc.LatestReleaseNoZip);

            await ReleaseUpdateService.DownloadAndExtractAsync(
                downloadUrl,
                destinationPath,
                cancellationToken);

            App.Settings.InstalledVersion = _latestRelease.TagName;
            SettingsStore.Save(App.Settings);
            await ShowBannerBrieflyAsync(Loc.UpdatedSuccessfully, isSuccess: true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Windows.MessageBox.Show(
                Loc.UpdateCheckFailedBody(ex.Message),
                Loc.UpdateCheckFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SetUpdateProgressUi(false, checkButtonText: null, manageBusy);
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingLanguage || LanguageComboBox.SelectedItem is not LanguageOption option)
            return;

        if (option.Language == Loc.Current)
            return;

        Loc.SetLanguage(option.Language);
        App.Settings.Language = Loc.LanguageCode;
        SettingsStore.Save(App.Settings);
    }

    private void ApplyLocalization()
    {
        _isApplyingLanguage = true;
        try
        {
            Title = Loc.WindowTitle;
            DoEverythingButton.Content = Loc.DoEverything;
            TurnOffButton.Content = Loc.TurnOff;
            AdvancedExpander.Header = Loc.Advanced;
            PathToZapretLabel.Content = Loc.PathToZapret;
            BrowseButton.Content = Loc.Browse;
            CheckForUpdateButton.Content = _checkForUpdateBusyLabel ?? Loc.CheckForUpdate;
            UpdateButton.Content = Loc.Update;
            ServiceTab.Header = Loc.TabService;
            ListsTab.Header = Loc.TabLists;
            DiagnosticsTab.Header = Loc.TabDiagnostics;
            StrategyForInstallLabel.Content = Loc.StrategyForInstall;
            InstallServiceButton.Content = Loc.InstallService;
            RemoveServiceButton.Content = Loc.RemoveServices;
            CheckStatusButton.Content = Loc.CheckStatus;
            UpdateIpSetListButton.Content = Loc.UpdateIpSetList;
            UpdateHostsFileButton.Content = Loc.UpdateHostsFile;
            ConnectivityTestsHintTextBlock.Text = Loc.ConnectivityTestsHint;
            RunDiagnosticsButton.Content = Loc.RunDiagnostics;
            RunConnectivityTestsButton.Content = Loc.RunConnectivityTests;
            CancelZapretOperationButton.Content = Loc.Cancel;
            OutputLabel.Content = Loc.Output;

            if (_runningZapretButton is not null)
                _runningZapretButton.Content = Loc.Running;

            RefreshZapretSections();
        }
        finally
        {
            _isApplyingLanguage = false;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = Loc.SelectFolderDescription,
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

        SetUpdateBusy(true, Loc.Checking);
        try
        {
            _latestRelease = await ReleaseUpdateService.GetLatestReleaseAsync();
            var latestVersion = _latestRelease.TagName;
            var installedVersion = App.Settings.InstalledVersion;

            if (ZapretVersion.IsNewer(latestVersion, installedVersion))
                ShowUpdateBanner(latestVersion);
            else
                await ShowBannerBrieflyAsync(Loc.StoredVersionLatest(installedVersion));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                Loc.UpdateCheckFailedBody(ex.Message),
                Loc.UpdateCheckFailedTitle,
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
                Loc.SetPathFirst,
                Loc.Update,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetUpdateBusy(true, Loc.Updating);
        try
        {
            _latestRelease ??= await ReleaseUpdateService.GetLatestReleaseAsync();

            var downloadUrl = ReleaseUpdateService.GetZipDownloadUrl(_latestRelease);
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new InvalidOperationException(Loc.LatestReleaseNoZip);

            await ReleaseUpdateService.DownloadAndExtractAsync(downloadUrl, destinationPath);

            App.Settings.InstalledVersion = _latestRelease.TagName;
            SettingsStore.Save(App.Settings);
            await ShowBannerBrieflyAsync(Loc.UpdatedSuccessfully);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                Loc.UpdateFailedBody(ex.Message),
                Loc.UpdateFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

    private Task ShowBannerBrieflyAsync(string message, bool isSuccess = true)
    {
        _bannerHideCts?.Cancel();
        _bannerHideCts = new CancellationTokenSource();
        var cancellationToken = _bannerHideCts.Token;

        SetBannerStyle(isSuccess);
        UpdateBannerText.Text = message;
        UpdateBanner.Visibility = Visibility.Visible;

        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await Dispatcher.InvokeAsync(() => UpdateBanner.Visibility = Visibility.Collapsed);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void ShowUpdateBanner(string version)
    {
        _bannerHideCts?.Cancel();
        SetBannerStyle(isSuccess: false);
        UpdateBannerText.Text = Loc.NewVersionAvailable(version);
        UpdateBanner.Visibility = Visibility.Visible;
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
        bool showErrors = true,
        bool manageBusy = true,
        CancellationToken cancellationToken = default)
    {
        if (manageBusy && _isBusy)
            return null;

        var runner = new ZapretBatRunner(GetZapretPath());
        if (!runner.ValidateFolderOrWarn())
            return null;

        CancellationTokenSource? ownedCts = null;
        if (manageBusy)
        {
            SetBusy(true);
            ServiceOutputTextBox.Text = Loc.Running;
            ownedCts = new CancellationTokenSource();
            _zapretOperationCts = ownedCts;
        }

        if (runningButton is not null)
            SetZapretOperationUi(runningButton, isRunning: true);

        var token = ownedCts?.Token ?? cancellationToken;

        try
        {
            var result = await run(runner, token).ConfigureAwait(true);
            if (showErrors && ZapretServiceCommands.IndicatesFailure(result))
                ShowZapretBatError(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return new ZapretBatResult(-1, "", Loc.OperationCancelled);
        }
        finally
        {
            if (runningButton is not null)
                SetZapretOperationUi(runningButton, isRunning: false);
            if (ownedCts is not null)
            {
                ownedCts.Dispose();
                if (ReferenceEquals(_zapretOperationCts, ownedCts))
                    _zapretOperationCts = null;
            }

            if (manageBusy)
            {
                SetBusy(false);
                RefreshZapretSections();
            }
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
            runningButton.Content = Loc.Running;
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
        System.Windows.MessageBox.Show(
            ZapretServiceCommands.FormatErrorOutput(result),
            Loc.ZapretCommandFailed,
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
        DoEverythingButton.IsEnabled = !busy;
        LanguageComboBox.IsEnabled = !busy;

        foreach (var control in _zapretActionControls)
            control.IsEnabled = !busy;
    }

    private void SetUpdateBusy(bool busy, string? checkButtonText = null) =>
        SetUpdateProgressUi(busy, checkButtonText, manageBusy: true);

    private void SetUpdateProgressUi(bool busy, string? checkButtonText, bool manageBusy)
    {
        if (manageBusy)
            SetBusy(busy);

        _checkForUpdateBusyLabel = busy ? checkButtonText : null;
        CheckForUpdateButton.Content = checkButtonText ?? Loc.CheckForUpdate;
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

    private async void TurnOffButton_Click(object sender, RoutedEventArgs e) =>
        await RemoveServiceAsync();

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
                Loc.SelectStrategyFirst,
                Loc.InstallService,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await RunZapretBatAsync(
                (runner, ct) => ZapretServiceCommands.InstallServiceAsync(runner, selectedBat, ct));
            if (result is not null)
                SetZapretOutput(Loc.InstallService, result.Value, ServiceMenuOperation.Install);
        }
        catch (ArgumentException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, Loc.InstallService, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RemoveServiceAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.RemoveServicesAsync(runner, ct));
        if (result is not null)
            SetZapretOutput(Loc.RemoveServices, result.Value, ServiceMenuOperation.Remove);
    }

    private async Task CheckServiceStatusAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.CheckStatusAsync(runner, ct));
        if (result is not null)
            SetZapretOutput(Loc.CheckStatus, result.Value, ServiceMenuOperation.CheckStatus);
    }

    private void RefreshZapretSections()
    {
        RefreshServiceRunningBanner();
        RefreshServiceSection();
        RefreshListsSection();
        RefreshDiagnosticsSection();
    }

    private void RefreshServiceRunningBanner()
    {
        var path = GetZapretPath();
        if (string.IsNullOrWhiteSpace(path))
            path = ZapretFolder.DefaultBesideExecutable;

        if (IsZapretFolder(path) &&
            ZapretServiceCommands.TryGetRunningServiceMenuIndex(path, out var menuIndex))
        {
            ServiceRunningBannerText.Text = Loc.ServiceRunning(menuIndex);
            ServiceRunningBanner.Visibility = Visibility.Visible;
            return;
        }

        ServiceRunningBanner.Visibility = Visibility.Collapsed;
    }

    private void RefreshServiceSection()
    {
        var path = GetZapretPath();
        var isValid = IsZapretFolder(path);

        InstallServiceButton.IsEnabled = isValid && !_isBusy;
        RemoveServiceButton.IsEnabled = isValid && !_isBusy;
        TurnOffButton.IsEnabled = isValid && !_isBusy;
        CheckStatusButton.IsEnabled = isValid && !_isBusy;
        StrategyComboBox.IsEnabled = isValid && !_isBusy;

        if (!isValid)
        {
            StrategyComboBox.ItemsSource = null;
            InstalledStrategyTextBlock.Text = Loc.InstalledStrategyInvalidFolder;
            return;
        }

        var runner = new ZapretBatRunner(path);
        StrategyComboBox.ItemsSource = runner.InstallableBatFiles;

        var strategy = ZapretServiceCommands.ReadInstalledStrategy();
        InstalledStrategyTextBlock.Text =
            Loc.InstalledStrategy(strategy is null ? Loc.None : strategy);

        var selectedBat = strategy is null
            ? null
            : runner.InstallableBatFiles.FirstOrDefault(
                f => string.Equals(f, strategy, StringComparison.OrdinalIgnoreCase));
        if (selectedBat is null)
        {
            selectedBat = runner.InstallableBatFiles.FirstOrDefault(
                f => string.Equals(f, "general.bat", StringComparison.OrdinalIgnoreCase))
                ?? runner.InstallableBatFiles.FirstOrDefault();
        }

        StrategyComboBox.SelectedItem = selectedBat;
    }

    private void RefreshListsSection()
    {
        var path = GetZapretPath();
        var isValid = IsZapretFolder(path);

        UpdateIpSetListButton.IsEnabled = isValid && !_isBusy;
        UpdateHostsFileButton.IsEnabled = isValid && !_isBusy;

        if (!isValid)
        {
            IpSetTimestampTextBlock.Text = Loc.IpSetInvalidFolder;
            HostsTimestampTextBlock.Text = Loc.HostsInvalidFolder;
            return;
        }

        var ipsetPath = ZapretServiceCommands.GetIpSetListPath(path);
        var hostsPath = ZapretServiceCommands.GetSystemHostsPath();
        IpSetTimestampTextBlock.Text =
            Loc.IpSetTimestamp(ZapretServiceCommands.FormatFileTimestamp(ipsetPath));
        HostsTimestampTextBlock.Text =
            Loc.HostsTimestamp(ZapretServiceCommands.FormatFileTimestamp(hostsPath));
    }

    private async Task UpdateIpSetListAsync()
    {
        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.UpdateIpSetListAsync(runner, ct));
        if (result is null)
            return;

        SetZapretOutput(Loc.UpdateIpSetList, result.Value, ServiceMenuOperation.UpdateIpSet);
        RefreshListsSection();

        if (!ZapretServiceCommands.IndicatesFailure(result.Value))
            await ShowBannerBrieflyAsync(Loc.IpSetListUpdated);
    }

    private async Task UpdateHostsFileAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            Loc.UpdateHostsConfirm(ZapretServiceCommands.GetSystemHostsPath()),
            Loc.UpdateHostsFile,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.UpdateHostsFileAsync(runner, ct));
        if (result is null)
            return;

        SetZapretOutput(Loc.UpdateHostsFile, result.Value, ServiceMenuOperation.UpdateHosts);
        RefreshListsSection();
    }

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
        if (result is not null)
            SetZapretOutput(Loc.RunDiagnostics, result.Value, ServiceMenuOperation.Diagnostics);
    }

    private async Task RunConnectivityTestsAsync()
    {
        var path = GetZapretPath();
        if (!ZapretServiceCommands.ConnectivityTestsScriptExists(path))
        {
            System.Windows.MessageBox.Show(
                Loc.ConnectivityScriptNotFound(ZapretServiceCommands.GetConnectivityTestsScriptPath(path)),
                Loc.RunConnectivityTests,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = await RunZapretBatAsync(
            (runner, ct) => ZapretServiceCommands.RunTestsAsync(runner, ct),
            runningButton: RunConnectivityTestsButton);
        if (result is null)
            return;

        SetZapretOutput(Loc.RunConnectivityTests, result.Value, ServiceMenuOperation.Tests);
        if (result.Value.ExitCode != -1)
        {
            ServiceOutputTextBox.Text +=
                $"{Environment.NewLine}{Environment.NewLine}" +
                Loc.TestsRunSeparateWindow;
        }
    }

    private void SetZapretOutput(string action, ZapretBatResult result, ServiceMenuOperation operation)
    {
        ServiceOutputTextBox.Text =
            $"{Loc.OutputHeader(action)}{Environment.NewLine}" +
            $"{ZapretServiceCommands.FormatOutput(result, operation)}{Environment.NewLine}{Environment.NewLine}" +
            Loc.LogLine(ZapretBatRunner.LogFilePath);
    }

    private string GetZapretPath() =>
        ZapretPathTextBox.Text.Trim();

    private string ResolveDoEverythingZapretPath()
    {
        var path = GetZapretPath();
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        path = ZapretFolder.EnsureBesideExecutable();
        ZapretPathTextBox.Text = path;
        SaveZapretPath();
        return path;
    }

    private sealed class Win32Window(nint handle) : WinForms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
