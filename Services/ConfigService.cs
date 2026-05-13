namespace SnapRAIDGUI.Services;

using System.IO;
using System.Text.Json;
using SnapRAIDGUI.Models;

public class ConfigService
{
    private readonly string _configPath;

    public ConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configPath = Path.Combine(appData, "SnapRAID GUI", "settings.json");
    }

    public SnapRAIDSettings LoadSettings()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                
                // Manual JSON parsing as fallback to ensure we can read the file
                var settings = new SnapRAIDSettings();
                
                var snapRaidMatch = System.Text.RegularExpressions.Regex.Match(json, @"""SnapRaidExePath""\s*:\s*""([^""]+)""");
                if (snapRaidMatch.Success) settings.SnapRaidExePath = snapRaidMatch.Groups[1].Value;
                
                var confMatch = System.Text.RegularExpressions.Regex.Match(json, @"""ConfFilePath""\s*:\s*""([^""]+)""");
                if (confMatch.Success) settings.ConfFilePath = confMatch.Groups[1].Value;
                
                var thresholdMatch = System.Text.RegularExpressions.Regex.Match(json, @"""DeletionWarningThreshold""\s*:\s*(\d+)");
                if (thresholdMatch.Success && int.TryParse(thresholdMatch.Groups[1].Value, out var t)) settings.DeletionWarningThreshold = t;
                
                var autoRefreshMatch = System.Text.RegularExpressions.Regex.Match(json, @"""AutoRefreshStatus""\s*:\s*(true|false)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (autoRefreshMatch.Success) settings.AutoRefreshStatus = bool.Parse(autoRefreshMatch.Groups[1].Value.ToLower());
                
                var intervalMatch = System.Text.RegularExpressions.Regex.Match(json, @"""RefreshIntervalSeconds""\s*:\s*(\d+)");
                if (intervalMatch.Success && int.TryParse(intervalMatch.Groups[1].Value, out var i)) settings.RefreshIntervalSeconds = i;
                
                var logDirMatch = System.Text.RegularExpressions.Regex.Match(json, @"""LogDirectory""\s*:\s*""([^""]+)""");
                if (logDirMatch.Success) settings.LogDirectory = logDirMatch.Groups[1].Value;
                
                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConfigService.LoadSettings error: {ex.Message}");
            }
        }
        return new SnapRAIDSettings();
    }

    public void SaveSettings(SnapRAIDSettings settings)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        
        // Use regex-based save to match our manual parse approach
        var json = $"{{\n";
        json += $"  \"SnapRaidExePath\": \"{settings.SnapRaidExePath}\",\n";
        json += $"  \"ConfFilePath\": \"{settings.ConfFilePath}\",\n";
        json += $"  \"DeletionWarningThreshold\": {settings.DeletionWarningThreshold},\n";
        json += $"  \"AutoRefreshStatus\": {settings.AutoRefreshStatus.ToString().ToLower()},\n";
        json += $"  \"RefreshIntervalSeconds\": {settings.RefreshIntervalSeconds},\n";
        json += $"  \"LogDirectory\": \"{settings.LogDirectory}\"\n";
        json += $"}}";
        
        File.WriteAllText(_configPath, json);
    }
}
