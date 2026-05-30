using System.Security.Principal;
using System.Windows;

namespace zapret_gui;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsStore.Load();
        Loc.SetLanguage(Settings.Language);

        if (!IsAdministrator())
        {
            System.Windows.MessageBox.Show(
                Loc.AdminRequiredBody,
                Loc.AdminRequiredTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
