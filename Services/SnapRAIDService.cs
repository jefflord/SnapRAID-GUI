namespace SnapRAIDGUI.Services;

using System.IO;
using System.Diagnostics;
using SnapRAIDGUI.Models;

public class SnapRAIDService
{
    private Process? _currentProcess;
    private CancellationTokenSource? _cancelSource;
    private SnapRAIDSettings _settings = new();

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? ExitCodeReceived;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _currentProcess is not null && !_currentProcess.HasExited;

    // Update settings from the ViewModel after config loads
    public void SetSettings(SnapRAIDSettings settings)
    {
        if (settings != null)
            _settings = settings;
    }

    public void Cancel()
    {
        if (_currentProcess is not null && !_currentProcess.HasExited)
        {
            try { _currentProcess.Kill(true); } catch { /* ignore */ }
            _cancelSource?.Cancel();
        }
    }

    private async Task<string> RunCommandAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(command))
        {
            var msg = $"snapraid.exe not found at: {command}\n\nPlease set the correct path in Settings.";
            OutputReceived?.Invoke(this, msg);
            ErrorOccurred?.Invoke(this, msg);
            return msg;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(_settings.ConfFilePath))
        {
            var confPath = _settings.ConfFilePath.Replace('\\', '/');
            startInfo.Arguments += $" -c \"{confPath}\"";
        }

        // Use a local process variable so parallel calls don't race on _currentProcess
        var process = new Process { StartInfo = startInfo };
        _currentProcess = process; // track last started for Cancel() — best effort

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder  = new System.Text.StringBuilder();

        try
        {
            process.Start();
            OutputReceived?.Invoke(this, $"Running: {command} {arguments}\n");

            await Task.WhenAll(
                ReadOutputAsync(process.StandardOutput, outputBuilder, cancellationToken),
                ReadOutputAsync(process.StandardError,  errorBuilder,  cancellationToken)
            ).ConfigureAwait(false);

            int exitCode = process.ExitCode;
            ExitCodeReceived?.Invoke(this, exitCode);
            OutputReceived?.Invoke(this, $"\n[Exit code: {exitCode}]\n");

            if (!string.IsNullOrWhiteSpace(errorBuilder.ToString()))
                ErrorOccurred?.Invoke(this, errorBuilder.ToString());
        }
        catch (Exception ex)
        {
            var errorMsg = $"[ERROR] Failed to run command:\n{ex.Message}\n\nCheck that snapraid.exe path is correct.";
            OutputReceived?.Invoke(this, errorMsg);
            ErrorOccurred?.Invoke(this, errorMsg);
            return errorMsg;
        }
        finally
        {
            try { process.WaitForInputIdle(1000); } catch { }
            process.Close();
            if (ReferenceEquals(_currentProcess, process)) _currentProcess = null;
        }

        return outputBuilder.ToString();
    }

    private async Task ReadOutputAsync(System.IO.StreamReader reader, System.Text.StringBuilder builder, CancellationToken ct)
    {
        try
        {
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                builder.AppendLine(line);
                OutputReceived?.Invoke(this, line + "\n");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel
        }
    }

    public async Task<string> RunStatusAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "status", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunDiffAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "diff", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunSyncAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "sync", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunScrubAsync(bool full, CancellationToken cancellationToken = default)
    {
        var plan = full ? "-p full" : "-p new";
        return await RunCommandAsync(_settings.SnapRaidExePath, $"scrub {plan}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunSmartAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "smart", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs snapraid with the given arguments, calling <paramref name="onLine"/> for every line
    /// of stdout as it arrives — no buffering. stderr is still collected and returned.
    /// This is the right approach for "snapraid list" which can emit 500k+ lines.
    /// </summary>
    public async Task StreamCommandAsync(
        string arguments,
        Action<string> onLine,
        CancellationToken cancellationToken = default)
    {
        var exe = _settings.SnapRaidExePath;
        if (!File.Exists(exe))
        {
            onLine($"[ERROR] snapraid.exe not found at: {exe}");
            return;
        }

        var args = arguments;
        if (!string.IsNullOrWhiteSpace(_settings.ConfFilePath))
            args += $" -c \"{_settings.ConfFilePath.Replace('\\', '/')}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = startInfo };
        _currentProcess = process;

        try
        {
            process.Start();

            // Read stderr on a background task so it doesn't block stdout
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            // Stream stdout line by line — call onLine immediately for each.
            // ConfigureAwait(false) keeps execution off the UI thread so the
            // caller's Dispatcher.Invoke/BeginInvoke calls inside onLine work.
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                       .ConfigureAwait(false)) != null)
            {
                onLine(line);
            }

            await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* caller cancelled — normal */ }
        catch (Exception ex) { onLine($"[ERROR] {ex.Message}"); }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            process.Close();
            if (ReferenceEquals(_currentProcess, process)) _currentProcess = null;
        }
    }

    public async Task<string> RunListAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "list", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunCheckFileAsync(string snapraidRelativePath, CancellationToken cancellationToken = default)
    {
        // snapraid requires a leading slash: check -f "/path/to/file"
        var path = snapraidRelativePath.StartsWith('/') ? snapraidRelativePath : "/" + snapraidRelativePath;
        return await RunCommandAsync(_settings.SnapRaidExePath, $"check -f \"{path}\"", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunFixFileAsync(string snapraidRelativePath, CancellationToken cancellationToken = default)
    {
        // snapraid requires a leading slash: fix -f "/path/to/file"
        var path = snapraidRelativePath.StartsWith('/') ? snapraidRelativePath : "/" + snapraidRelativePath;
        return await RunCommandAsync(_settings.SnapRaidExePath, $"fix -f \"{path}\"", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RunFixAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "fix", cancellationToken).ConfigureAwait(false);
    }
}
