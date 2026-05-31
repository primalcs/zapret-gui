namespace zapret_gui;

public class AppSettings
{
    public string ZapretPath { get; set; } = "";

    public string InstalledVersion { get; set; } = "0.0.0";

    public string Language { get; set; } = "en";

    public bool CheckAppUpdatesOnStartup { get; set; } = true;

    public string SkippedAppUpdateVersion { get; set; } = "";
}
