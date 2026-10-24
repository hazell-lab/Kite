using System.IO;
using System.Globalization;

namespace Kite.App.Services;

/// <summary>writes problems to kite.log in the data folder, to help with bug reports.</summary>
internal static class Log
{
    private const long MaxSize = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string folder)
    {
        _path = Path.Combine(folder, "kite.log");
    }

    public static void Error(string message, Exception? ex = null)
    {
        Write("error", ex is null ? message : $"{message}: {ex}");
    }

    public static void Info(string message) => Write("info", message);

    private static void Write(string level, string message)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                // start over when the file gets big
                var file = new FileInfo(_path);
                if (file.Exists && file.Length > MaxSize)
                {
                    file.Delete();
                }

                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(_path, $"{stamp} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // logging must never crash the app
        }
        catch (UnauthorizedAccessException)
        {
            // logging must never crash the app
        }
    }
}
