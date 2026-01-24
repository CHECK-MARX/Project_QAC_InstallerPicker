using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class ModuleSupportInfo
{
    public bool IsSupported { get; init; }
    public string? ModuleVersion { get; init; }
}

public class HelixVersionData
{
    public HelixVersionData(string version, Dictionary<string, ModuleSupportInfo> moduleSupport)
    {
        Version = version;
        ModuleSupport = moduleSupport;
    }

    public string Version { get; }
    public Dictionary<string, ModuleSupportInfo> ModuleSupport { get; }
}

public class CompatibilityData
{
    public CompatibilityData(List<string> moduleCodes, List<HelixVersionData> versions)
    {
        ModuleCodes = moduleCodes;
        Versions = versions;
    }

    public List<string> ModuleCodes { get; }
    public List<HelixVersionData> Versions { get; }
}
