namespace SnapRAIDGUI.Services;

using System.IO;
using Newtonsoft.Json;
using SnapRAIDGUI.Models;

public class ConfigService
{
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapRAID GUI",
        "settings.json");

    public void EnsureDirectoryExists()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public SnapRAIDSettings LoadSettings()
    {
        if (!File.Exists(_configPath))
            return new SnapRAIDSettings();

        try
        {
            var raw = File.ReadAllText(_configPath);
            if (string.IsNullOrWhiteSpace(raw.Trim()))
                return new SnapRAIDSettings();

            var settings = JsonConvert.DeserializeObject<SnapRAIDSettings>(raw, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            return settings ?? new SnapRAIDSettings();
        }
        catch
        {
            return new SnapRAIDSettings();
        }
    }

    public void SaveSettings(SnapRAIDSettings settings)
    {
        EnsureDirectoryExists();
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(_configPath, json);
    }
}
