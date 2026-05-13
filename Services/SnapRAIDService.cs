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
        // Validate snapraid.exe exists before starting
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
            // Convert backslashes to forward slashes for CLI argument (snapraid on Windows handles /)
            var confPath = _settings.ConfFilePath.Replace('\\', '/');
            startInfo.Arguments += $" -c \"{confPath}\"";
        }

        _currentProcess = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        try
        {
            _currentProcess.Start();
            OutputReceived?.Invoke(this, $"Running: {command} {arguments}\n");

            await Task.WhenAll(
                ReadOutputAsync(_currentProcess.StandardOutput, outputBuilder, cancellationToken),
                ReadOutputAsync(_currentProcess.StandardError, errorBuilder, cancellationToken)
            ).ConfigureAwait(false);

            int exitCode = _currentProcess.ExitCode;
            ExitCodeReceived?.Invoke(this, exitCode);

            var fullOutput = outputBuilder.ToString();
            OutputReceived?.Invoke(this, $"\n[Exit code: {exitCode}]\n");

            if (!string.IsNullOrWhiteSpace(errorBuilder.ToString()))
            {
                ErrorOccurred?.Invoke(this, errorBuilder.ToString());
            }
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
            try { _currentProcess.WaitForInputIdle(1000); } catch { /* ignore */ }
            _currentProcess.Close();
            _currentProcess = null;
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

    public async Task<string> RunFixAsync(CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync(_settings.SnapRaidExePath, "fix", cancellationToken).ConfigureAwait(false);
    }
}
