using System;
using System.IO;
using System.Reflection;

namespace QACInstallerPicker.App.Helpers;

public static class AppPaths
{
    private const string AppFolderName = "QACInstallerPicker";
    private const string SynonymsResourceName = "QACInstallerPicker.App.Data.synonyms.json";
    private static readonly string DataRoot = InitializeDataRoot();

    public static string AppBase => AppContext.BaseDirectory;
    public static string SettingsPath => EnsureDataFile("Settings.json", "Settings.json");
    public static string SynonymsPath => EnsureDataFile(Path.Combine("Data", "synonyms.json"), Path.Combine("Data", "synonyms.json"), SynonymsResourceName);
    public static string DatabasePath => EnsureDataFile("qacinstaller.db", "qacinstaller.db");
    public static string LogPath => Path.Combine(DataRoot, "qacinstaller.log");

    private static string InitializeDataRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataRoot = Path.Combine(local, AppFolderName);
        try
        {
            Directory.CreateDirectory(dataRoot);
            return dataRoot;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static string EnsureDataFile(string relativePath, string? legacyRelativePath = null, string? embeddedResource = null)
    {
        var path = Path.Combine(DataRoot, relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                // Ignore directory creation failures; the caller will handle missing files.
            }
        }

        if (!File.Exists(path))
        {
            if (!string.IsNullOrWhiteSpace(legacyRelativePath))
            {
                var legacyPath = Path.Combine(AppBase, legacyRelativePath);
                if (File.Exists(legacyPath))
                {
                    try
                    {
                        File.Copy(legacyPath, path, true);
                        return path;
                    }
                    catch
                    {
                        // Ignore copy failures; fallback to embedded resource if available.
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(embeddedResource))
            {
                try
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResource);
                    if (stream != null)
                    {
                        using var file = File.Create(path);
                        stream.CopyTo(file);
                    }
                }
                catch
                {
                    // Ignore embedded resource failures.
                }
            }
        }

        return path;
    }
}
