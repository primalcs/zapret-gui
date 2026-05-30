using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace zapret_gui;

public enum ServiceMenuOperation
{
    Install,
    Remove,
    CheckStatus,
    UpdateIpSet,
    UpdateHosts,
    Diagnostics,
    Tests,
}

public static class ZapretServiceCommands
{
    private static readonly Regex DigitSortRegex = new(@"(\d+)", RegexOptions.Compiled);
    private static readonly Regex MenuOptionLineRegex = new(
        @"^\s*\d+\.\s+(Install Service|Remove Services|Check Status|Game Filter|IPSet Filter|Auto-Update|Update IPSet|Update Hosts|Check for Updates|Run Diagnostics|Run Tests|Exit)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstallBatLineRegex = new(
        @"^\d+\.\s+general.*\.bat\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string ArgAdmin = "admin";

    public const string MenuInstall = "1";
    public const string MenuRemove = "2";
    public const string MenuStatus = "3";
    public const string MenuUpdateIpSet = "7";
    public const string MenuUpdateHosts = "8";
    public const string MenuDiagnostics = "10";
    public const string MenuRunTests = "11";

    public static readonly IReadOnlyList<string> StdinRemoveServices = [MenuRemove, ""];
    public static readonly IReadOnlyList<string> StdinCheckStatus = [MenuStatus, ""];
    public static readonly IReadOnlyList<string> StdinUpdateIpSetList = [MenuUpdateIpSet];
    public static readonly IReadOnlyList<string> StdinUpdateHostsFile = [MenuUpdateHosts];
    /// <summary>N = no conflicting-software removal; Y = clear Discord cache (service.bat set /p defaults).</summary>
    public static readonly IReadOnlyList<string> StdinRunDiagnostics = [MenuDiagnostics, "N", "Y", ""];
    public static readonly IReadOnlyList<string> StdinRunTests = [MenuRunTests];

    public static IReadOnlyList<string> StdinInstallService(int installMenuIndex) =>
        [MenuInstall, installMenuIndex.ToString()];

    public static IReadOnlyList<string> GetInstallableBatFiles(string zapretFolder) =>
        Directory.Exists(zapretFolder)
            ? Directory
                .EnumerateFiles(zapretFolder, "*.bat", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null &&
                               !name.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .OrderBy(
                    name => DigitSortRegex.Replace(name, m => m.Value.PadLeft(8, '0')),
                    StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public static int? GetInstallMenuIndex(string zapretFolder, string batFileName)
    {
        var files = GetInstallableBatFiles(zapretFolder);
        var normalizedName = NormalizeStrategyBatFileName(batFileName);
        var index = files.ToList().FindIndex(f =>
            string.Equals(f, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            var stem = Path.GetFileNameWithoutExtension(normalizedName);
            index = files.ToList().FindIndex(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
        }

        return index >= 0 ? index + 1 : null;
    }

    private static string NormalizeStrategyBatFileName(string strategy)
    {
        strategy = strategy.Trim();
        return strategy.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? strategy
            : strategy + ".bat";
    }

    public static string? ReadInstalledStrategy()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            var value = key?.GetValue("zapret-discord-youtube") as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetRunningServiceMenuIndex(string zapretFolder, out int menuIndex)
    {
        menuIndex = 0;
        if (string.IsNullOrWhiteSpace(zapretFolder) || !Directory.Exists(zapretFolder))
            return false;

        if (!IsZapretWindowsServiceRunning())
            return false;

        var strategy = ReadInstalledStrategy();
        if (strategy is null)
            return false;

        var index = GetInstallMenuIndex(zapretFolder, strategy);
        if (index is null)
            return false;

        menuIndex = index.Value;
        return true;
    }

    private static bool IsZapretWindowsServiceRunning()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query zapret",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 &&
                   output.Contains("STATE", StringComparison.OrdinalIgnoreCase) &&
                   output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string GetIpSetListPath(string zapretFolder) =>
        Path.Combine(zapretFolder, "lists", "ipset-all.txt");

    public static string GetSystemHostsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    public static string GetConnectivityTestsScriptPath(string zapretFolder) =>
        Path.Combine(zapretFolder, "utils", "test zapret.ps1");

    public static bool ConnectivityTestsScriptExists(string zapretFolder) =>
        File.Exists(GetConnectivityTestsScriptPath(zapretFolder));

    public static string FormatFileTimestamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTime(path).ToString("g") : Loc.Missing;

    public static Task<ZapretBatResult> InstallServiceAsync(
        ZapretBatRunner runner,
        string strategyBatFileName,
        CancellationToken cancellationToken = default)
    {
        var menuIndex = GetInstallMenuIndex(runner.ZapretFolder, strategyBatFileName)
            ?? throw new ArgumentException(
                Loc.BatNotInInstallList(strategyBatFileName),
                nameof(strategyBatFileName));

        return InstallServiceByMenuIndexAsync(runner, menuIndex, cancellationToken);
    }

    public static Task<ZapretBatResult> InstallServiceByMenuIndexAsync(
        ZapretBatRunner runner,
        int installMenuIndex,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(
            StdinInstallService(installMenuIndex),
            ServiceMenuOperation.Install,
            cancellationToken);

    public static bool IsServiceRunningSuccessfully(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        if (!HasStatusReport(text))
            return false;

        var zapretRunning =
            text.Contains("service is RUNNING", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("is ALREADY RUNNING", StringComparison.OrdinalIgnoreCase);
        if (!zapretRunning)
            return false;

        if (text.Contains("Bypass (winws.exe) is NOT RUNNING", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("winws.exe is NOT RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase) ||
               (text.Contains("winws.exe", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase));
    }

    public static Task<ZapretBatResult> RemoveServicesAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinRemoveServices, ServiceMenuOperation.Remove, cancellationToken);

    public static Task<ZapretBatResult> CheckStatusAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinCheckStatus, ServiceMenuOperation.CheckStatus, cancellationToken);

    public static Task<ZapretBatResult> UpdateIpSetListAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinUpdateIpSetList, ServiceMenuOperation.UpdateIpSet, cancellationToken);

    public static Task<ZapretBatResult> UpdateHostsFileAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinUpdateHostsFile, ServiceMenuOperation.UpdateHosts, cancellationToken);

    public static Task<ZapretBatResult> RunDiagnosticsAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinRunDiagnostics, ServiceMenuOperation.Diagnostics, cancellationToken);

    public static Task<ZapretBatResult> RunTestsAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunServiceMenuAsync(StdinRunTests, ServiceMenuOperation.Tests, cancellationToken);

    public static bool ShouldSendStdin(string output, int lineIndex, IReadOnlyList<string> stdinLines)
    {
        if (lineIndex >= stdinLines.Count)
            return false;

        if (stdinLines[lineIndex].Length == 0)
        {
            return output.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("Для продолжения", StringComparison.OrdinalIgnoreCase);
        }

        if (stdinLines[0] == MenuDiagnostics)
            return ShouldSendDiagnosticsStdin(output, lineIndex, stdinLines);

        return lineIndex switch
        {
            0 => output.Contains("Select option (0-11)", StringComparison.OrdinalIgnoreCase),
            1 when stdinLines[0] == MenuInstall =>
                output.Contains("Input file index", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Conflict prompt is skipped when no conflicting software is found.</summary>
    public static bool CanSkipStdinLine(string output, int lineIndex, IReadOnlyList<string> stdinLines)
    {
        if (stdinLines.Count == 0 || stdinLines[0] != MenuDiagnostics || lineIndex != 1)
            return false;

        return output.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase) &&
               !HasConflictingSoftwarePrompt(output);
    }

    private static bool ShouldSendDiagnosticsStdin(string output, int lineIndex, IReadOnlyList<string> stdinLines) =>
        lineIndex switch
        {
            0 => output.Contains("Select option (0-11)", StringComparison.OrdinalIgnoreCase),
            1 => HasConflictingSoftwarePrompt(output),
            2 => output.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase),
            3 => output.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("Для продолжения", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool HasConflictingSoftwarePrompt(string output) =>
        output.Contains("conflicting software", StringComparison.OrdinalIgnoreCase) ||
        (output.Contains("conflicting", StringComparison.OrdinalIgnoreCase) &&
         output.Contains("(Y/N)", StringComparison.OrdinalIgnoreCase) &&
         !output.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase));

    public static bool IsActionComplete(string output, ServiceMenuOperation operation) =>
        operation switch
        {
            ServiceMenuOperation.Install =>
                output.Contains("Final args:", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("sc create", StringComparison.OrdinalIgnoreCase),
            ServiceMenuOperation.Remove => HasRemoveOutput(output),
            ServiceMenuOperation.CheckStatus => HasStatusReport(output),
            ServiceMenuOperation.UpdateIpSet =>
                output.Contains("Updating ipset", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("Finished", StringComparison.OrdinalIgnoreCase),
            ServiceMenuOperation.UpdateHosts =>
                output.Contains("Checking hosts file", StringComparison.OrdinalIgnoreCase) &&
                (output.Contains("Hosts file is up to date", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("Hosts file needs to be updated", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("Failed to download hosts", StringComparison.OrdinalIgnoreCase)),
            ServiceMenuOperation.Diagnostics => HasDiagnosticsFinished(output),
            ServiceMenuOperation.Tests =>
                output.Contains("Starting configuration tests", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public static bool IndicatesFailure(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Invalid choice", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("The choice is empty", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Failed to download hosts", StringComparison.OrdinalIgnoreCase);
    }

    public static int ResolveExitCode(
        int processExitCode,
        string stdout,
        string stderr,
        ServiceMenuOperation operation)
    {
        if (IsAbnormalTermination(processExitCode) &&
            IsActionComplete($"{stdout}\n{stderr}", operation))
        {
            return 0;
        }

        if (IndicatesFailure(new ZapretBatResult(processExitCode, stdout, stderr)))
            return processExitCode == 0 ? 1 : processExitCode;

        if (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr))
            return 0;

        return processExitCode;
    }

    public static string FormatOutput(ZapretBatResult result, ServiceMenuOperation operation)
    {
        var combined = $"{result.StdOut}\n{result.StdErr}";
        var cleaned = CleanOutput(combined, operation);
        return string.IsNullOrWhiteSpace(cleaned)
            ? Loc.NoOutput
            : cleaned;
    }

    public static string FormatErrorOutput(ZapretBatResult result) =>
        CleanOutput($"{result.StdOut}\n{result.StdErr}", operation: null);

    private static string CleanOutput(string combined, ServiceMenuOperation? operation)
    {
        if (string.IsNullOrWhiteSpace(combined))
            return "";

        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in combined.Replace("\r\n", "\n").Split('\n'))
        {
            var line = NormalizeLine(raw);
            if (line.Length == 0)
                continue;

            if (IsMenuChrome(line))
                continue;

            if (operation is not null && !IsValuableLine(line, operation.Value))
                continue;

            if (operation == ServiceMenuOperation.Install &&
                line.Contains("Final args:", StringComparison.OrdinalIgnoreCase) &&
                line.Length > 400)
            {
                line = line[..400] + "...";
            }

            if (!seen.Add(line))
                continue;

            kept.Add(line);
        }

        return string.Join(Environment.NewLine, kept);
    }

    private static string NormalizeLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
            return "";

        var promptIndex = line.IndexOf("Input file index", StringComparison.OrdinalIgnoreCase);
        if (promptIndex >= 0)
        {
            var after = line[(promptIndex + "Input file index".Length)..];
            var colon = after.IndexOf(':');
            if (colon >= 0)
                line = after[(colon + 1)..].Trim();
        }

        return line;
    }

    private static bool IsMenuChrome(string line) =>
        line.Contains("ZAPRET SERVICE MANAGER", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith(":: ", StringComparison.Ordinal) ||
        line.StartsWith("Select option", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("----", StringComparison.Ordinal) ||
        line.Contains("Started with admin rights", StringComparison.OrdinalIgnoreCase) ||
        MenuOptionLineRegex.IsMatch(line) ||
        InstallBatLineRegex.IsMatch(line) ||
        line.StartsWith("Press any key", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Для продолжения", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("нажмите любую клавишу", StringComparison.OrdinalIgnoreCase) ||
        (line.Length <= 2 && line.All(char.IsDigit));

    private static bool IsValuableLine(string line, ServiceMenuOperation operation) =>
        operation switch
        {
            ServiceMenuOperation.Install => IsInstallLine(line),
            ServiceMenuOperation.Remove => IsRemoveLine(line),
            ServiceMenuOperation.CheckStatus => IsStatusLine(line),
            ServiceMenuOperation.UpdateIpSet => IsIpSetLine(line),
            ServiceMenuOperation.UpdateHosts => IsHostsLine(line),
            ServiceMenuOperation.Diagnostics => IsDiagnosticsLine(line),
            ServiceMenuOperation.Tests => IsTestsLine(line),
            _ => true,
        };

    private static bool IsInstallLine(string line) =>
        line.Contains("Final args:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("sc create", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Pick one of the options", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Strategy:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Service strategy installed", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemoveLine(string line) =>
        line.Contains("sc delete", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("net stop", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("taskkill", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("is not installed", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("zapret", StringComparison.OrdinalIgnoreCase);

    private static bool IsStatusLine(string line) =>
        line.Contains("service is ", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("\"zapret\"", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("winws.exe", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Strategy:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Service strategy installed", StringComparison.OrdinalIgnoreCase);

    private static bool IsIpSetLine(string line) =>
        line.Contains("Updating ipset", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Finished", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("ipset", StringComparison.OrdinalIgnoreCase);

    private static bool IsHostsLine(string line) =>
        line.Contains("hosts", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Checking hosts file", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiagnosticsLine(string line) =>
        line.Contains("Base Filtering Engine", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("check passed", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("check failed", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Proxy check", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("TCP timestamps", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("conflicting", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Discord cache", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("secure DNS", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Adguard", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Killer check", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("VPN check", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestsLine(string line) =>
        line.Contains("Starting configuration tests", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("test zapret", StringComparison.OrdinalIgnoreCase);

    private static bool HasRemoveOutput(string output) =>
        output.Contains("sc delete", StringComparison.OrdinalIgnoreCase) ||
        (output.Contains("is not installed", StringComparison.OrdinalIgnoreCase) &&
         output.Contains("zapret", StringComparison.OrdinalIgnoreCase)) ||
        output.Contains("taskkill", StringComparison.OrdinalIgnoreCase) ||
        (output.Contains("net stop", StringComparison.OrdinalIgnoreCase) &&
         output.Contains("zapret", StringComparison.OrdinalIgnoreCase));

    private static bool HasStatusReport(string output) =>
        (output.Contains("service is NOT running", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("service is RUNNING", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("is ALREADY RUNNING", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("\"zapret\"", StringComparison.OrdinalIgnoreCase)) &&
        (output.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase));

    private static bool HasDiagnosticsFinished(string output) =>
        output.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase) &&
        (output.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("Discord cache cleared", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("cleared Discord cache", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("Deleting Discord", StringComparison.OrdinalIgnoreCase) ||
         output.Contains("cache has been cleared", StringComparison.OrdinalIgnoreCase));

    private static bool IsAbnormalTermination(int exitCode) =>
        exitCode == unchecked((int)0xC0000142);
}
