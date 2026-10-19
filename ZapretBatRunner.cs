using System.Diagnostics;
using System.IO;
using System.Text;

namespace zapret_gui;

public readonly record struct ZapretBatResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class ZapretBatRunner
{
    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DrainAfterComplete = TimeSpan.FromSeconds(2);

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

    public static string LogFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zapret-gui",
            "zapret-gui.log");

    public static bool IsZapretFolder(string path) =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "service.bat")) &&
        File.Exists(Path.Combine(path, "general.bat"));

    /// <summary>
    /// Folder a release zip should be unpacked into. A path that is already
    /// <c>zapret-discord-youtube-&lt;version&gt;</c> unpacks into its parent so the next
    /// version lands beside it instead of inside it.
    /// </summary>
    public static string GetExtractDirectory(string zapretPath)
    {
        var trimmed = TrimPath(zapretPath);
        var name = Path.GetFileName(trimmed);
        if (name.StartsWith("zapret-discord-youtube-", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrEmpty(parent))
                return parent;
        }

        return trimmed;
    }

    /// <summary>
    /// Release zips since 1.9.9c unpack as <c>zapret-discord-youtube-&lt;version&gt;</c>.
    /// Returns that directory when it holds <c>service.bat</c> and <c>general.bat</c>.
    /// Flat archives stay at <paramref name="path"/>.
    /// </summary>
    public static string ResolveInstalledFolder(string path, string? releaseTag = null)
    {
        path = TrimPath(path);
        if (path.Length == 0 || !Directory.Exists(path))
            return path;

        if (!string.IsNullOrWhiteSpace(releaseTag))
        {
            var tag = releaseTag.Trim();
            if (tag.StartsWith('v') || tag.StartsWith('V'))
                tag = tag[1..];

            var named = Path.Combine(path, "zapret-discord-youtube-" + tag);
            if (IsZapretFolder(named))
                return named;
        }

        if (IsZapretFolder(path))
            return path;

        string[] children;
        try
        {
            children = Directory.GetDirectories(path);
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }

        var matches = children.Where(IsZapretFolder).ToArray();
        if (matches.Length == 1)
            return matches[0];

        if (matches.Length > 1)
            return matches.OrderByDescending(Directory.GetLastWriteTimeUtc).First();

        return path;
    }

    private static string TrimPath(string path) =>
        path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public bool ValidateFolderOrWarn()
    {
        if (IsZapretFolder(_zapretFolder))
            return true;

        System.Windows.MessageBox.Show(
            Loc.InvalidZapretFolderBody,
            Loc.InvalidZapretFolderTitle,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        return false;
    }

    public async Task<ZapretBatResult> RunServiceMenuAsync(
        IReadOnlyList<string> stdinLines,
        ServiceMenuOperation operation,
        CancellationToken cancellationToken = default)
    {
        EnsureValidFolder();

        using var process = new Process { StartInfo = CreateStartInfo(ServiceBatPath, "admin") };
        process.Start();

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stderrTask = ReadStreamAsync(process.StandardError, stderr, cancellationToken);

        var stdinSent = 0;
        var stdinLog = new List<string>();
        var completed = false;
        var cancelledByUser = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operation == ServiceMenuOperation.Diagnostics ? DiagnosticsTimeout : MenuTimeout);

        try
        {
            var buffer = new char[1024];
            while (!timeout.Token.IsCancellationRequested)
            {
                var read = await process.StandardOutput
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                stdout.Append(buffer, 0, read);
                var text = stdout.ToString();

                while (stdinSent < stdinLines.Count)
                {
                    if (ZapretServiceCommands.CanSkipStdinLine(text, stdinSent, operation))
                    {
                        stdinLog.Add($"(skip #{stdinSent + 1}: {DisplayStdin(stdinLines[stdinSent])})");
                        stdinSent++;
                        continue;
                    }

                    if (!ZapretServiceCommands.ShouldSendStdin(text, stdinSent, stdinLines, operation))
                        break;

                    var line = ZapretServiceCommands.ResolveStdinLine(
                        text,
                        stdinSent,
                        stdinLines[stdinSent],
                        operation);
                    await WriteStdinLineAsync(process, line, timeout.Token).ConfigureAwait(false);
                    stdinLog.Add($"#{stdinSent + 1}: {DisplayStdin(line)}");
                    stdinSent++;
                }

                if (!completed && ZapretServiceCommands.IsActionComplete(text, operation))
                {
                    completed = true;
                    await DrainOutputAsync(process, stdout, buffer, timeout.Token).ConfigureAwait(false);
                    break;
                }

                if (process.HasExited)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelledByUser = true;
        }
        catch (OperationCanceledException)
        {
            completed = completed || ZapretServiceCommands.IsActionComplete(stdout.ToString(), operation);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch
            {
            }

            if (cancelledByUser)
                KillProcessTree(process);
        }

        try
        {
            await stderrTask.ConfigureAwait(false);
        }
        catch (Exception) when (cancelledByUser || cancellationToken.IsCancellationRequested)
        {
            cancelledByUser = true;
            KillProcessTree(process);
        }

        if (cancelledByUser)
            throw new OperationCanceledException(cancellationToken);

        await WaitForExitAsync(process, DrainAfterComplete).ConfigureAwait(false);

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var exitCode = ZapretServiceCommands.ResolveExitCode(
            ReadExitCode(process),
            stdoutText,
            stderrText,
            operation);

        await WriteLogAsync(operation, exitCode, stdinLog, stdoutText, stderrText, cancellationToken)
            .ConfigureAwait(false);

        return new ZapretBatResult(exitCode, stdoutText, stderrText);
    }

    private static async Task DrainOutputAsync(
        Process process,
        StringBuilder stdout,
        char[] buffer,
        CancellationToken cancellationToken)
    {
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(DrainAfterComplete);

        try
        {
            while (!drain.Token.IsCancellationRequested && !process.HasExited)
            {
                var read = await process.StandardOutput
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), drain.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                stdout.Append(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan maxWait)
    {
        if (process.HasExited)
            return;

        using var cts = new CancellationTokenSource(maxWait);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task WriteStdinLineAsync(
        Process process,
        string line,
        CancellationToken cancellationToken)
    {
        if (line.Length == 0)
            await process.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
        else
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        StringBuilder target,
        CancellationToken cancellationToken)
    {
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        target.Append(text);
    }

    private static int ReadExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string DisplayStdin(string line) =>
        line.Length == 0 ? "(Enter)" : line;

    private static async Task WriteLogAsync(
        ServiceMenuOperation operation,
        int exitCode,
        IReadOnlyList<string> stdinLog,
        string stdout,
        string stderr,
        CancellationToken cancellationToken)
    {
        try
        {
            var logDir = Path.GetDirectoryName(LogFilePath)!;
            Directory.CreateDirectory(logDir);

            var log = new StringBuilder()
                .AppendLine($"--- {DateTime.Now:O} {operation} exit={exitCode} ---")
                .AppendLine("--- stdin sent ---")
                .AppendLine(stdinLog.Count > 0 ? string.Join(Environment.NewLine, stdinLog) : "(none)")
                .AppendLine("--- stdout ---")
                .AppendLine(stdout)
                .AppendLine("--- stderr ---")
                .AppendLine(stderr)
                .AppendLine();

            await File.AppendAllTextAsync(LogFilePath, log.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void EnsureValidFolder()
    {
        if (!ValidateFolderOrWarn())
            throw new InvalidOperationException("Invalid zapret folder.");
    }

    private ProcessStartInfo CreateStartInfo(string scriptPath, string? arguments)
    {
        var argSuffix = string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments.Trim();
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c chcp 65001>nul & call \"{scriptPath}\"{argSuffix}",
            WorkingDirectory = _zapretFolder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }
}
