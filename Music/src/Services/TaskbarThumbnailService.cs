using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Music.Services;

/// <summary>
/// Thumbnail toolbar integration for the taskbar preview. Uses the
/// IMAGE-LIST form of THUMBBUTTON because some shell versions reject the
/// per-button HICON form.
/// </summary>
public sealed class TaskbarService : IDisposable
{
    private const int GwlWndProc = -4;
    private const int WmCommand = 0x0111;
    private const int ThbnClicked = 0x1800;
    private const int ImageIcon = 1;
    private const int LrLoadFromFile = 0x0010;
    private const uint ThbImage = 0x00000001;
    private const uint ThbTooltip = 0x00000004;
    private const uint ThbFlags = 0x00000008;
    private const uint IlcColor32 = 0x00000020;
    private const uint IlcMask = 0x00000001;

    private const int PlayImage = 0;
    private const int PauseImage = 1;
    private const int PreviousImage = 2;
    private const int NextImage = 3;

    private readonly IntPtr _windowHandle;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _togglePlayback;
    private readonly Action _previous;
    private readonly Action _next;
    private readonly WndProcDelegate _wndProc;

    private ITaskbarList4? _taskbar;
    private IntPtr _imageList;
    private IntPtr _oldWndProc;
    private bool _hooked;
    private bool _disposed;
    private bool _isPlaying;
    private readonly List<IntPtr> _icons = [];
    private string _previousTip = "上一首";
    private string _playPauseTip = "播放 / 暂停";
    private string _nextTip = "下一首";

    public TaskbarService(
        IntPtr windowHandle,
        DispatcherQueue dispatcher,
        Action togglePlayback,
        Action previous,
        Action next)
    {
        _windowHandle = windowHandle;
        _dispatcher = dispatcher;
        _togglePlayback = togglePlayback;
        _previous = previous;
        _next = next;
        _wndProc = WndProc;
    }

    public bool Initialize()
    {
        try
        {
            var classId = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            var type = Type.GetTypeFromCLSID(classId);
            if (type is null)
            {
                return false;
            }

            _taskbar = (ITaskbarList4?)Activator.CreateInstance(type);
            if (_taskbar is null || _taskbar.HrInit() != 0)
            {
                _taskbar = null;
                return false;
            }

            _imageList = ImageList_Create(16, 16, IlcColor32 | IlcMask, 4, 4);
            if (_imageList == IntPtr.Zero)
            {
                return false;
            }

            AddImage("play.ico");
            AddImage("pause.ico");
            AddImage("previous.ico");
            AddImage("next.ico");

            _taskbar.ThumbBarSetImageList(_windowHandle, _imageList);

            var addResult = CallButtons(CreateButtons(), _taskbar.ThumbBarAddButtons);
            if (addResult != 0)
            {
                return false;
            }

            HookWindowProc();
            UpdatePlayButton();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Updates the thumbnail button tooltips, for example after a language change.
    /// </summary>
    public void SetToolTips(string previous, string playPause, string next)
    {
        _previousTip = previous;
        _playPauseTip = playPause;
        _nextTip = next;

        if (_taskbar is null || _disposed)
        {
            return;
        }

        try
        {
            CallButtons(
                [
                    Button(1, PreviousImage, _previousTip),
                    Button(2, _isPlaying ? PauseImage : PlayImage, _playPauseTip),
                    Button(3, NextImage, _nextTip)
                ],
                _taskbar.ThumbBarUpdateButtons);
        }
        catch
        {
            // Shell can be shutting down.
        }
    }

    private THUMBBUTTON[] CreateButtons()
    {
        return
        [
            Button(1, PreviousImage, _previousTip),
            Button(2, PlayImage, _playPauseTip),
            Button(3, NextImage, _nextTip)
        ];
    }

    public void SetPlaying(bool isPlaying)
    {
        _isPlaying = isPlaying;
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        if (_taskbar is null || _disposed)
        {
            return;
        }

        try
        {
            CallButtons(
                [Button(2, _isPlaying ? PauseImage : PlayImage, _playPauseTip)],
                _taskbar.ThumbBarUpdateButtons);
        }
        catch
        {
            // Shell can be shutting down.
        }
    }

    private void AddImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Taskbar", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(AppPaths.ApplicationDirectory, "Assets", "Taskbar", fileName);
        }

        var icon = File.Exists(path)
            ? LoadImage(IntPtr.Zero, path, ImageIcon, 16, 16, LrLoadFromFile)
            : IntPtr.Zero;
        if (icon == IntPtr.Zero)
        {
            icon = LoadIcon(IntPtr.Zero, (IntPtr)32512);
        }

        _icons.Add(icon);
        ImageList_AddIcon(_imageList, icon);
    }

    private void HookWindowProc()
    {
        _oldWndProc = SetWindowLongPtr(
            _windowHandle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_wndProc));
        _hooked = _oldWndProc != IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmCommand)
        {
            var value = wParam.ToInt64();
            var id = (int)(value & 0xFFFF);
            var notification = (int)((value >> 16) & 0xFFFF);
            if (notification == ThbnClicked && id is >= 1 and <= 3)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    switch (id)
                    {
                        case 1: _previous(); break;
                        case 2: _togglePlayback(); break;
                        case 3: _next(); break;
                    }
                });
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hwnd, message, wParam, lParam);
    }

    private int CallButtons(
        THUMBBUTTON[] buttons,
        Func<IntPtr, uint, IntPtr, int> method)
    {
        var size = Marshal.SizeOf<THUMBBUTTON>();
        var pointer = Marshal.AllocHGlobal(size * buttons.Length);
        try
        {
            for (var index = 0; index < buttons.Length; index++)
            {
                Marshal.StructureToPtr(buttons[index], pointer + index * size, false);
            }

            return method(_windowHandle, (uint)buttons.Length, pointer);
        }
        finally
        {
            for (var index = 0; index < buttons.Length; index++)
            {
                Marshal.DestroyStructure<THUMBBUTTON>(pointer + index * size);
            }

            Marshal.FreeHGlobal(pointer);
        }
    }

    private static THUMBBUTTON Button(uint id, int image, string tooltip)
    {
        return new THUMBBUTTON
        {
            Mask = ThbImage | ThbTooltip | ThbFlags,
            Id = id,
            Bitmap = (uint)image,
            Icon = IntPtr.Zero,
            Tip = tooltip,
            Flags = 0
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_hooked)
            {
                SetWindowLongPtr(_windowHandle, GwlWndProc, _oldWndProc);
                _hooked = false;
            }
        }
        catch
        {
            // Best effort.
        }

        foreach (var icon in _icons)
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }

        _icons.Clear();

        if (_imageList != IntPtr.Zero)
        {
            ImageList_Destroy(_imageList);
            _imageList = IntPtr.Zero;
        }

        if (_taskbar is not null)
        {
            Marshal.ReleaseComObject(_taskbar);
            _taskbar = null;
        }
    }

    [ComImport]
    [Guid("C43DC798-95D1-4BEA-9030-BB99E2983A1A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList4
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
        [PreserveSig] int RegisterTab(IntPtr tab, IntPtr mdi);
        [PreserveSig] int UnregisterTab(IntPtr tab);
        [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr insertBefore);
        [PreserveSig] int SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
        [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr clip);
        [PreserveSig] int SetTabProperties(IntPtr hwnd, uint flags);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint Mask;
        public uint Id;
        public uint Bitmap;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Tip;
        public uint Flags;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr instance, string name, int type, int width, int height, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_Create(
        int width, int height, uint flags, int initial, int grow);

    [DllImport("comctl32.dll")]
    private static extern int ImageList_AddIcon(IntPtr imageList, IntPtr icon);

    [DllImport("comctl32.dll")]
    private static extern bool ImageList_Destroy(IntPtr imageList);
}
