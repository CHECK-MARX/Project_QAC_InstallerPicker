using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using QACInstallerPicker.App;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;
using QACInstallerPicker.App.ViewModels;

var tempRoot = Path.Combine(Path.GetTempPath(), "QACPickerTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    await CheckSettingsRecovery();
    CheckDeletionBoundary();
    CheckCustomTabFilter();
    CheckTransferPreflight();
    CheckCustomPathRepair();
    CheckSelectionManifest();
    CheckDiagnosticsExport();
    await CheckTransferRecovery();
    CheckCustomTabPopupUi();
    Console.WriteLine("PASS settings, deletion boundary, custom search and path repair, manifest and diff, diagnostics, preflight, transfer recovery, popup UI");
}
finally
{
    SqliteConnection.ClearAllPools();
    for (var attempt = 0; attempt < 20; attempt++)
    {
        try
        {
            Directory.Delete(tempRoot, recursive: true);
            break;
        }
        catch (IOException) when (attempt < 19)
        {
            await Task.Delay(100);
        }
    }
}

void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

async Task WaitForStatus(TransferItemViewModel item, TransferStatus expected)
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(15))
    {
        if (item.Status == expected) return;
        if (item.Status == TransferStatus.Failed && expected != TransferStatus.Failed)
            throw new Exception($"Transfer failed: {item.ErrorReason}");
        await Task.Delay(25);
    }
    throw new TimeoutException($"Expected {expected}, got {item.Status}: {item.ErrorReason}");
}

async Task CheckSettingsRecovery()
{
    var path = Path.Combine(tempRoot, "settings.json");
    var service = new SettingsService(path);
    service.Save(new SettingsModel { OutputBaseFolder = "first" });
    service.Save(new SettingsModel { OutputBaseFolder = "second" });
    Require(service.Load().OutputBaseFolder == "second", "Latest settings were not loaded");
    await File.WriteAllTextAsync(path, "{broken json");
    Require(service.Load().OutputBaseFolder == "first", "Backup settings were not recovered");
    Require(Directory.GetFiles(tempRoot, "settings.json.damaged-*").Length == 1,
        "Damaged settings were not preserved");
}

void CheckDeletionBoundary()
{
    var root = Path.Combine(tempRoot, "output");
    var sibling = Path.Combine(tempRoot, "output-other");
    var child = Path.Combine(root, "customer");
    Directory.CreateDirectory(child);
    Directory.CreateDirectory(sibling);
    Require(OutputDeletionGuard.TryValidate(child, root, out _), "Valid child was rejected");
    Require(!OutputDeletionGuard.TryValidate(root, root, out _), "Base directory was accepted");
    Require(!OutputDeletionGuard.TryValidate(sibling, root, out _), "Sibling directory was accepted");
    Require(!OutputDeletionGuard.TryValidate(Path.Combine(root, "..", "output-other"), root, out _),
        "Traversal outside the base was accepted");
}

void CheckCustomTabFilter()
{
    var tab = new CustomTabViewModel("test");
    var first = Path.Combine(tempRoot, "release[1].zip");
    var second = Path.Combine(tempRoot, "release2.zip");
    File.WriteAllText(first, "first");
    File.WriteAllText(second, "second");
    Require(tab.AddFile(first) && tab.AddFile(second), "Test files were not added");
    tab.FilterText = "[1]";
    tab.ApplyFilter();
    Require(tab.RowsView.Count == 1, "Literal bracket search did not filter to one row");
    tab.FilterText = string.Empty;
    tab.ApplyFilter();
    Require(tab.RowsView.Count == 2, "Clearing search did not restore both rows");
}

void CheckTransferPreflight()
{
    var first = Path.Combine(tempRoot, "preflight-first.bin");
    var second = Path.Combine(tempRoot, "preflight-second.bin");
    var destination = Path.Combine(tempRoot, "preflight-output.bin");
    File.WriteAllBytes(first, new byte[10]);
    File.WriteAllBytes(second, new byte[20]);

    var valid = TransferPreflightService.Check(new[]
    {
        new TransferPreflightTarget(first, destination)
    }, new[] { second });
    Require(valid.CanStart && valid.SourceBytes == 30, "Valid preflight failed or size was wrong");
    Require(!TransferPreflightService.Check(new[]
    {
        new TransferPreflightTarget(first, destination)
    }, availableBytes: 1).CanStart, "Insufficient free space was not rejected");

    var duplicate = TransferPreflightService.Check(new[]
    {
        new TransferPreflightTarget(first, destination),
        new TransferPreflightTarget(second, destination)
    });
    Require(!duplicate.CanStart && duplicate.Errors.Any(error => error.Contains("重複")),
        "Duplicate destination was not rejected");

    var missing = TransferPreflightService.Check(new[]
    {
        new TransferPreflightTarget(first, destination)
    }, new[] { Path.Combine(tempRoot, "vanished.bin") });
    Require(!missing.CanStart && missing.Errors.Any(error => error.Contains("見つかりません")),
        "Missing ZIP source was not rejected");

    var overwriteSource = TransferPreflightService.Check(new[]
    {
        new TransferPreflightTarget(first, second),
        new TransferPreflightTarget(second, destination)
    });
    Require(!overwriteSource.CanStart, "A source would be overwritten by another target");
}

void CheckCustomPathRepair()
{
    var oldRoot = Path.Combine(tempRoot, "old-share");
    var newRoot = Path.Combine(tempRoot, "new-share");
    var oldFile = Path.Combine(oldRoot, "docs", "manual.pdf");
    var newFile = Path.Combine(newRoot, "docs", "manual.pdf");
    Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
    File.WriteAllText(newFile, "replacement");
    var outside = Path.Combine(tempRoot, "old-share-other", "manual.pdf");
    var preview = CustomPathRepairService.Preview(oldRoot, newRoot,
        new[] { oldFile, outside, Path.Combine(oldRoot, "missing.pdf") });
    Require(preview.Replacements.Count == 1 && preview.Replacements[oldFile] == newFile,
        "Existing file was not mapped to the new root");
    Require(preview.MissingTargets.Count == 1 && !preview.Replacements.ContainsKey(outside),
        "Missing target or sibling boundary was not handled");

    var tab = new CustomTabViewModel("repair");
    Require(tab.AddFile(oldFile), "Old custom path was not added");
    Require(tab.CanReplaceSourcePaths(preview.Replacements), "Valid path repair was rejected");
    Require(tab.ReplaceSourcePaths(preview.Replacements) == 1 && tab.HasFile(newFile),
        "Custom tab path was not repaired");
}

void CheckSelectionManifest()
{
    var source = Path.Combine(tempRoot, "manifest-source.bin");
    File.WriteAllBytes(source, new byte[12]);
    var previous = SelectionManifestService.Create(new[]
    {
        SelectionManifestService.CreateEntry("ファイル", "A", source, "old-output"),
        SelectionManifestService.CreateEntry("ファイル", "B", source, "old-output-2")
    });
    var current = SelectionManifestService.Create(new[]
    {
        SelectionManifestService.CreateEntry("ファイル", "A", source, "new-output"),
        SelectionManifestService.CreateEntry("ファイル", "C", source, "new-output-3")
    });
    var path = Path.Combine(tempRoot, "manifest.json");
    SelectionManifestService.Save(path, previous);
    Require(SelectionManifestService.Load(path).Items.Count == 2, "Manifest did not round-trip");
    var diff = SelectionManifestService.Compare(previous, current);
    Require(diff.Count == 2 && diff.Any(item => item.Key == "B" && item.Change == "削除") &&
            diff.Any(item => item.Key == "C" && item.Change == "追加"),
        "Manifest addition/deletion was wrong");
    var csvPath = Path.Combine(tempRoot, "manifest-diff.csv");
    SelectionManifestService.SaveDiffCsv(csvPath, diff);
    Require(File.ReadAllLines(csvPath).Length == 3, "Diff CSV was not written");
}

void CheckDiagnosticsExport()
{
    var buildInfo = Path.Combine(tempRoot, "BUILD_INFO.txt");
    var logPath = Path.Combine(tempRoot, "diagnostics.log");
    File.WriteAllText(buildInfo, "commit=test-commit");
    File.WriteAllLines(logPath, Enumerable.Range(1, 250).Select(index => $"log-{index}"));
    var report = DiagnosticsService.BuildReport("test-version", buildInfo, logPath);
    Require(report.Contains("commit=test-commit") && report.Contains("log-250") &&
            !report.Contains("log-1" + Environment.NewLine),
        "Diagnostics report did not include build info and bounded log tail");
    var output = Path.Combine(tempRoot, "diagnostics.txt");
    DiagnosticsService.Save(output, report);
    Require(File.Exists(output), "Diagnostics report was not saved");
}

void CheckCustomTabPopupUi()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        System.Windows.Application? app = null;
        MainWindow? window = null;
        try
        {
            app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var vm = new MainViewModel(
                new SettingsService(Path.Combine(tempRoot, "ui-settings.json")),
                new DatabaseService(Path.Combine(tempRoot, "ui.db")));
            var source = Path.Combine(tempRoot, "popup-source.txt");
            File.WriteAllText(source, "popup");
            var tab = new CustomTabViewModel("UI test");
            tab.AddFile(source);
            vm.CustomTabs.Add(tab);
            vm.SelectedCustomTab = tab;
            vm.SavedSelectionSetName = "検証セット";
            vm.SaveNamedSelectionSetCommand.Execute(null);
            Require(vm.SavedSelectionSetEntries.Count == 1 &&
                    new SettingsService(Path.Combine(tempRoot, "ui-settings.json")).Load().SavedSelectionSets.Count == 1,
                "Named selection set was not persisted");
            tab.ClearSelection();
            vm.ApplyNamedSelectionSetCommand.Execute(null);
            Require(tab.GetSelectedFiles().Count == 1, "Named selection set was not restored");
            window = new MainWindow(vm, initializeOnLoaded: false)
            {
                ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None,
                Left = -10000, Top = -10000
            };
            window.Show();
            window.UpdateLayout();
            var grid = FindGrid(window, tab);
            Require(grid != null, "Custom tab grid was not created");
            grid!.SelectedItem = grid.Items[0];
            grid.CurrentCell = new DataGridCellInfo(grid.Items[0],
                grid.Columns.First(column => column.Header?.ToString() == CustomTabViewModel.FolderColumnName));
            grid.Focus();
            Require(grid.BeginEdit(), "Custom tab grid did not enter edit mode");

            var open = typeof(MainWindow).GetMethod("OpenCustomTabPopupWindow",
                BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(DataGrid) }, null);
            Require(open != null, "Popup open handler was not found");
            open!.Invoke(window, new object?[] { grid });
            var field = typeof(MainWindow).GetField("_customTabPopupWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            var popup = field?.GetValue(window) as Window;
            Require(popup?.IsVisible == true, "Custom tab popup did not open");
            popup!.Close();
            open.Invoke(window, new object?[] { grid });
            popup = field?.GetValue(window) as Window;
            Require(popup?.IsVisible == true, "Custom tab popup did not reopen");
            popup!.Close();

            var scanRoot = Path.Combine(tempRoot, "ui-scan");
            Directory.CreateDirectory(scanRoot);
            for (var index = 0; index < 500; index++)
            {
                File.WriteAllText(Path.Combine(scanRoot, $"item-{index:0000}.txt"), "scan");
            }
            tab.NewDirectoryPath = scanRoot;
            var scanTask = vm.ScanCustomDirectoryCommand.ExecuteAsync(null);
            var frame = new DispatcherFrame();
            var timedOut = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            timer.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
            timer.Start();
            scanTask.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Require(!timedOut, "Custom directory scan timed out");
            scanTask.GetAwaiter().GetResult();
            Require(tab.GetAllSourcePaths().Count == 501 && !vm.IsCustomDirectoryScanInProgress,
                "Custom directory scan did not finish adding files");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            window?.Close();
            app?.Shutdown();
        }
    }) { IsBackground = true };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Require(thread.Join(TimeSpan.FromSeconds(20)), "Popup UI test timed out");
    if (failure != null) throw new Exception("Popup UI test failed", failure);
}

DataGrid? FindGrid(DependencyObject parent, CustomTabViewModel tab)
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
    {
        var child = VisualTreeHelper.GetChild(parent, index);
        if (child is DataGrid grid && ReferenceEquals(grid.DataContext, tab)) return grid;
        var nested = FindGrid(child, tab);
        if (nested != null) return nested;
    }
    return null;
}

async Task CheckTransferRecovery()
{
    var source = Path.Combine(tempRoot, "source.bin");
    var outputDir = Path.Combine(tempRoot, "transfers");
    Directory.CreateDirectory(outputDir);
    var payload = RandomNumberGenerator.GetBytes(1024 * 128);
    await File.WriteAllBytesAsync(source, payload);
    var db = new DatabaseService(Path.Combine(tempRoot, "transfers.db"));
    await db.InitializeAsync();
    var manager = new TransferManager(db, new HashService(db), new CopyService(), 1);

    var dest = Path.Combine(outputDir, "retry.bin");
    await File.WriteAllBytesAsync(dest + ".part", new byte[payload.Length]);
    var failed = new TransferItemRecord
    {
        AssetSourcePath = source, DestPath = dest, Size = payload.Length,
        SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(source),
        Status = TransferStatus.Failed, ErrorReason = "HashMismatch"
    };
    await db.UpsertHashCacheAsync(new HashCacheEntry
    {
        SourcePath = source, Size = payload.Length,
        LastWriteTimeUtc = failed.SourceLastWriteTimeUtc,
        Sha256 = new string('0', 64), CalculatedAtUtc = DateTime.UtcNow
    });
    failed.Id = await db.InsertTransferItemAsync(failed);
    var retry = new TransferItemViewModel(failed, manager);
    await manager.RetryAsync(retry);
    await WaitForStatus(retry, TransferStatus.Completed);
    Require((await File.ReadAllBytesAsync(dest)).SequenceEqual(payload), "Corrupt .part was reused");
    Require(failed.SourceHashSha256 == Convert.ToHexString(SHA256.HashData(payload)),
        "Stale source hash cache was reused");

    var replaceDest = Path.Combine(outputDir, "replace.bin");
    await File.WriteAllTextAsync(replaceDest, "old data");
    var replace = new TransferItemRecord { AssetSourcePath = source, DestPath = replaceDest };
    replace.Id = await db.InsertTransferItemAsync(replace);
    var replacement = new TransferItemViewModel(replace, manager);
    await manager.StartAsync(replacement);
    await WaitForStatus(replacement, TransferStatus.Completed);
    Require((await File.ReadAllBytesAsync(replaceDest)).SequenceEqual(payload), "Existing destination was not replaced");
    Require(Directory.GetFiles(outputDir, "*.previous-*").Length == 0, "Backup was not removed");

    var lockedDest = Path.Combine(outputDir, "locked.bin");
    await File.WriteAllTextAsync(lockedDest, "keep old data");
    var locked = new TransferItemRecord { AssetSourcePath = source, DestPath = lockedDest };
    locked.Id = await db.InsertTransferItemAsync(locked);
    using (var lockStream = new FileStream(lockedDest, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var lockedItem = new TransferItemViewModel(locked, manager);
        await manager.StartAsync(lockedItem);
        await WaitForStatus(lockedItem, TransferStatus.Failed);
        Require(lockedItem.ErrorReason.StartsWith("FinalizeFailed:", StringComparison.Ordinal),
            "Locked destination did not fail during finalization");
    }
    Require(await File.ReadAllTextAsync(lockedDest) == "keep old data",
        "Existing destination was lost after replacement failure");

    var missing = new TransferItemRecord
    {
        AssetSourcePath = Path.Combine(tempRoot, "missing.bin"),
        DestPath = Path.Combine(outputDir, "missing.bin")
    };
    missing.Id = await db.InsertTransferItemAsync(missing);
    var missingItem = new TransferItemViewModel(missing, manager);
    await manager.StartAsync(missingItem);
    await WaitForStatus(missingItem, TransferStatus.Failed);
    await Task.Delay(100);
    await manager.RetryAsync(missingItem);
    await WaitForStatus(missingItem, TransferStatus.Failed);
    Require(missingItem.ErrorReason == "SourceNotFound", "Immediate failure could not be retried");
}
