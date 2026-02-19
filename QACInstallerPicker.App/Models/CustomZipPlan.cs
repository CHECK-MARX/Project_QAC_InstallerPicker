using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public sealed record CustomZipPlan(
    string TabName,
    string ArchiveBaseName,
    IReadOnlyList<CustomZipPlanItem> Items);

public sealed record CustomZipPlanItem(
    string SourcePath,
    string FolderName,
    string FileName,
    bool IncludeFolderInArchive);
