using System.IO;
using System.Text.Json;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static SettingsModel Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsModel>(json);
                if (settings != null)
                    return settings;
                LogWriter.Write("[Settings] Deserialization returned null, using defaults");
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("[Settings] Failed to load: " + ex.Message);
        }

        return new SettingsModel();
    }

    public static void Save(SettingsModel settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(AppPaths.SettingsPath, json);
            LogWriter.Write("[Settings] Saved");
        }
        catch (Exception ex)
        {
            LogWriter.Write("[Settings] Failed to save: " + ex.Message);
        }
    }
}
