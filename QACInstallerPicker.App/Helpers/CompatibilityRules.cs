using System;
using System.Collections.Generic;

namespace QACInstallerPicker.App.Helpers;

public static class CompatibilityRules
{
    public static readonly IReadOnlyDictionary<string, string> MinHelixVersionByModuleCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // MVP: "○○以降" 注記の最小対応バージョンをここに定数で持つ
            ["VALIDATE"] = "2024.1"
        };

    public static bool TryCheckMinVersion(string helixVersion, string moduleCode, out string? reason)
    {
        reason = null;
        if (MinHelixVersionByModuleCode.TryGetValue(moduleCode, out var minVersion))
        {
            if (!VersionUtil.IsAtLeast(helixVersion, minVersion))
            {
                reason = $"{minVersion}以降";
                return false;
            }
        }

        return true;
    }
}
