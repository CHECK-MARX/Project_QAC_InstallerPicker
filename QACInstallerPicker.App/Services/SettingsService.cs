using System;
using System.IO;
using System.Text.Json;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class SettingsService
{
    public SettingsModel Load()
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            var defaults = new SettingsModel();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(AppPaths.SettingsPath);
        var settings = JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
        if (settings.MaxConcurrentTransfers <= 0)
        {
            settings.MaxConcurrentTransfers = 2;
        }

        return settings;
    }

    public void Save(SettingsModel settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}
