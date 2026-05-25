using System.Windows;

namespace zapret_gui;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = SettingsStore.Load();
    }
}
