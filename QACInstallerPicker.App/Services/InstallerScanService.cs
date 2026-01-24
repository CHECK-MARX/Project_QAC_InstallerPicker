using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class ScanResult
{
    public List<LogicalItem> Items { get; } = new();
    public List<string> Errors { get; } = new();
}

public class InstallerScanService
{
    private static readonly Regex ModuleRegex = new(
        @"^(?<code>[A-Za-z0-9+]+)[-_](?<version>\d+(?:\.\d+)+(?:[-_]\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidateRegex = new(
        @"^p4-validate-installer[._-](?<version>\d+(?:\.\d+)+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DashboardRegex = new(
        @"^Helix[-_]QAC[-_]Dashboard[-_](?<version>\d{4}\.\d+)(?:(?<suffix>-[A-Za-z])(?=-|$))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HelixQacRegex = new(
        @"^Helix[-_]QAC[-_](?<version>\d+(?:\.\d+)+(?:[-_]\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<ScanResult> ScanAsync(string root, CancellationToken cancellationToken)
    {
        return await Task.Run(() => ScanInternal(root, cancellationToken), cancellationToken);
    }

    private ScanResult ScanInternal(string root, CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        if (!Directory.Exists(root))
        {
            result.Errors.Add($"UNCルートが見つかりません: {root}");
            return result;
        }

        var logicalMap = new Dictionary<string, LogicalItem>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var fileName = Path.GetFileName(file);
                if (!TryGetInstallerOs(extension, fileName, file, out var os, out _))
                {
                    continue;
                }

                var (code, version, classified) = TryParseModule(fileName);
                if (!classified)
                {
                    code = "Unclassified";
                    version = "";
                }

                var asset = new InstallerAsset
                {
                    SourcePath = file,
                    Size = new FileInfo(file).Length,
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(file),
                    Os = os,
                    IsZip = extension == ".zip",
                    Code = code,
                    Version = version
                };

                var logicalKey = $"{code}|{version}|{os}";
                if (!logicalMap.TryGetValue(logicalKey, out var logicalItem))
                {
                    logicalItem = new LogicalItem
                    {
                        Code = code,
                        Version = version,
                        Os = os
                    };
                    logicalMap[logicalKey] = logicalItem;
                }

                logicalItem.Assets.Add(asset);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{file}: {ex.Message}");
            }
        }

        foreach (var item in logicalMap.Values)
        {
            // zip優先
            item.Assets.Sort((a, b) => b.IsZip.CompareTo(a.IsZip));
        }

        result.Items.AddRange(logicalMap.Values.OrderBy(i => i.Code));
        return result;
    }

    private static (string Code, string Version, bool Classified) TryParseModule(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var validateMatch = ValidateRegex.Match(name);
        if (validateMatch.Success)
        {
            return ("VALIDATE", validateMatch.Groups["version"].Value, true);
        }

        var dashboardMatch = DashboardRegex.Match(name);
        if (dashboardMatch.Success)
        {
            var version = dashboardMatch.Groups["version"].Value;
            if (dashboardMatch.Groups["suffix"].Success)
            {
                version += dashboardMatch.Groups["suffix"].Value;
            }

            return ("DASHBOARD", version, true);
        }

        var helixQacMatch = HelixQacRegex.Match(name);
        if (helixQacMatch.Success)
        {
            return ("QAC", helixQacMatch.Groups["version"].Value.Replace('_', '-'), true);
        }

        var match = ModuleRegex.Match(name);
        if (!match.Success)
        {
            return (string.Empty, string.Empty, false);
        }

        return (
            match.Groups["code"].Value,
            match.Groups["version"].Value.Replace('_', '-'),
            true);
    }

    private static bool TryGetInstallerOs(string extension, string fileName, string filePath, out OsType os, out bool isZipCandidate)
    {
        isZipCandidate = false;
        switch (extension)
        {
            case ".exe":
            case ".msi":
            case ".win64":
                os = OsType.Windows;
                return true;
            case ".sh":
            case ".run":
            case ".linux64":
                os = OsType.Linux;
                return true;
            case ".zip":
                os = InspectZip(filePath, out isZipCandidate);
                return isZipCandidate;
        }

        os = GuessOsFromName(Path.GetFileNameWithoutExtension(fileName));
        return os != OsType.Unknown;
    }

    private static OsType InspectZip(string filePath, out bool isZipCandidate)
    {
        isZipCandidate = false;
        var hasLinux = false;
        using var archive = ZipFile.OpenRead(filePath);
        foreach (var entry in archive.Entries)
        {
            var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
            if (ext is ".exe" or ".msi")
            {
                isZipCandidate = true;
                return OsType.Windows;
            }

            if (ext is ".sh" or ".run")
            {
                hasLinux = true;
            }
        }

        if (hasLinux)
        {
            isZipCandidate = true;
            return OsType.Linux;
        }

        return OsType.Unknown;
    }

    private static OsType GuessOsFromName(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith("-win") || lower.EndsWith("_win") || lower.EndsWith(".win") || lower.EndsWith(".win64"))
        {
            return OsType.Windows;
        }

        if (lower.EndsWith("-linux") || lower.EndsWith("_linux") || lower.EndsWith(".linux") || lower.EndsWith(".linux64"))
        {
            return OsType.Linux;
        }

        return OsType.Unknown;
    }
}
