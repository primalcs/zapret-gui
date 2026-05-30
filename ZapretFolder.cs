using System.IO;

namespace zapret_gui;

public static class ZapretFolder
{
    public static string DefaultBesideExecutable =>
        Path.Combine(AppContext.BaseDirectory, "zapret");

    public static string EnsureBesideExecutable()
    {
        var path = DefaultBesideExecutable;
        Directory.CreateDirectory(path);
        return path;
    }
}
