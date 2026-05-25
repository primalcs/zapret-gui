using System.Security.Principal;

namespace zapret_gui;

internal static class WindowsElevation
{
    public static bool IsAdministrator
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string RunningStatusMessage =>
        IsAdministrator
            ? "Running with administrator rights..."
            : "Running... Approve the UAC prompt (check the taskbar if you do not see it).";
}
