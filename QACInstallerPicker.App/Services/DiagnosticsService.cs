using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace QACInstallerPicker.App.Services;

public static class DiagnosticsService
{
    public static string BuildReport(string appVersion, string? buildInfoPath, string? logPath)
    {
        var lines = new List<string>
        {
            "QAC インストーラ選定ツール 診断情報",
            $"作成日時 UTC: {DateTime.UtcNow:O}",
            $"アプリ版: {appVersion}",
            $"OS: {RuntimeInformation.OSDescription}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            string.Empty,
            "ビルド情報"
        };
        lines.AddRange(ReadTextOrPlaceholder(buildInfoPath, "ビルド情報ファイルなし"));
        lines.Add(string.Empty);
        lines.Add("ログ末尾 200 行");
        lines.AddRange(ReadTailOrPlaceholder(logPath, 200));
        return string.Join(Environment.NewLine, lines);
    }

    public static void Save(string path, string report) =>
        File.WriteAllText(path, report, new UTF8Encoding(true));

    private static IEnumerable<string> ReadTextOrPlaceholder(string? path, string placeholder)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new[] { placeholder };
        }
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new[] { $"読み込み不可: {ex.Message}" };
        }
    }

    private static IEnumerable<string> ReadTailOrPlaceholder(string? path, int count)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new[] { "ログファイルなし" };
        }
        try
        {
            var tail = new Queue<string>();
            foreach (var line in File.ReadLines(path))
            {
                tail.Enqueue(line);
                if (tail.Count > count)
                {
                    tail.Dequeue();
                }
            }
            return tail.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new[] { $"読み込み不可: {ex.Message}" };
        }
    }
}
