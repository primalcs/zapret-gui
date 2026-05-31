using System.ComponentModel;
using System.Windows;
using Updatum;

namespace zapret_gui;

public partial class AppUpdateProgressWindow : Window
{
    private readonly UpdatumManager _updater;

    public AppUpdateProgressWindow(UpdatumManager updater)
    {
        InitializeComponent();
        _updater = updater;
        _updater.PropertyChanged += UpdaterOnPropertyChanged;

        Title = Loc.AppUpdateTitle;
        StatusTextBlock.Text = Loc.AppDownloading;
        DownloadProgressBar.Value = 0;
        ProgressTextBlock.Text = "0 %";

        CancellationTokenSource = new CancellationTokenSource();
        Closed += (_, _) =>
        {
            _updater.PropertyChanged -= UpdaterOnPropertyChanged;
            CancellationTokenSource.Cancel();
        };
    }

    public CancellationTokenSource CancellationTokenSource { get; }

    public CancellationToken CancellationToken => CancellationTokenSource.Token;

    private void UpdaterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UpdatumManager.DownloadedPercentage)
            or nameof(UpdatumManager.DownloadedMegabytes)
            or nameof(UpdatumManager.DownloadSizeMegabytes)))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            DownloadProgressBar.Value = _updater.DownloadedPercentage;
            ProgressTextBlock.Text = Loc.AppDownloadProgress(
                _updater.DownloadedMegabytes,
                _updater.DownloadSizeMegabytes,
                _updater.DownloadedPercentage);
        });
    }
}
