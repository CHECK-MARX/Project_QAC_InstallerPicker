using System;
using System.Collections.Generic;

namespace QACInstallerPicker.App.Helpers;

public static class ModuleCatalog
{
    public static readonly IReadOnlyDictionary<string, string> ModuleDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HelixQAC"] = "HelixQAC",
            ["QAC"] = "QAC",
            ["QAC++"] = "QAC++",
            ["QACPP"] = "QAC++",
            ["Helix"] = "Helix QAC",
            ["QAF"] = "PRQA Framework",
            ["RCMA"] = "クロスモジュール解析コンポーネント",
            ["NAMECHECK"] = "命名規則チェックコンポーネント",
            ["MTA"] = "マルチスレッドアナライザコンポーネント",
            ["DFA"] = "データフロー解析コンポーネント",
            ["MCM"] = "MISRA C:1998 コンプライアンスモジュール",
            ["M2CM"] = "MISRA C:2004 コンプライアンスモジュール",
            ["M3CM"] = "MISRA C:2012 コンプライアンスモジュール",
            ["MCPP"] = "MISRA C++:2008 コンプライアンスモジュール",
            ["M2CPP"] = "MISRA C++:2023 コンプライアンスモジュール",
            ["CERTCCM"] = "CERT C コンプライアンスモジュール",
            ["CERTCPPCM"] = "CERT C++ コンプライアンスモジュール",
            ["CWECCM"] = "CWE (C言語) コンプライアンスモジュール",
            ["CWECPPCM"] = "CWE (C++言語) コンプライアンスモジュール",
            ["ASCM"] = "AUTOSAR コンプライアンスモジュール",
            ["SECCCM"] = "Cセキュアコンプライアンスモジュール",
            ["VALIDATE"] = "Validate",
            ["DASHBOARD"] = "Helix QAC Dashboard"
        };

    public static string GetDescription(string code)
    {
        if (ModuleDescriptions.TryGetValue(code, out var name))
        {
            return name;
        }

        return code;
    }
}
