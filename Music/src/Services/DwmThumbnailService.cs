using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Music.Services;

/// <summary>
/// Provides a static album-art image for the window's taskbar hover
/// thumbnail via DWM. Windows 11 blocks the older ITaskbarList3 thumbnail
/// clip/toolbar APIs, but DWM iconic thumbnails remain available.
/// </summary>
public sealed class DwmThumbnailService
{
    private const int DwmwaHasIconicBitmap = 18;

    private readonly IntPtr _windowHandle;
    private bool _attributeSet;
    private bool _disposed;

    public DwmThumbnailService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public async Task SetArtworkAsync(byte[]? artwork)
    {
        if (_disposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (artwork is not { Length: > 0 })
        {
            Clear();
            return;
        }

        try
        {
            EnsureAttribute();

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(artwork);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = 220,
                ScaledHeight = 220
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var byteCount = bitmap.PixelWidth * bitmap.PixelHeight * 4;
            var buffer = new Windows.Storage.Streams.Buffer((uint)byteCount);
            buffer.Length = (uint)byteCount;
            bitmap.CopyToBuffer(buffer);

            var pixels = new byte[byteCount];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(pixels);
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bitmap.PixelWidth,
                    Height = -bitmap.PixelHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };

            var nativeBitmap = CreateDIBSection(
                IntPtr.Zero,
                ref bitmapInfo,
                0,
                out var bits,
                IntPtr.Zero,
                0);

            if (nativeBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return;
            }

            try
            {
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                DwmSetIconicThumbnail(_windowHandle, nativeBitmap, 0);
            }
            finally
            {
                DeleteObject(nativeBitmap);
            }
        }
        catch
        {
            // A missing taskbar thumbnail must never affect playback.
        }
    }

    public void Clear()
    {
        if (_disposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            EnsureAttribute();
            DwmSetIconicThumbnail(_windowHandle, IntPtr.Zero, 0);
        }
        catch
        {
            // Best effort.
        }
    }

    private void EnsureAttribute()
    {
        if (_attributeSet)
        {
            return;
        }

        var value = 1;
        DwmSetWindowAttribute(_windowHandle, DwmwaHasIconicBitmap, ref value, sizeof(int));
        _attributeSet = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicThumbnail(
        IntPtr hwnd, IntPtr bitmap, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
