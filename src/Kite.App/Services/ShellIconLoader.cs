using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Kite.App.Interop.ShellIconInterop;

namespace Kite.App.Services;

/// <summary>
/// loads icons for files, shortcuts and store apps on one background thread
/// and remembers them. if an icon cannot be loaded, null is returned and a
/// simple symbol is shown instead.
/// </summary>
internal sealed class ShellIconLoader : IDisposable
{
    private const int IconPixels = 48;
    private const int MaxCacheSize = 1500;

    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<(string Path, TaskCompletionSource<ImageSource?> Done)> _queue = new();
    private readonly Thread _worker;

    public ShellIconLoader()
    {
        _worker = new Thread(WorkLoop)
        {
            IsBackground = true,
            Name = "Kite icon loader",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public Task<ImageSource?> GetIconAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _queue.IsAddingCompleted)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        if (_cache.Count > MaxCacheSize)
        {
            _cache.Clear();
        }

        return _cache.GetOrAdd(path, key =>
        {
            var done = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add((key, done));
            }
            catch (InvalidOperationException)
            {
                // the loader is shutting down
                done.TrySetResult(null);
            }

            return done.Task;
        });
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
    }

    private static ImageSource? LoadIcon(string path)
    {
        object? created = null;
        try
        {
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out created);
            if (hr != 0 || created is not IShellItemImageFactory factory)
            {
                return null;
            }

            var size = new SIZE { cx = IconPixels, cy = IconPixels };
            hr = factory.GetImage(size, SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out var bitmapHandle);
            if (hr != 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return ToBitmapSource(bitmapHandle);
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            if (created is not null && Marshal.IsComObject(created))
            {
                Marshal.ReleaseComObject(created);
            }
        }
    }

    /// <summary>copy the pixels out of a windows bitmap, keeping see through parts.</summary>
    private static BitmapSource? ToBitmapSource(IntPtr bitmapHandle)
    {
        if (GetObject(bitmapHandle, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.bmWidth <= 0 || info.bmHeight == 0)
        {
            return null;
        }

        var width = info.bmWidth;
        var height = Math.Abs(info.bmHeight);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,

            // a negative height asks for rows from top to bottom
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(screen, bitmapHandle, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        // old icons may have no see through data at all, so make them solid
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (!hasAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private void WorkLoop()
    {
        foreach (var (path, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                done.TrySetResult(LoadIcon(path));
            }
            catch (Exception ex)
            {
                Log.Error($"Icon failed for {path}", ex);
                done.TrySetResult(null);
            }
        }
    }
}
