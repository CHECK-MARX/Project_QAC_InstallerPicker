using System;
using System.Runtime.InteropServices;

namespace QACInstallerPicker.App.Services;

public static class DiskSpaceService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceEx(
        string directoryName, out ulong freeBytesAvailable,
        out ulong totalBytes, out ulong totalFreeBytes);

    public static long? TryGetAvailableBytes(string outputPath)
    {
        try
        {
            var directory = System.IO.Path.GetFullPath(outputPath);
            while (!System.IO.Directory.Exists(directory))
            {
                directory = System.IO.Path.GetDirectoryName(directory) ?? string.Empty;
                if (directory.Length == 0)
                {
                    return null;
                }
            }
            if (!GetDiskFreeSpaceEx(directory, out var free, out _, out _))
            {
                return null;
            }
            return free > long.MaxValue ? long.MaxValue : (long)free;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.IOException)
        {
            return null;
        }
    }
}
