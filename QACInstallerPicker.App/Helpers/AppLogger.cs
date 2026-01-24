using System;
using System.IO;
using System.Text;

namespace QACInstallerPicker.App.Helpers;

public static class AppLogger
{
    private static readonly object Gate = new();

    public static void LogInfo(string message)
    {
        Write("INFO", message, null);
    }

    public static void LogError(string context, Exception ex)
    {
        Write("ERROR", context, ex);
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTime.UtcNow.ToString("O"));
            builder.Append(" [").Append(level).Append("] ");
            builder.Append(message);
            if (ex != null)
            {
                builder.AppendLine();
                builder.Append(ex);
            }
            builder.AppendLine();

            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
