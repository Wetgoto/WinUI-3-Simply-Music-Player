using System.Runtime.InteropServices;

namespace Music.Services;

/// <summary>
/// Enforces a native minimum window size so the player bar can never shrink
/// to the point where progress, transport and volume controls overlap.
/// </summary>
public sealed class WindowMinSizeService : IDisposable
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MinimumWidth = 1120;
    private const int MinimumHeight = 720;
    private static readonly UIntPtr SubclassId = 1;

    private readonly IntPtr _windowHandle;
    private readonly SubclassProc _subclassProc;
    private bool _installed;
    private bool _disposed;

    public WindowMinSizeService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _subclassProc = WndProc;
    }

    public void Initialize()
    {
        if (_installed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        _installed = SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, UIntPtr.Zero);
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr id,
        UIntPtr data)
    {
        if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = Math.Max(info.MinTrackSize.X, MinimumWidth);
            info.MinTrackSize.Y = Math.Max(info.MinTrackSize.Y, MinimumHeight);
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_installed)
        {
            RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
            _installed = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    private delegate IntPtr SubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr id,
        UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd, SubclassProc callback, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
