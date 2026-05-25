using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace zapret_gui;

public enum ServiceMenuOperation
{
    Generic,
    Install,
    CheckStatus,
    Remove,
    UpdateIpSet,
    UpdateHosts,
    Diagnostics,
    Tests,
}

/// <summary>
/// Documented invocation contract for service.bat (v1.9.8c).
/// See docs/README.md → GUI invocation.
/// </summary>
public static class ZapretServiceCommands
{
    private static readonly Regex DigitSortRegex = new(@"(\d+)", RegexOptions.Compiled);

    // --- Non-interactive CLI arguments (handled before admin / menu; no UAC for these alone) ---

    public const string ArgStatusZapret = "status_zapret";
    public const string ArgCheckUpdates = "check_updates";
    public const string ArgCheckUpdatesSoft = "soft";
    public const string ArgLoadGameFilter = "load_game_filter";
    public const string ArgLoadUserLists = "load_user_lists";
    public const string ArgAdmin = "admin";

    // --- Interactive menu digits (require: service.bat admin + stdin) ---

    public const string MenuInstall = "1";
    public const string MenuRemove = "2";
    public const string MenuStatus = "3";
    public const string MenuUpdateIpSet = "7";
    public const string MenuUpdateHosts = "8";
    public const string MenuDiagnostics = "10";
    public const string MenuRunTests = "11";
    public const string MenuExit = "0";

    /// <summary>Stdin for echo-pipe menu actions. Trailing empty line feeds service.bat "pause".</summary>
    public static readonly IReadOnlyList<string> StdinRemoveServices = [MenuRemove, ""];

    public static readonly IReadOnlyList<string> StdinCheckStatus = [MenuStatus, ""];

    public static readonly IReadOnlyList<string> StdinUpdateIpSetList = [MenuUpdateIpSet, ""];

    public static readonly IReadOnlyList<string> StdinUpdateHostsFile = [MenuUpdateHosts, ""];

    /// <summary>Empty lines feed set /p defaults (N/Y) and extra pause when conflict prompt is skipped.</summary>
    public static readonly IReadOnlyList<string> StdinRunDiagnostics = [MenuDiagnostics, "", "", ""];

    public static readonly IReadOnlyList<string> StdinRunTests = [MenuRunTests, ""];

    public static IReadOnlyList<string> StdinInstallService(int installMenuIndex) =>
        [MenuInstall, installMenuIndex.ToString(), ""];

    /// <summary>Wait until stdout contains this text before sending the matching stdin line.</summary>
    public static IReadOnlyList<string> GetStdinPromptMarkers(IReadOnlyList<string> stdinLines)
    {
        if (stdinLines.Count == 0)
            return [];

        if (stdinLines[0] == MenuInstall)
        {
            return stdinLines.Count switch
            {
                1 => ["Select option (0-11)"],
                _ => ["Select option (0-11)", "Input file index (number)"],
            };
        }

        return ["Select option (0-11)"];
    }

    /// <summary>
    /// Same ordering as service.bat install menu (all *.bat except service*).
    /// </summary>
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
        var index = files.ToList().FindIndex(f => string.Equals(f, batFileName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : null;
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

    public static string GetIpSetListPath(string zapretFolder) =>
        Path.Combine(zapretFolder, "lists", "ipset-all.txt");

    public static string GetSystemHostsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    public static string GetConnectivityTestsScriptPath(string zapretFolder) =>
        Path.Combine(zapretFolder, "utils", "test zapret.ps1");

    public static bool ConnectivityTestsScriptExists(string zapretFolder) =>
        File.Exists(GetConnectivityTestsScriptPath(zapretFolder));

    public static string FormatFileTimestamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTime(path).ToString("g") : "(missing)";

    // --- Non-interactive CLI (no admin menu) ---

    public static Task<ZapretBatResult> StatusZapretAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunProcessAsync(
            runner.ServiceBatPath,
            ArgStatusZapret,
            requireAdmin: false,
            cancellationToken: cancellationToken);

    public static Task<ZapretBatResult> CheckUpdatesAsync(
        ZapretBatRunner runner,
        bool soft = true,
        CancellationToken cancellationToken = default) =>
        runner.RunProcessAsync(
            runner.ServiceBatPath,
            soft ? $"{ArgCheckUpdates} {ArgCheckUpdatesSoft}" : ArgCheckUpdates,
            requireAdmin: false,
            cancellationToken: cancellationToken);

    public static Task<ZapretBatResult> LoadGameFilterAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunProcessAsync(
            runner.ServiceBatPath,
            ArgLoadGameFilter,
            requireAdmin: false,
            cancellationToken: cancellationToken);

    public static Task<ZapretBatResult> LoadUserListsAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        runner.RunProcessAsync(
            runner.ServiceBatPath,
            ArgLoadUserLists,
            requireAdmin: false,
            cancellationToken: cancellationToken);

    // --- Menu actions (elevated service.bat admin + stdin) ---

    /// <summary>Menu 1 — pick strategy by install-menu index (see <see cref="GetInstallMenuIndex"/>).</summary>
    public static Task<ZapretBatResult> InstallServiceAsync(
        ZapretBatRunner runner,
        string strategyBatFileName,
        CancellationToken cancellationToken = default)
    {
        var menuIndex = GetInstallMenuIndex(runner.ZapretFolder, strategyBatFileName)
            ?? throw new ArgumentException(
                $"\"{strategyBatFileName}\" is not in the service.bat install file list.",
                nameof(strategyBatFileName));

        return RunServiceMenuAsync(
            runner,
            StdinInstallService(menuIndex),
            ServiceMenuOperation.Install,
            operationTimeout: TimeSpan.FromSeconds(90),
            cancellationToken);
    }

    /// <summary>Menu 2 — remove zapret / WinDivert services and kill winws.exe.</summary>
    public static Task<ZapretBatResult> RemoveServicesAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinRemoveServices,
            ServiceMenuOperation.Remove,
            operationTimeout: TimeSpan.FromSeconds(45),
            cancellationToken);

    /// <summary>Menu 3 — service, WinDivert, winws.exe status.</summary>
    public static Task<ZapretBatResult> CheckStatusAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinCheckStatus,
            ServiceMenuOperation.CheckStatus,
            operationTimeout: TimeSpan.FromSeconds(45),
            cancellationToken);

    /// <summary>Menu 7 — download lists\ipset-all.txt from GitHub.</summary>
    public static Task<ZapretBatResult> UpdateIpSetListAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinUpdateIpSetList,
            ServiceMenuOperation.UpdateIpSet,
            operationTimeout: TimeSpan.FromSeconds(45),
            cancellationToken);

    /// <summary>Menu 8 — compare/merge zapret hosts into System32\drivers\etc\hosts (may open Notepad).</summary>
    public static Task<ZapretBatResult> UpdateHostsFileAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinUpdateHostsFile,
            ServiceMenuOperation.UpdateHosts,
            operationTimeout: TimeSpan.FromSeconds(45),
            cancellationToken);

    /// <summary>Menu 10 — BFE, proxy, TCP timestamps, conflicting software checks.</summary>
    public static Task<ZapretBatResult> RunDiagnosticsAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinRunDiagnostics,
            ServiceMenuOperation.Diagnostics,
            operationTimeout: TimeSpan.FromSeconds(90),
            cancellationToken);

    /// <summary>Menu 11 — launches utils\test zapret.ps1 in a separate PowerShell window.</summary>
    public static Task<ZapretBatResult> RunTestsAsync(
        ZapretBatRunner runner,
        CancellationToken cancellationToken = default) =>
        RunServiceMenuAsync(
            runner,
            StdinRunTests,
            ServiceMenuOperation.Tests,
            operationTimeout: TimeSpan.FromSeconds(45),
            cancellationToken);

    private static Task<ZapretBatResult> RunServiceMenuAsync(
        ZapretBatRunner runner,
        IReadOnlyList<string> stdinLines,
        ServiceMenuOperation operation,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken) =>
        runner.RunProcessAsync(
            runner.ServiceBatPath,
            ArgAdmin,
            stdinLines,
            requireAdmin: true,
            operationTimeout: operationTimeout,
            menuOperation: operation,
            cancellationToken: cancellationToken);

    public static bool IndicatesFailure(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Invalid choice", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("The choice is empty", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Administrator permission was denied", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Failed to download hosts", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesInstallCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Final args:", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sc create", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesRemoveCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("sc delete", StringComparison.OrdinalIgnoreCase) ||
               (text.Contains("is not installed", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("zapret", StringComparison.OrdinalIgnoreCase)) ||
               text.Contains("taskkill", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesUpdateIpSetCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Updating ipset", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("Finished", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesUpdateHostsCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Checking hosts file", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("Hosts file is up to date", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Hosts file needs to be updated", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Failed to download hosts", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IndicatesDiagnosticsCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Base Filtering Engine", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IndicatesTestsCompleted(ZapretBatResult result)
    {
        var text = $"{result.StdOut}\n{result.StdErr}";
        return text.Contains("Starting configuration tests", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatRemoveServiceOutput(ZapretBatResult result) =>
        FormatServiceMenuOutput(result, IsRemoveOutputLine);

    private static bool IsRemoveOutputLine(string trimmed) =>
        trimmed.Contains("Started with admin", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("sc delete", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("net stop", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("taskkill", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("is not installed", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("Press any key", StringComparison.OrdinalIgnoreCase);

    /// <summary>Menu actions are stopped at pause; rely on output text, not exit code.</summary>
    public static bool IndicatesFailureForMenuAction(ZapretBatResult result) =>
        result.ExitCode == -1 || IndicatesFailure(result);

    /// <summary>Option 11 launches an external window; exit code alone is not a reliable failure signal.</summary>
    public static bool IndicatesFailureForRunTests(ZapretBatResult result) =>
        IndicatesFailureForMenuAction(result);

    public static bool IsMenuActionSuccess(ZapretBatResult result) =>
        !IndicatesFailureForMenuAction(result);

    /// <summary>Killing cmd at pause often yields exit code 1 despite success.</summary>
    public static int NormalizeMenuExitCode(int exitCode, string stdout, string stderr)
    {
        if (exitCode is 0 or -1)
            return exitCode;

        if (IndicatesFailure(new ZapretBatResult(exitCode, stdout, stderr)))
            return exitCode;

        return string.IsNullOrWhiteSpace($"{stdout}{stderr}") ? exitCode : 0;
    }

    public static string FormatOutput(ZapretBatResult result)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            builder.AppendLine(result.StdOut.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.AppendLine(result.StdErr.TrimEnd());
        }

        if (builder.Length > 0)
            return builder.ToString();

        return "(no output — if UAC was denied, allow elevation and try again)";
    }

    /// <summary>Strip repeated main-menu redraws after pause; keep the useful action output.</summary>
    public static string FormatServiceMenuOutput(ZapretBatResult result) =>
        FormatServiceMenuOutput(result, _ => true);

    public static string FormatCheckStatusOutput(ZapretBatResult result) =>
        FormatServiceMenuOutput(result, IsCheckStatusLine);

    private static string FormatServiceMenuOutput(
        ZapretBatResult result,
        Func<string, bool> includeLine)
    {
        var combined = $"{result.StdOut}\n{result.StdErr}";
        if (string.IsNullOrWhiteSpace(combined))
            return FormatOutput(result);

        var lines = combined.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedInitialMenu = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (IsMenuChromeLine(trimmed))
            {
                if (skippedInitialMenu && kept.Count > 0)
                    break;

                skippedInitialMenu = true;
                continue;
            }

            if (!includeLine(trimmed))
                continue;

            if (!seen.Add(trimmed))
                continue;

            kept.Add(line);
        }

        if (kept.Count == 0)
            return FormatOutput(result);

        return string.Join(Environment.NewLine, kept);
    }

    private static bool IsMenuChromeLine(string trimmed) =>
        trimmed.Contains("ZAPRET SERVICE MANAGER", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith(":: ", StringComparison.Ordinal) ||
        trimmed.StartsWith("Select option", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("----", StringComparison.Ordinal) ||
        IsEchoPipeLeakLine(trimmed);

    /// <summary>Stray digits from echo-pipe stdin must not appear as action output.</summary>
    private static bool IsEchoPipeLeakLine(string trimmed) =>
        trimmed.Length > 0 &&
        trimmed.Length <= 3 &&
        trimmed.All(char.IsDigit);

    private static bool IsCheckStatusLine(string trimmed) =>
        trimmed.Contains("Started with admin", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("Strategy:", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("Service strategy installed", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("service is ", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("\"zapret\"", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("is ALREADY RUNNING", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("is STOP_PENDING", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("winws.exe", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("Press any key", StringComparison.OrdinalIgnoreCase);
}
