using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace zapret_gui;

public readonly record struct ZapretBatResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class ZapretBatRunner
{
    private readonly string _zapretFolder;

    public ZapretBatRunner(string zapretFolder)
    {
        _zapretFolder = zapretFolder.Trim();
        ServiceBatPath = Path.Combine(_zapretFolder, "service.bat");
        InstallableBatFiles = ZapretServiceCommands.GetInstallableBatFiles(_zapretFolder);
    }

    public string ZapretFolder => _zapretFolder;

    public string ServiceBatPath { get; }

    public IReadOnlyList<string> InstallableBatFiles { get; }

    public static bool IsZapretFolder(string path) =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "service.bat")) &&
        File.Exists(Path.Combine(path, "general.bat"));

    public bool ValidateFolderOrWarn()
    {
        if (IsZapretFolder(_zapretFolder))
            return true;

        System.Windows.MessageBox.Show(
            "The selected folder is not a valid zapret installation.\n\n" +
            "Both service.bat and general.bat must exist in the folder.",
            "Invalid zapret folder",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public Task<ZapretBatResult> RunProcessAsync(
        string scriptPath,
        string? arguments = null,
        IReadOnlyList<string>? stdinLines = null,
        string? workingDirectory = null,
        bool requireAdmin = false,
        TimeSpan? operationTimeout = null,
        ServiceMenuOperation? menuOperation = null,
        CancellationToken cancellationToken = default) =>
        requireAdmin
            ? RunElevatedAsync(
                scriptPath,
                arguments,
                stdinLines,
                workingDirectory,
                operationTimeout,
                menuOperation,
                cancellationToken)
            : RunDirectAsync(scriptPath, arguments, stdinLines, workingDirectory, cancellationToken);

    private void EnsureValidFolder()
    {
        if (!ValidateFolderOrWarn())
            throw new InvalidOperationException("Invalid zapret folder.");
    }

    private async Task<ZapretBatResult> RunDirectAsync(
        string scriptPath,
        string? arguments,
        IReadOnlyList<string>? stdinLines,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureValidFolder();

        var workDir = ResolveWorkingDirectory(workingDirectory);
        using var process = new Process { StartInfo = CreateProcessStartInfo(scriptPath, arguments, workDir) };

        process.Start();
        await WriteStdinWithDelaysAsync(process, stdinLines, cancellationToken).ConfigureAwait(false);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ZapretBatResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private async Task<ZapretBatResult> RunElevatedAsync(
        string scriptPath,
        string? arguments,
        IReadOnlyList<string>? stdinLines,
        string? workingDirectory,
        TimeSpan? operationTimeout,
        ServiceMenuOperation? menuOperation,
        CancellationToken cancellationToken)
    {
        EnsureValidFolder();

        var workDir = ResolveWorkingDirectory(workingDirectory);
        var timeout = operationTimeout ?? TimeSpan.FromSeconds(20);
        var runDir = Path.Combine(Path.GetTempPath(), "zapret-gui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDir);

        var stdoutFile = Path.Combine(runDir, "out.txt");
        var stderrFile = Path.Combine(runDir, "err.txt");
        var exitFile = Path.Combine(runDir, "exit.txt");
        var wrapperFile = Path.Combine(runDir, "run.cmd");
        var logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zapret-gui",
            "last-run.log");

        try
        {
            bool stoppedOk;
            if (stdinLines is { Count: > 0 })
            {
                var menuOp = menuOperation ?? ServiceMenuOperation.Generic;
                var (echoStoppedOk, elevateExit) = await RunElevatedEchoPipeMenuAsync(
                    scriptPath,
                    arguments,
                    workDir,
                    stdinLines,
                    wrapperFile,
                    stdoutFile,
                    stderrFile,
                    exitFile,
                    timeout,
                    menuOp,
                    logFile,
                    runDir,
                    cancellationToken).ConfigureAwait(false);

                if (elevateExit != 0)
                {
                    return new ZapretBatResult(
                        1,
                        "",
                        elevateExit == 1
                            ? "Administrator permission was denied (UAC cancelled or not shown). " +
                              "Try: close the app, right-click zapret-gui.exe → Run as administrator, then retry."
                            : $"Elevation helper failed (exit code {elevateExit}). See log: {logFile}");
                }

                stoppedOk = echoStoppedOk;
            }
            else if (WindowsElevation.IsAdministrator)
            {
                var wrapper = BuildWrapperCmd(
                    workDir,
                    scriptPath,
                    arguments,
                    null,
                    stdoutFile,
                    stderrFile,
                    exitFile);
                await File.WriteAllTextAsync(wrapperFile, wrapper, cancellationToken).ConfigureAwait(false);

                stoppedOk = await RunElevatedWrapperInProcessAsync(
                    wrapperFile,
                    workDir,
                    stdoutFile,
                    timeout,
                    ServiceMenuOperation.Generic,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var wrapper = BuildWrapperCmd(
                    workDir,
                    scriptPath,
                    arguments,
                    null,
                    stdoutFile,
                    stderrFile,
                    exitFile);
                await File.WriteAllTextAsync(wrapperFile, wrapper, cancellationToken).ConfigureAwait(false);

                var elevateScript = Path.Combine(runDir, "elevate-run.ps1");
                await File.WriteAllTextAsync(
                    elevateScript,
                    BuildElevateRunScript(
                        wrapperFile,
                        workDir,
                        stdoutFile,
                        (int)timeout.TotalSeconds,
                        ServiceMenuOperation.Generic),
                    cancellationToken).ConfigureAwait(false);

                var exitCodeFromScript = await RunViaUacPowerShellAsync(
                    elevateScript,
                    cancellationToken).ConfigureAwait(false);

                if (exitCodeFromScript != 0)
                {
                    await SaveLastRunLogAsync(
                        logFile,
                        runDir,
                        "",
                        $"Elevated PowerShell exited with code {exitCodeFromScript}. Script: {elevateScript}",
                        cancellationToken).ConfigureAwait(false);

                    return new ZapretBatResult(
                        1,
                        "",
                        exitCodeFromScript == 1
                            ? "Administrator permission was denied (UAC cancelled or not shown). " +
                              "Try: close the app, right-click zapret-gui.exe → Run as administrator, then retry."
                            : $"Elevation helper failed (exit code {exitCodeFromScript}). See log: {logFile}");
                }

                stoppedOk = File.Exists(stdoutFile) && new FileInfo(stdoutFile).Length > 0;
            }

            if (stoppedOk && !File.Exists(exitFile))
                await File.WriteAllTextAsync(exitFile, "0", cancellationToken).ConfigureAwait(false);

            var exitCode = 1;
            if (File.Exists(exitFile) &&
                int.TryParse((await File.ReadAllTextAsync(exitFile, cancellationToken).ConfigureAwait(false)).Trim(), out var code))
            {
                exitCode = code;
            }
            else if (stoppedOk)
            {
                exitCode = 0;
            }

            var stdout = File.Exists(stdoutFile)
                ? await File.ReadAllTextAsync(stdoutFile, cancellationToken).ConfigureAwait(false)
                : "";
            var stderr = File.Exists(stderrFile)
                ? await File.ReadAllTextAsync(stderrFile, cancellationToken).ConfigureAwait(false)
                : "";

            await SaveLastRunLogAsync(logFile, runDir, stdout, stderr, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return new ZapretBatResult(-1, stdout, "Operation cancelled.");

            if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            {
                stderr =
                    WindowsElevation.IsAdministrator
                        ? $"No output captured from the elevated command.\n\nDiagnostic log: {logFile}"
                        : $"No output captured. Approve the UAC prompt when it appears (check the taskbar).\n\nDiagnostic log: {logFile}";
            }

            exitCode = ZapretServiceCommands.NormalizeMenuExitCode(exitCode, stdout, stderr);
            return new ZapretBatResult(exitCode, stdout, stderr);
        }
        finally
        {
            TryDeleteDirectory(runDir);
        }
    }

    private static IReadOnlyList<string> PrepareEchoPipeStdin(IReadOnlyList<string> stdinLines)
    {
        if (stdinLines.Count == 0 || stdinLines[^1] == "")
            return stdinLines;

        var withPause = new string[stdinLines.Count + 1];
        for (var i = 0; i < stdinLines.Count; i++)
            withPause[i] = stdinLines[i];
        withPause[^1] = "";
        return withPause;
    }

    private async Task<(bool StoppedOk, int ElevateExitCode)> RunElevatedEchoPipeMenuAsync(
        string scriptPath,
        string? arguments,
        string workDir,
        IReadOnlyList<string> stdinLines,
        string wrapperFile,
        string stdoutFile,
        string stderrFile,
        string exitFile,
        TimeSpan timeout,
        ServiceMenuOperation menuOperation,
        string logFile,
        string runDir,
        CancellationToken cancellationToken)
    {
        var pipeStdin = PrepareEchoPipeStdin(stdinLines);
        var wrapper = BuildWrapperCmd(
            workDir,
            scriptPath,
            arguments,
            pipeStdin,
            stdoutFile,
            stderrFile,
            exitFile);
        await File.WriteAllTextAsync(wrapperFile, wrapper, cancellationToken).ConfigureAwait(false);

        if (WindowsElevation.IsAdministrator)
        {
            var stoppedOk = await RunElevatedWrapperInProcessAsync(
                wrapperFile,
                workDir,
                stdoutFile,
                timeout,
                menuOperation,
                cancellationToken).ConfigureAwait(false);
            return (stoppedOk, 0);
        }

        var elevateScript = Path.Combine(Path.GetDirectoryName(wrapperFile)!, "elevate-run.ps1");
        await File.WriteAllTextAsync(
            elevateScript,
            BuildElevateRunScript(
                wrapperFile,
                workDir,
                stdoutFile,
                (int)timeout.TotalSeconds,
                menuOperation),
            cancellationToken).ConfigureAwait(false);

        var exitCodeFromScript = await RunViaUacPowerShellAsync(elevateScript, cancellationToken)
            .ConfigureAwait(false);

        if (exitCodeFromScript != 0)
        {
            await SaveLastRunLogAsync(
                logFile,
                runDir,
                "",
                $"Elevated PowerShell exited with code {exitCodeFromScript}. Script: {elevateScript}",
                cancellationToken).ConfigureAwait(false);

            return (false, exitCodeFromScript);
        }

        var hasOutput = File.Exists(stdoutFile) && new FileInfo(stdoutFile).Length > 0;
        return (hasOutput, 0);
    }

    private static async Task<bool> RunElevatedWrapperInProcessAsync(
        string wrapperFile,
        string workDir,
        string stdoutFile,
        TimeSpan timeout,
        ServiceMenuOperation menuOperation,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{wrapperFile}\"\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start elevated wrapper.");

        var stoppedOk = await PollUntilCompleteAsync(
            process,
            stdoutFile,
            timeout,
            menuOperation,
            cancellationToken).ConfigureAwait(false);

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        return stoppedOk;
    }

    /// <summary>
    /// Elevates PowerShell via ShellExecute (interactive UAC). Nested Start-Process -Verb RunAs
    /// from a hidden non-interactive child fails silently on many systems.
    /// </summary>
    private static async Task<int> RunViaUacPowerShellAsync(
        string elevateScriptPath,
        CancellationToken cancellationToken)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{elevateScriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 1;
        }

        if (process is null)
            return 1;

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw;
        }
    }

    private static string BuildElevateRunScript(
        string wrapperFile,
        string workDir,
        string stdoutFile,
        int timeoutSeconds,
        ServiceMenuOperation menuOperation)
    {
        var wrapper = PsSingleQuote(wrapperFile);
        var work = PsSingleQuote(workDir);
        var stdout = PsSingleQuote(stdoutFile);
        var actionComplete = GetActionCompletePowerShellExpression(menuOperation);
        var actionIncompleteMenu = PsSingleQuote(GetActionIncompleteMenuPattern(menuOperation));
        var actionSpecific = menuOperation != ServiceMenuOperation.Generic ? "$true" : "$false";
        var stopOnMenuLoop = menuOperation == ServiceMenuOperation.Generic
            ? "(($content -split 'ZAPRET SERVICE MANAGER').Count -ge 3)"
            : "$false";

        return $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $wrapper = '{{wrapper}}'
            $workDir = '{{work}}'
            $stdout = '{{stdout}}'
            $timeoutSec = {{timeoutSeconds}}

            $p = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $wrapper) -WorkingDirectory $workDir -PassThru -WindowStyle Hidden
            if ($null -eq $p) { exit 2 }

            $deadline = [datetime]::UtcNow.AddSeconds($timeoutSec)
            $lastLen = 0L
            $lastOutputAt = [datetime]::UtcNow
            $idleRequired = 4

            while ([datetime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 250
                if (-not (Test-Path -LiteralPath $stdout)) { continue }
                try {
                    $content = [System.IO.File]::ReadAllText($stdout)
                } catch {
                    continue
                }
                $len = $content.Length
                if ($len -gt $lastLen) {
                    $lastLen = $len
                    $lastOutputAt = [datetime]::UtcNow
                }
                if ($len -eq 0) { continue }
                $idle = ([datetime]::UtcNow - $lastOutputAt).TotalSeconds
                if ({{stopOnMenuLoop}}) { break }
                if ({{actionSpecific}} -and $content -match 'ZAPRET SERVICE MANAGER' -and $content -match 'Select option \(0-11\)' -and $content -notmatch '{{actionIncompleteMenu}}') { continue }
                if ($content -match 'Press any key|Для продолжения|нажмите любую клавишу') {
                    if ({{actionComplete}}) { break }
                }
                if ({{actionComplete}}) { break }
                if ($p.HasExited -and $len -gt 50 -and ({{actionComplete}})) { break }
            }

            if (-not $p.HasExited) {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
            exit 0
            """;
    }

    private static string PsSingleQuote(string value) => value.Replace("'", "''");

    private static async Task<bool> PollUntilCompleteAsync(
        Process process,
        string stdoutFile,
        TimeSpan timeout,
        ServiceMenuOperation menuOperation,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var idleSeconds = timeout.TotalSeconds > 60 ? 4 : 2;
        var lastLen = 0L;
        var lastOutputAt = DateTime.UtcNow;
        var lastContent = "";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(stdoutFile))
                continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(stdoutFile, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            lastContent = content;
            var len = content.Length;
            if (len > lastLen)
            {
                lastLen = len;
                lastOutputAt = DateTime.UtcNow;
            }

            if (len == 0)
                continue;

            var idle = (DateTime.UtcNow - lastOutputAt).TotalSeconds;
            if (OutputIndicatesComplete(content, idle, idleSeconds, menuOperation))
                return true;

            if (process.HasExited && len > 50 && !LooksLikeIncompleteMenuAction(content, menuOperation))
                return true;
        }

        return OutputIndicatesComplete(lastContent, idleSeconds, idleSeconds, menuOperation);
    }

    private static bool OutputIndicatesComplete(
        string content,
        double idleSeconds,
        int requiredIdleSeconds,
        ServiceMenuOperation menuOperation)
    {
        if (LooksLikeIncompleteMenuAction(content, menuOperation))
            return false;

        if (MenuActionHasExpectedOutput(content, menuOperation))
            return true;

        if (menuOperation == ServiceMenuOperation.Generic &&
            CountOccurrences(content, "ZAPRET SERVICE MANAGER") >= 2)
        {
            return true;
        }

        return false;
    }

    private static bool MenuActionHasExpectedOutput(string content, ServiceMenuOperation menuOperation) =>
        menuOperation switch
        {
            ServiceMenuOperation.Install => HasInstallCompleted(content),
            ServiceMenuOperation.Remove => HasRemoveOutput(content),
            ServiceMenuOperation.CheckStatus => HasServiceStatusReport(content),
            ServiceMenuOperation.UpdateIpSet => HasUpdateIpSetOutput(content),
            ServiceMenuOperation.UpdateHosts => HasUpdateHostsOutput(content),
            ServiceMenuOperation.Diagnostics => HasDiagnosticsOutput(content),
            ServiceMenuOperation.Tests => HasTestsOutput(content),
            _ => false,
        };

    /// <summary>Regex used in -notmatch while the main menu is waiting for the action (incomplete).</summary>
    private static string GetActionIncompleteMenuPattern(ServiceMenuOperation menuOperation) =>
        menuOperation switch
        {
            ServiceMenuOperation.Install => "Pick one of the options|Final args:|sc create",
            ServiceMenuOperation.Remove => "sc delete|is not installed|taskkill|net stop",
            ServiceMenuOperation.CheckStatus => "service is (NOT running|RUNNING)|Bypass \\(winws",
            ServiceMenuOperation.UpdateIpSet => "Updating ipset|Finished",
            ServiceMenuOperation.UpdateHosts =>
                "Checking hosts file|Hosts file is up to date|Hosts file needs to be updated|Failed to download hosts",
            ServiceMenuOperation.Diagnostics => "Base Filtering Engine|Do you want to clear the Discord cache",
            ServiceMenuOperation.Tests => "Starting configuration tests",
            _ => ".",
        };

    private static string GetActionCompletePowerShellExpression(ServiceMenuOperation menuOperation) =>
        menuOperation switch
        {
            ServiceMenuOperation.Install => "$content -match 'Final args:|sc create'",
            ServiceMenuOperation.Remove => "$content -match 'sc delete|is not installed|taskkill|net stop'",
            ServiceMenuOperation.CheckStatus =>
                "$content -match '(\"zapret\"|service is (NOT running|RUNNING))' -and $content -match 'WinDivert|winws\\.exe|Bypass \\(winws'",
            ServiceMenuOperation.UpdateIpSet =>
                "($content -match 'Updating ipset') -and ($content -match 'Finished')",
            ServiceMenuOperation.UpdateHosts =>
                "($content -match 'Checking hosts file') -and ($content -match 'Hosts file is up to date|Hosts file needs to be updated|Failed to download hosts')",
            ServiceMenuOperation.Diagnostics =>
                "($content -match 'Base Filtering Engine') -and ($content -match 'Do you want to clear the Discord cache')",
            ServiceMenuOperation.Tests => "$content -match 'Starting configuration tests'",
            _ => "$content -match 'ZAPRET SERVICE MANAGER'",
        };

    private static int CountOccurrences(string content, string value) =>
        content.Split([value], StringSplitOptions.None).Length - 1;

    private static bool HasInstallCompleted(string content) =>
        content.Contains("Final args:", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("sc create", StringComparison.OrdinalIgnoreCase);

    private static bool HasRemoveOutput(string content) =>
        content.Contains("sc delete", StringComparison.OrdinalIgnoreCase) ||
        (content.Contains("is not installed", StringComparison.OrdinalIgnoreCase) &&
         content.Contains("zapret", StringComparison.OrdinalIgnoreCase)) ||
        content.Contains("taskkill", StringComparison.OrdinalIgnoreCase) ||
        (content.Contains("net stop", StringComparison.OrdinalIgnoreCase) &&
         content.Contains("zapret", StringComparison.OrdinalIgnoreCase));

    private static bool HasUpdateIpSetOutput(string content) =>
        content.Contains("Updating ipset", StringComparison.OrdinalIgnoreCase) &&
        content.Contains("Finished", StringComparison.OrdinalIgnoreCase);

    private static bool HasUpdateHostsOutput(string content) =>
        content.Contains("Checking hosts file", StringComparison.OrdinalIgnoreCase) &&
        (content.Contains("Hosts file is up to date", StringComparison.OrdinalIgnoreCase) ||
         content.Contains("Hosts file needs to be updated", StringComparison.OrdinalIgnoreCase) ||
         content.Contains("Failed to download hosts", StringComparison.OrdinalIgnoreCase));

    private static bool HasDiagnosticsOutput(string content) =>
        content.Contains("Base Filtering Engine", StringComparison.OrdinalIgnoreCase) &&
        content.Contains("Do you want to clear the Discord cache", StringComparison.OrdinalIgnoreCase);

    private static bool HasTestsOutput(string content) =>
        content.Contains("Starting configuration tests", StringComparison.OrdinalIgnoreCase);

    /// <summary>Main menu redraw without entering the expected action output yet.</summary>
    private static bool LooksLikeIncompleteMenuAction(string content, ServiceMenuOperation menuOperation)
    {
        if (MenuActionHasExpectedOutput(content, menuOperation) ||
            content.Contains("Invalid choice", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("The choice is empty", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (menuOperation == ServiceMenuOperation.Install &&
            content.Contains("Pick one of the options", StringComparison.OrdinalIgnoreCase) &&
            !HasInstallCompleted(content))
        {
            return true;
        }

        if (menuOperation == ServiceMenuOperation.UpdateIpSet &&
            content.Contains("Updating ipset", StringComparison.OrdinalIgnoreCase) &&
            !HasUpdateIpSetOutput(content))
        {
            return true;
        }

        if (menuOperation == ServiceMenuOperation.UpdateHosts &&
            content.Contains("Checking hosts file", StringComparison.OrdinalIgnoreCase) &&
            !HasUpdateHostsOutput(content))
        {
            return true;
        }

        if (menuOperation == ServiceMenuOperation.Diagnostics &&
            content.Contains("Base Filtering Engine", StringComparison.OrdinalIgnoreCase) &&
            !HasDiagnosticsOutput(content))
        {
            return true;
        }

        if (menuOperation == ServiceMenuOperation.Tests &&
            !HasTestsOutput(content) &&
            content.Contains("ZAPRET SERVICE MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HasServiceStatusReport(content) &&
            menuOperation is not ServiceMenuOperation.CheckStatus &&
            !MenuActionHasExpectedOutput(content, menuOperation))
        {
            return true;
        }

        return content.Contains("ZAPRET SERVICE MANAGER", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("Select option (0-11)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasServiceStatusReport(string content)
    {
        var hasServiceLine =
            content.Contains("service is NOT running", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("service is RUNNING", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("is ALREADY RUNNING", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("is STOP_PENDING", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("\"zapret\"", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Service strategy installed", StringComparison.OrdinalIgnoreCase);

        var hasBypassLine =
            content.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("winws.exe", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Bypass (winws.exe)", StringComparison.OrdinalIgnoreCase);

        return hasServiceLine && hasBypassLine;
    }

    private static async Task SaveLastRunLogAsync(
        string logFile,
        string runDir,
        string stdout,
        string stderr,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            var log = new StringBuilder()
                .AppendLine($"Time: {DateTime.Now:O}")
                .AppendLine($"Run dir: {runDir}")
                .AppendLine($"Stdout length: {stdout.Length}")
                .AppendLine($"Stderr length: {stderr.Length}")
                .AppendLine("--- stdout ---")
                .AppendLine(stdout)
                .AppendLine("--- stderr ---")
                .AppendLine(stderr);
            await File.WriteAllTextAsync(logFile, log.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    /// <summary>Wrapper for elevated runs without interactive stdin (output capture only).</summary>
    private static string BuildWrapperCmd(
        string workDir,
        string scriptPath,
        string? arguments,
        IReadOnlyList<string>? stdinLines,
        string stdoutFile,
        string stderrFile,
        string exitFile)
    {
        var argSuffix = string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments.Trim();
        var call = $"call \"{scriptPath}\"{argSuffix}";
        var sb = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("chcp 65001 >nul")
            .AppendLine($"cd /d \"{workDir}\"");

        if (stdinLines is { Count: > 0 })
        {
            var echoes = string.Join(
                " & ",
                stdinLines.Select(line => line.Length == 0 ? "echo." : $"echo {EscapeEchoLine(line)}"));
            // Parenthesize echoes before | so every line feeds call's stdin (not only the last echo).
            sb.AppendLine($"( ( {echoes} ) | {call} )> \"{stdoutFile}\" 2> \"{stderrFile}\"");
        }
        else
        {
            sb.AppendLine($"( {call} )> \"{stdoutFile}\" 2> \"{stderrFile}\"");
        }

        sb.AppendLine($"(echo %ERRORLEVEL%)> \"{exitFile}\"");
        return sb.ToString();
    }

    private static string EscapeEchoLine(string line) =>
        line.Replace("%", "%%").Replace("|", "^|").Replace("&", "^&");

    private string ResolveWorkingDirectory(string? workingDirectory)
    {
        var workDir = string.IsNullOrWhiteSpace(workingDirectory) ? _zapretFolder : workingDirectory.Trim();
        if (!Directory.Exists(workDir))
            throw new DirectoryNotFoundException($"Working directory does not exist: {workDir}");
        return workDir;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string scriptPath, string? arguments, string workDir) =>
        new()
        {
            FileName = "cmd.exe",
            Arguments = BuildCmdArguments(scriptPath, arguments),
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

    private static string BuildCmdArguments(string scriptPath, string? arguments)
    {
        var argSuffix = string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments.Trim();
        return $"/c \"\"{scriptPath}\"{argSuffix}\"";
    }

    private static async Task WriteStdinAdaptivelyAsync(
        Process process,
        IReadOnlyList<string> stdinLines,
        string stdoutFile,
        TimeSpan operationTimeout,
        ServiceMenuOperation menuOperation,
        CancellationToken cancellationToken)
    {
        if (stdinLines is not { Count: > 0 })
            return;

        var markers = ZapretServiceCommands.GetStdinPromptMarkers(stdinLines);
        var waitTimeout = TimeSpan.FromSeconds(Math.Max(operationTimeout.TotalSeconds * 0.6, 15));

        for (var i = 0; i < stdinLines.Count; i++)
        {
            if (i < markers.Count && !string.IsNullOrWhiteSpace(stdoutFile))
            {
                await WaitForStdoutContainsAsync(stdoutFile, markers[i], waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (i == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }

            await process.StandardInput.WriteLineAsync(stdinLines[i].AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        process.StandardInput.Close();
    }

    private static async Task WriteStdinWithDelaysAsync(
        Process process,
        IReadOnlyList<string>? stdinLines,
        CancellationToken cancellationToken) =>
        await WriteStdinAdaptivelyAsync(
            process,
            stdinLines ?? [],
            stdoutFile: "",
            TimeSpan.FromSeconds(20),
            ServiceMenuOperation.Generic,
            cancellationToken).ConfigureAwait(false);

    private static async Task WaitForStdoutAnyAsync(
        string stdoutFile,
        IReadOnlyList<string> markers,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(stdoutFile))
                continue;

            try
            {
                var content = await File.ReadAllTextAsync(stdoutFile, cancellationToken).ConfigureAwait(false);
                if (markers.Any(m => content.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    return;
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task WaitForStdoutContainsAsync(
        string stdoutFile,
        string marker,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(stdoutFile))
                continue;

            try
            {
                var content = await File.ReadAllTextAsync(stdoutFile, cancellationToken).ConfigureAwait(false);
                if (content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
