using System;
using System.Collections.Generic;
using System.IO;

namespace QACInstallerPicker.App.Helpers;

public static class OutputDeletionGuard
{
    public static bool TryValidate(string targetPath, string basePath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(basePath))
        {
            reason = "削除先または出力ベースが設定されていません。";
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        if (string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, root, StringComparison.OrdinalIgnoreCase) ||
            !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            reason = "出力ベースの直下にある対象だけ削除できます。";
            return false;
        }

        if (!Directory.Exists(root) || (!Directory.Exists(target) && !File.Exists(target)))
        {
            reason = "削除対象または出力ベースが見つかりません。";
            return false;
        }

        // Refuse links in ancestors and descendants, including directory junctions.
        for (string? path = target; path != null; path = Path.GetDirectoryName(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "リンクや再解析ポイントを含む対象は削除できません。";
                return false;
            }
            if (string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        if (Directory.Exists(target))
        {
            var pending = new Stack<string>();
            pending.Push(target);
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        reason = "リンクや再解析ポイントを含む対象は削除できません。";
                        return false;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }
        }
        return true;
    }
}
