using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using Music.Models;
using Music.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Music;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<Track> _queue = [];
    private readonly ObservableCollection<Track> _visibleQueue = [];
    private readonly ObservableCollection<Track> _recentTracks = [];
    private readonly ObservableCollection<Track> _visibleRecentTracks = [];
    private readonly ObservableCollection<Track> _customPlaylistTracks = [];
    private readonly ObservableCollection<Track> _visibleCustomPlaylistTracks = [];
    private readonly List<SidebarPlaylistItem> _customPlaylists = [];
    private RecentSortMode _recentSortMode = RecentSortMode.RecentlyPlayed;
    private CustomSortMode _customSortMode = CustomSortMode.AddedOrder;
    private int _customPlaylistLoadVersion;
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly MediaPlayer _player = new();
    private readonly DispatcherQueueTimer _positionTimer;

    private Track? _currentTrack;

    /// <summary>
    /// The list playback is currently following: the default queue, a custom
    /// playlist, or a single track started from Recently played.
    /// </summary>
    private PlaybackScope _playbackScope = PlaybackScope.Queue;

    private bool _taskbarInitialized;
    private TaskbarService? _taskbarService;
    private SystemMediaTransportControls? _systemMediaControls;
    private StorageItemThumbnail? _systemMediaThumbnail;
    private InMemoryRandomAccessStream? _systemMediaArtworkStream;
    private DwmThumbnailService? _dwmThumbnail;
    private WindowMinSizeService? _minSizeService;
    private InMemoryRandomAccessStream? _fallbackStream;
    private bool _isImporting;
    private bool _isSeeking;
    private bool _isUpdatingProgress;
    private bool _pendingAutoPlay;
    private bool _hasInitialized;
    private bool _isMuted;
    private bool _isUpdatingVolume;
    private bool _isPlayerPageOpen;
    private bool _isPageTransitioning;
    private bool _playlistReady;
    private double _volumeBeforeMute = 65;
    private RepeatMode _repeatMode = RepeatMode.None;
    private Brush? _repeatIdleForeground;
    private Brush? _fullRepeatIdleForeground;
    private SolidColorBrush? _accentBrush;

    /// <summary>Cross-fade duration when switching to or from the player page.</summary>
    private const int PlayerPageFadeMilliseconds = 200;

    public MainWindow()
    {
        InitializeComponent();
        _selectedNavButton = DefaultPlaylistButton;
        DefaultPlaylistButton.Background = CreateNavSelectionBrush();
        LoadCustomPlaylists();

        Title = "Simple Music Player";

        // Read the saved preference before the window is first shown so the
        // backdrop does not visibly change afterwards.
        _settings = AppSettingsStore.Load();
        ApplyTransparentBackground(_settings.TransparentBackground);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        AppWindow.Title = Title;
        InitializeSettings();

        QueueList.ItemsSource = _visibleQueue;
        QueueList.ContainerContentChanging += QueueList_ContainerContentChanging;
        RecentList.ItemsSource = _visibleRecentTracks;
        RecentList.ContainerContentChanging += QueueList_ContainerContentChanging;
        CustomPlaylistList.ItemsSource = _visibleCustomPlaylistTracks;
        CustomPlaylistList.ContainerContentChanging += QueueList_ContainerContentChanging;
        SearchList.ItemsSource = _searchResults;
        SearchList.ContainerContentChanging += QueueList_ContainerContentChanging;

        InitializeFavorites();
        InitializePlayerPagePanel();

        // Catch right-taps even if a ListViewItem already handled the event.
        QueueList.AddHandler(
            UIElement.RightTappedEvent,
            new RightTappedEventHandler(QueueList_RightTapped),
            true);

        SearchList.AddHandler(
            UIElement.RightTappedEvent,
            new RightTappedEventHandler(SearchList_RightTapped),
            true);

        RecentList.AddHandler(
            UIElement.RightTappedEvent,
            new RightTappedEventHandler(RecentList_RightTapped),
            true);

        CustomPlaylistList.AddHandler(
            UIElement.RightTappedEvent,
            new RightTappedEventHandler(CustomPlaylistList_RightTapped),
            true);

        // The player bar and the full page both open the track menu on
        // right-click; the bar's own buttons and sliders must not swallow it.
        PlayerBar.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PlayerBar_PointerPressed),
            true);
        PlayerPage.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PlayerPage_PointerPressed),
            true);

        // Listen for drag events even when an inner control (such as the
        // ListView) already handled them.
        QueueDropSurface.AddHandler(
            UIElement.DragEnterEvent,
            new DragEventHandler(Playlist_DragEnter),
            true);
        QueueDropSurface.AddHandler(
            UIElement.DragOverEvent,
            new DragEventHandler(Playlist_DragOver),
            true);
        QueueDropSurface.AddHandler(
            UIElement.DragLeaveEvent,
            new DragEventHandler(Playlist_DragLeave),
            true);
        QueueDropSurface.AddHandler(
            UIElement.DropEvent,
            new DragEventHandler(Playlist_Drop),
            true);

        // Dropping anywhere on the default list page counts, not just over the
        // track card, so the whole page is a drop target.
        MainWorkspace.AllowDrop = true;
        MainWorkspace.AddHandler(
            UIElement.DragEnterEvent,
            new DragEventHandler(Playlist_DragEnter),
            true);
        MainWorkspace.AddHandler(
            UIElement.DragOverEvent,
            new DragEventHandler(Playlist_DragOver),
            true);
        MainWorkspace.AddHandler(
            UIElement.DragLeaveEvent,
            new DragEventHandler(Playlist_DragLeave),
            true);
        MainWorkspace.AddHandler(
            UIElement.DropEvent,
            new DragEventHandler(Playlist_Drop),
            true);
        ConfigureCustomPlaylistDrop();

        _positionTimer = DispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _positionTimer.Tick += PositionTimer_Tick;
        _positionTimer.Start();

        _player.AutoPlay = false;
        _player.Volume = VolumeSlider.Value / 100d;
        _player.MediaOpened += Player_MediaOpened;
        _player.MediaEnded += Player_MediaEnded;
        _player.MediaFailed += Player_MediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += Player_PlaybackStateChanged;
        ConfigureSystemMediaControls();
        _dwmThumbnail = new DwmThumbnailService(WindowNative.GetWindowHandle(this));
        _minSizeService = new WindowMinSizeService(WindowNative.GetWindowHandle(this));
        _minSizeService.Initialize();

        ProgressSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(ProgressSlider_PointerPressed),
            true);
        ProgressSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(ProgressSlider_PointerReleased),
            true);
        ProgressSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(ProgressSlider_PointerCaptureLost),
            true);

        FullProgressSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(ProgressSlider_PointerPressed),
            true);
        FullProgressSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(ProgressSlider_PointerReleased),
            true);
        FullProgressSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(ProgressSlider_PointerCaptureLost),
            true);

        AppWindow.Changed += AppWindow_Changed;
        Closed += MainWindow_Closed;
        Activated += MainWindow_Activated;

        ResizeAndCenter();
        ResetNowPlaying();
        RefreshQueueUi();
        UpdateVolumeIcon();
        _repeatIdleForeground = GetSecondaryTextBrush();
        _fullRepeatIdleForeground = GetSecondaryTextBrush();
        UpdateRepeatUi();
        SetStatus(Strings.Initializing);
    }

    private bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    private void ApplyNativeTitleBarTheme(bool dark)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var value = dark ? 1 : 0;

        // DWMWA_USE_IMMERSIVE_DARK_MODE (20 on current Windows, 19 on older builds).
        if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
        }

        // DWMWA_SYSTEMBACKDROP_TYPE = 3 (transient window / Acrylic).
        var acrylicBackdrop = 3;
        DwmSetWindowAttribute(hwnd, 38, ref acrylicBackdrop, sizeof(int));
    }

    private void ApplyCustomTitleBarTheme(bool dark)
    {
        var titleBar = AppWindow.TitleBar;
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var foreground = dark
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 17, 17, 17);
        var inactiveForeground = dark
            ? Color.FromArgb(150, 255, 255, 255)
            : Color.FromArgb(150, 0, 0, 0);
        var hoverBackground = dark
            ? Color.FromArgb(28, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0);
        var pressedBackground = dark
            ? Color.FromArgb(44, 255, 255, 255)
            : Color.FromArgb(32, 0, 0, 0);

        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private void PlayerPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FullLeftColumn is null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        // Keep enough vertical room for the fixed title/controls rows. On a
        // wide, short window this moves the split line left rather than
        // allowing the player content to overflow.
        const double fixedRowsHeight = 220;
        var idealLeft = e.NewSize.Width * 0.39;
        var maxLeft = Math.Max(240, e.NewSize.Height - fixedRowsHeight);
        var targetLeft = Math.Clamp(
            Math.Min(idealLeft, maxLeft),
            240,
            e.NewSize.Width);

        if (Math.Abs(FullLeftColumn.Width.Value - targetLeft) > 0.5)
        {
            FullLeftColumn.Width = new GridLength(targetLeft);
        }
    }

    private void FullLeftPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FullCover is null || FullTitleRow is null || FullControlsBorder is null || FullVolumeSlider is null)
        {
            return;
        }

        var paneWidth = e.NewSize.Width;
        var paneHeight = e.NewSize.Height;
        if (paneWidth <= 0 || paneHeight <= 0)
        {
            return;
        }

        // The cover and controls share the same inset from the left, right and
        // top/bottom edges. The action buttons and transport keep fixed sizes.
        const double minMargin = 32;
        const double fixedRowsHeight = 220;
        var margin = Math.Max(
            minMargin,
            paneWidth + fixedRowsHeight - paneHeight);
        margin = Math.Min(margin, Math.Max(0, (paneWidth - 240) / 2));

        var coverSize = Math.Max(240, paneWidth - margin * 2);
        FullCover.Width = coverSize;
        FullCover.Height = coverSize;
        FullCover.Margin = new Thickness(margin, margin, 0, 0);

        FullTitleRow.Width = coverSize;
        FullTitleRow.Margin = new Thickness(margin, 8, 0, 8);

        FullControlsBorder.Width = coverSize;
        FullControlsBorder.Margin = new Thickness(margin, 0, 0, margin);

        // Once the controls get too narrow, keep only the mute icon rather
        // than allowing the slider and transport controls to overlap.
        FullVolumeSlider.Visibility = coverSize < 460
            ? Visibility.Collapsed
            : Visibility.Visible;


    }

    public async Task InitializeAsync(IReadOnlyList<string> arguments)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;

        try
        {
            await RestorePlaylistAsync();
            await LoadLaunchFilesAsync(arguments);
        }
        catch (Exception exception)
        {
            SetStatus(Strings.StatusInitFailed(exception.Message));
        }
    }

    private async Task RestorePlaylistAsync()
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = await PlaylistStore.LoadPathsAsync();
        }
        catch
        {
            paths = [];
        }

        if (paths.Count == 0)
        {
            _playlistReady = true;
            SetStatus(Strings.StatusReadyToImport);
            return;
        }

        _isImporting = true;
        SetImportingUi(true);
        SetStatus(Strings.StatusRestoring);

        var missingTracks = new List<Track>();

        try
        {
            foreach (var path in paths)
            {
                if (_knownPaths.Contains(path))
                {
                    continue;
                }

                Track track;
                if (!File.Exists(path))
                {
                    track = new Track(path, isMissing: true);
                    var importedAt = await TrackDataStore.GetImportedAtAsync(path);
                    if (importedAt is not null)
                    {
                        track.SetImportedAt(importedAt.Value);
                    }

                    missingTracks.Add(track);
                }
                else
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        track = await MusicLibrary.LoadAsync(file);
                    }
                    catch
                    {
                        // The file exists but cannot be brokered as StorageFile;
                        // keep it in the queue through the path-based fallback.
                        track = await MusicLibrary.LoadAsync(path);
                    }
                }

                _knownPaths.Add(track.Path);
                _queue.Add(track);
                RefreshQueueUi();
            }
        }
        finally
        {
            _isImporting = false;
            _playlistReady = true;
            SetImportingUi(false);
        }

        RefreshQueueUi();

        if (_queue.Count > 0)
        {
            var firstPlayable = _queue.FirstOrDefault(track => !track.IsMissing);
            if (firstPlayable is not null)
            {
                LoadTrack(firstPlayable, autoPlay: false);
            }

            SetStatus(missingTracks.Count == 0
                ? Strings.StatusRestored(_queue.Count)
                : Strings.StatusRestoredWithMissing(_queue.Count, missingTracks.Count));
        }
        else
        {
            SetStatus(Strings.StatusReadyToImport);
        }

        if (missingTracks.Count > 0)
        {
            await ShowMissingTracksDialogAsync(missingTracks);
        }
    }

    private void PersistPlaylist()
    {
        if (!_playlistReady)
        {
            return;
        }

        PlaylistStore.SavePaths(_queue.Select(track => track.Path));
    }

    private async Task ShowMissingTracksDialogAsync(IReadOnlyList<Track> missingTracks)
    {
        if (missingTracks.Count == 0)
        {
            return;
        }

        await EnsureWindowLoadedAsync();

        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var content = new StackPanel
        {
            Spacing = 10
        };

        content.Children.Add(new TextBlock
        {
            Text = missingTracks.Count == 1
                ? Strings.MissingFilesSingle
                : Strings.MissingFilesMany(missingTracks.Count),
            TextWrapping = TextWrapping.Wrap
        });

        var fileList = new StackPanel
        {
            Spacing = 3
        };

        foreach (var track in missingTracks.Take(8))
        {
            fileList.Children.Add(new TextBlock
            {
                Text = $"•  {track.Path}",
                FontSize = 11,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        if (missingTracks.Count > 8)
        {
            fileList.Children.Add(new TextBlock
            {
                Text = Strings.MissingFilesMore(missingTracks.Count - 8),
                FontSize = 11,
                Opacity = 0.7
            });
        }

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = fileList
        });

        content.Children.Add(new TextBlock
        {
            Text = Strings.MissingFilesRemove,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.MissingFilesTitle,
            Content = content,
            PrimaryButtonText = Strings.RemoveFromList,
            CloseButtonText = Strings.Keep,
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch
        {
            return;
        }

        if (result == ContentDialogResult.Primary)
        {
            RemoveMissingTracks(missingTracks);
        }
    }

    private void RemoveMissingTracks(IReadOnlyList<Track> tracks)
    {
        var removed = 0;

        foreach (var track in tracks.ToList())
        {
            if (ReferenceEquals(track, _currentTrack))
            {
                _player.Pause();
                _player.Source = null;
                _fallbackStream?.Dispose();
                _fallbackStream = null;
                _pendingAutoPlay = false;
                _currentTrack = null;
                ResetNowPlaying();
            }

            if (_queue.Remove(track))
            {
                _knownPaths.Remove(track.Path);
                removed++;
            }
        }

        RefreshQueueUi();

        if (QueueList.SelectedItem is Track selected && !_queue.Contains(selected))
        {
            QueueList.SelectedItem = null;
        }

        PersistPlaylist();
        SetStatus(removed > 0
            ? Strings.StatusRemovedMissing(removed)
            : Strings.StatusNoMissingToRemove);
    }

    private Task EnsureWindowLoadedAsync()
    {
        if (RootGrid.XamlRoot is not null)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>();
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            RootGrid.Loaded -= handler;
            completion.TrySetResult(true);
        };
        RootGrid.Loaded += handler;
        return completion.Task;
    }

    private async Task LoadLaunchFilesAsync(IReadOnlyList<string> arguments)
    {
        var launchFiles = new List<StorageFile>();
        var fallbackPaths = new List<string>();

        foreach (var argument in arguments)
        {
            if (!File.Exists(argument) || !MusicLibrary.IsSupported(argument))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(argument);
            try
            {
                launchFiles.Add(await StorageFile.GetFileFromPathAsync(fullPath));
            }
            catch
            {
                // Launching from a shell that cannot broker StorageFile access still allows
                // playback through a local file URI.
                fallbackPaths.Add(fullPath);
            }
        }

        if (launchFiles.Count > 0)
        {
            await AddFilesAsync(launchFiles, autoPlayFirst: true);
        }

        var addedFallback = 0;
        foreach (var path in fallbackPaths)
        {
            if (!_knownPaths.Add(path))
            {
                continue;
            }

            var track = new Track(path);
            _queue.Add(track);
            addedFallback++;
        }

        if (addedFallback > 0)
        {
            RefreshQueueUi();
            PersistPlaylist();

            if (_currentTrack is null)
            {
                var firstPlayable = FindPlayable(0, 1);
                if (firstPlayable is not null)
                {
                    LoadTrack(firstPlayable, autoPlay: true);
                }
            }
        }

        await LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        var paths = await RecentStore.LoadPathsAsync();
        foreach (var path in paths)
        {
            if (_recentTracks.Any(track =>
                    string.Equals(track.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var track = _queue.FirstOrDefault(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (track is null)
            {
                track = File.Exists(path)
                    ? await MusicLibrary.LoadAsync(path)
                    : new Track(path, isMissing: true);
            }

            if (!File.Exists(path))
            {
                track.MarkMissing();
            }

            _recentTracks.Add(track);
        }

        RefreshRecentUi();
    }

    private void AddRecent(Track track)
    {
        if (_recentTracks.Count > 0 &&
            string.Equals(
                _recentTracks[0].Path,
                track.Path,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = _recentTracks.FirstOrDefault(item =>
            string.Equals(item.Path, track.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _recentTracks.Remove(existing);
        }

        _recentTracks.Insert(0, track);
        while (_recentTracks.Count > 50)
        {
            _recentTracks.RemoveAt(_recentTracks.Count - 1);
        }

        RefreshRecentUi();
        RecentStore.SavePaths(_recentTracks.Select(item => item.Path));
    }

    private void RefreshRecentUi()
    {
        var selected = RecentList.SelectedItem as Track;
        var query = RecentSearchBox?.Text?.Trim();
        var searching = !string.IsNullOrWhiteSpace(query);

        IEnumerable<Track> ordered = _recentSortMode switch
        {
            RecentSortMode.Name => _recentTracks
                .OrderBy(track => track.QueueTitle, StringComparer.CurrentCultureIgnoreCase),
            RecentSortMode.Duration => _recentTracks.OrderBy(track => track.Duration),
            _ => _recentTracks
        };

        if (searching)
        {
            ordered = ordered.Where(track =>
                Contains(track.Title, query!) ||
                Contains(track.Artist, query!) ||
                Contains(track.Album, query!) ||
                Contains(track.AlbumArtist, query!) ||
                Contains(Path.GetFileName(track.Path), query!));
        }

        _visibleRecentTracks.Clear();
        foreach (var track in ordered)
        {
            _visibleRecentTracks.Add(track);
        }

        RecentList.SelectedItem = selected is not null && _visibleRecentTracks.Contains(selected)
            ? selected
            : null;
        RecentList.Visibility = _visibleRecentTracks.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentEmptyPanel.Visibility = _recentTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentNoResultsPanel.Visibility = searching &&
            _recentTracks.Count > 0 &&
            _visibleRecentTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentClearButton.IsEnabled = _recentTracks.Count > 0;
        RecentCountText.Text = searching
            ? Strings.MatchCount(_visibleRecentTracks.Count)
            : Strings.ItemCount(_recentTracks.Count);

        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RecentSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        RefreshRecentUi();
    }

    private void RecentSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentSearchBox.Visibility == Visibility.Visible)
        {
            RecentSearchBox.Text = string.Empty;
            RecentSearchBox.Visibility = Visibility.Collapsed;
            RecentSearchButton.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            RefreshRecentUi();
            return;
        }

        RecentSearchBox.Visibility = Visibility.Visible;
        RecentSearchButton.Background = CreateNavSelectionBrush();
        RecentSearchBox.Focus(FocusState.Programmatic);
    }

    private void RecentSortButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var byRecent = new MenuFlyoutItem { Text = Strings.SortByRecent };
        var byName = new MenuFlyoutItem { Text = Strings.SortByName };
        var byDuration = new MenuFlyoutItem { Text = Strings.SortByDuration };

        byRecent.Click += (_, _) => SetRecentSortMode(RecentSortMode.RecentlyPlayed);
        byName.Click += (_, _) => SetRecentSortMode(RecentSortMode.Name);
        byDuration.Click += (_, _) => SetRecentSortMode(RecentSortMode.Duration);

        flyout.Items.Add(byRecent);
        flyout.Items.Add(byName);
        flyout.Items.Add(byDuration);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void SetRecentSortMode(RecentSortMode mode)
    {
        _recentSortMode = mode;
        RefreshRecentUi();
    }

    private async void RecentClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recentTracks.Count == 0 || RootGrid.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.ClearRecentTitle,
            Content = Strings.ClearRecentDetail,
            PrimaryButtonText = Strings.Clear,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Close
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        _recentTracks.Clear();
        RecentSearchBox.Text = string.Empty;
        RecentStore.SavePaths([]);
        RefreshRecentUi();
    }

    private void RefreshRecentState()
    {
        foreach (var track in _recentTracks)
        {
            if (!File.Exists(track.Path))
            {
                track.MarkMissing();
            }
            else if (track.IsMissing)
            {
                track.MarkAvailable();
            }
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportFilesAsync();
    }

    private Button? _selectedNavButton;

    private void SidebarNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
        {
            return;
        }

        SelectSidebarNav(button, key);
    }

    private void SelectSidebarNav(Button button, string key)
    {
        ResetSidebarSearch();

        if (_selectedNavButton is not null && _selectedNavButton != button)
        {
            _selectedNavButton.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        _selectedNavButton = button;
        button.Background = CreateNavSelectionBrush();
        _selectedCustomPlaylist = null;

        // Results shown on the search page reflect their source playlists, so
        // refresh them after the navigation (the page may still be open behind
        // the one being switched to).
        RefreshSearchResults(_lastSearchQuery);

        // Only a custom playlist can keep its own play order. Switching away
        // while one was playing falls back to the default queue.
        if (_playbackScope == PlaybackScope.CustomPlaylist)
        {
            _playbackScope = PlaybackScope.Queue;
        }

        if (key == "default")
        {
            MainWorkspace.Visibility = Visibility.Visible;
            RecentPage.Visibility = Visibility.Collapsed;
            HideFavoritesPage();
            CustomPlaylistPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            DefaultListTitleText.Text = Strings.DefaultList;
            return;
        }

        if (key == "recent")
        {
            RefreshRecentState();
            RefreshRecentUi();
            MainWorkspace.Visibility = Visibility.Collapsed;
            RecentPage.Visibility = Visibility.Visible;
            HideFavoritesPage();
            CustomPlaylistPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            return;
        }

        if (key == "favorites")
        {
            MainWorkspace.Visibility = Visibility.Collapsed;
            RecentPage.Visibility = Visibility.Collapsed;
            CustomPlaylistPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            ShowFavoritesPage();
            return;
        }

        if (key == "settings")
        {
            MainWorkspace.Visibility = Visibility.Collapsed;
            RecentPage.Visibility = Visibility.Collapsed;
            HideFavoritesPage();
            CustomPlaylistPage.Visibility = Visibility.Collapsed;
            SearchPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
        }
    }

    private void TogglePlaylistSection(object sender, RoutedEventArgs e)
    {
        var show = PlaylistItems.Visibility != Visibility.Visible;
        PlaylistItems.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PlaylistChevron.Glyph = show ? "\uE70D" : "\uE70E";
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Visibility == Visibility.Visible)
        {
            CloseSearch();
            return;
        }

        SearchBox.Visibility = Visibility.Visible;
        SearchButton.Background = CreateNavSelectionBrush();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void CloseSearch()
    {
        SearchBox.Text = string.Empty;
        SearchBox.Visibility = Visibility.Collapsed;
        SearchButton.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        RefreshVisibleQueue();
    }

    private void RefreshVisibleQueue()
    {
        var selected = QueueList.SelectedItem as Track;
        var query = SearchBox?.Text?.Trim();
        var searching = !string.IsNullOrWhiteSpace(query);
        var matches = searching
            ? _queue.Where(track =>
                Contains(track.Title, query!) ||
                Contains(track.Artist, query!) ||
                Contains(track.Album, query!) ||
                Contains(track.AlbumArtist, query!) ||
                Contains(Path.GetFileName(track.Path), query!))
            : _queue;

        _visibleQueue.Clear();
        foreach (var track in matches)
        {
            _visibleQueue.Add(track);
        }

        QueueList.SelectedItem = selected is not null && _visibleQueue.Contains(selected)
            ? selected
            : null;

        QueueList.Visibility = _queue.Count > 0 &&
            (!searching || _visibleQueue.Count > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyQueuePanel.Visibility = _queue.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoSearchResultsPanel.Visibility = searching &&
            _queue.Count > 0 &&
            _visibleQueue.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var missingCount = _queue.Count(track => track.IsMissing);
        QueueCountText.Text = searching
            ? Strings.MatchCount(_visibleQueue.Count)
            : Strings.TrackCount(_queue.Count, missingCount);

        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var byName = new MenuFlyoutItem { Text = Strings.SortByName };
        var byDuration = new MenuFlyoutItem { Text = Strings.SortByDuration };
        var byAdded = new MenuFlyoutItem { Text = Strings.SortByAdded };

        byName.Click += (_, _) => SortQueue(QueueSortMode.Name);
        byDuration.Click += (_, _) => SortQueue(QueueSortMode.Duration);
        byAdded.Click += (_, _) => SortQueue(QueueSortMode.AddedAt);

        flyout.Items.Add(byName);
        flyout.Items.Add(byDuration);
        flyout.Items.Add(byAdded);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void SortQueue(QueueSortMode mode)
    {
        var sorted = mode switch
        {
            QueueSortMode.Name => _queue
                .OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            QueueSortMode.Duration => _queue
                .OrderBy(track => track.Duration)
                .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            _ => _queue
                .OrderBy(track => track.ImportedAt)
                .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };

        var current = _currentTrack;
        _queue.Clear();
        foreach (var track in sorted)
        {
            _queue.Add(track);
        }

        QueueList.SelectedItem = current;
        RefreshQueueUi();
        PersistPlaylist();
        SetStatus(mode switch
        {
            QueueSortMode.Name => Strings.SortedByName,
            QueueSortMode.Duration => Strings.SortedByDuration,
            _ => Strings.SortedByAdded
        });
    }

    private enum QueueSortMode
    {
        Name,
        Duration,
        AddedAt
    }

    private enum RecentSortMode
    {
        RecentlyPlayed,
        Name,
        Duration
    }

    private enum CustomSortMode
    {
        AddedOrder,
        Name,
        Duration
    }

    private async void EmptyImportButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportFilesAsync();
    }

    private void Playlist_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.AddToQueueCaption;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private void Playlist_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Playlist_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        if (_isImporting)
        {
            SetStatus(Strings.StatusReadingFiles);
            return;
        }

        IReadOnlyList<IStorageItem> droppedItems;
        try
        {
            droppedItems = await e.DataView.GetStorageItemsAsync();
        }
        catch (Exception exception)
        {
            SetStatus(Strings.StatusDropReadFailed(exception.Message));
            return;
        }

        var files = droppedItems
            .OfType<StorageFile>()
            .Where(file => MusicLibrary.IsSupported(file.Path))
            .ToList();

        if (files.Count == 0)
        {
            SetStatus(Strings.StatusNoAudioDropped);
            return;
        }

        await AddFilesAsync(files);
    }

    private async Task ImportFilesAsync()
    {
        if (_isImporting)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };

        foreach (var extension in MusicLibrary.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files;
        try
        {
            files = await picker.PickMultipleFilesAsync();
        }
        catch (Exception exception)
        {
            SetStatus(Strings.StatusPickerFailed(exception.Message));
            return;
        }

        if (files.Count == 0)
        {
            return;
        }

        await AddFilesAsync(files);
    }

    private async Task AddFilesAsync(IReadOnlyList<StorageFile> files, bool autoPlayFirst = false)
    {
        if (_isImporting || files.Count == 0)
        {
            return;
        }

        var candidates = new List<StorageFile>();
        var skipped = 0;

        foreach (var file in files)
        {
            if (!MusicLibrary.IsSupported(file.Path) || _knownPaths.Contains(file.Path))
            {
                skipped++;
                continue;
            }

            candidates.Add(file);
        }

        if (candidates.Count == 0)
        {
            SetStatus(skipped > 0 ? Strings.StatusAllAlreadyInQueue : Strings.StatusNoAudioSelected);
            return;
        }

        _isImporting = true;
        SetImportingUi(true);

        var added = 0;
        var failed = 0;

        try
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var file = candidates[index];
                SetStatus(Strings.StatusReadingTrack(index + 1, candidates.Count, file.DisplayName));

                try
                {
                    var track = await MusicLibrary.LoadAsync(file);
                    if (_knownPaths.Add(track.Path))
                    {
                        _queue.Add(track);
                        added++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch
                {
                    failed++;
                }

                RefreshQueueUi();
            }
        }
        finally
        {
            _isImporting = false;
            SetImportingUi(false);
        }

        RefreshQueueUi();

        if (added > 0)
        {
            PersistPlaylist();

            var firstPlayable = FindPlayable(0, 1);
            var willLoad = _currentTrack is null && firstPlayable is not null;

            if (willLoad)
            {
                LoadTrack(firstPlayable!, autoPlay: autoPlayFirst);
            }

            SetStatus(willLoad && autoPlayFirst
                ? Strings.StatusLoading(firstPlayable!.Title)
                : Strings.StatusImported(added, skipped + failed));
        }
        else
        {
            SetStatus(failed > 0
                ? Strings.StatusImportFailed
                : Strings.StatusNothingAdded);
        }
    }

    private void SetImportingUi(bool importing)
    {
        ImportButton.IsEnabled = !importing;
        EmptyImportButton.IsEnabled = !importing;
        ImportProgressRing.IsActive = importing;
        ImportProgressRing.Visibility = importing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadTrack(Track track, bool autoPlay)
    {
        // The track may live in the default queue, a custom playlist, or only
        // in Recently played, so the queue alone is not a valid guard here.
        if (IndexInPlaybackScope(track) < 0)
        {
            return;
        }

        if (track.IsMissing)
        {
            if (!File.Exists(track.Path))
            {
                SetStatus(Strings.StatusMissingFile(track.Title));
                return;
            }

            track.MarkAvailable();
            RefreshQueueUi();
            PersistPlaylist();
        }

        if (track.File is null && !File.Exists(track.Path))
        {
            track.MarkMissing();
            RefreshQueueUi();
            PersistPlaylist();
            SetStatus(Strings.StatusMissingFile(track.Title));
            return;
        }

        if (ReferenceEquals(_currentTrack, track) && _player.Source is not null)
        {
            if (autoPlay)
            {
                RestartIfAtEnd();
                _player.Play();
                SetStatus(Strings.StatusNowPlaying(track.Title));
            }

            return;
        }

        if (_currentTrack is not null)
        {
            _currentTrack.IsCurrent = false;
            _currentTrack.IsPlaying = false;
        }

        _currentTrack = track;
        track.IsCurrent = true;
        track.IsPlaying = false;

        // Only mirror the selection inside the default list; a track played
        // from Recently played or Favorites does not belong to that list.
        if (_queue.Contains(track))
        {
            QueueList.SelectedItem = track;
            QueueList.ScrollIntoView(track);
        }
        else
        {
            QueueList.SelectedItem = null;
        }

        UpdateNowPlaying(track);

        _pendingAutoPlay = autoPlay;

        InMemoryRandomAccessStream? previousFallback = null;
        try
        {
            var source = CreateMediaSource(track, out previousFallback);
            _player.Source = source;
            previousFallback?.Dispose();
        }
        catch (Exception exception)
        {
            _player.Pause();
            _player.Source = null;
            var fallback = _fallbackStream;
            _fallbackStream = null;
            fallback?.Dispose();

            if (!ReferenceEquals(previousFallback, fallback))
            {
                previousFallback?.Dispose();
            }

            SetStatus(Strings.StatusLoadFailed(track.Title, exception.Message));
            ResetNowPlaying();
            return;
        }

        UpdateTransportUi();
        NowStateText.Text = LoadingState;

        if (autoPlay)
        {
            SetStatus(Strings.StatusLoading(track.Title));
        }
    }

    private MediaSource CreateMediaSource(Track track, out InMemoryRandomAccessStream? previousFallback)
    {
        previousFallback = _fallbackStream;

        if (track.File is not null)
        {
            _fallbackStream = null;
            return MediaSource.CreateFromStorageFile(track.File);
        }

        // The path fallback is used when a launch path cannot be brokered as a
        // StorageFile. Reading it here keeps playback working in that case.
        var bytes = File.ReadAllBytes(track.Path);
        var stream = new InMemoryRandomAccessStream();

        try
        {
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            stream.Seek(0);
            _fallbackStream = stream;
            return MediaSource.CreateFromStream(stream, GetContentType(track.FileType));
        }
        catch
        {
            _fallbackStream = previousFallback;
            stream.Dispose();
            throw;
        }
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" or ".m4b" => "audio/mp4",
            ".aac" => "audio/aac",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".ogg" or ".opus" => "audio/ogg",
            ".wma" => "audio/x-ms-wma",
            ".aif" or ".aiff" => "audio/aiff",
            _ => "application/octet-stream"
        };
    }

    private void TogglePlayback()
    {
        if (_currentTrack is null)
        {
            var firstPlayable = FindPlayable(0, 1);
            if (firstPlayable is not null)
            {
                LoadTrack(firstPlayable, autoPlay: true);
            }
            else
            {
                SetStatus(_queue.Count == 0 ? Strings.StatusQueueEmpty : Strings.StatusNoPlayable);
            }

            return;
        }

        if (_player.Source is null)
        {
            LoadTrack(_currentTrack, autoPlay: true);
            return;
        }

        var state = _player.PlaybackSession.PlaybackState;
        if (state is MediaPlaybackState.Playing or MediaPlaybackState.Buffering)
        {
            _player.Pause();
            SetStatus(Strings.StatusPausedWith(_currentTrack.Title));
            return;
        }

        RestartIfAtEnd();
        _player.Play();
    }

    private Track? FindPlayable(int startIndex, int direction)
    {
        var scope = PlaybackScopeTracks();
        if (scope.Count == 0 ||
            direction == 0 ||
            startIndex < 0 ||
            startIndex >= scope.Count)
        {
            return null;
        }

        var index = startIndex;
        while (index >= 0 && index < scope.Count)
        {
            var track = scope[index];
            if (IsTrackPlayable(track))
            {
                return track;
            }

            index += direction;
        }

        return null;
    }

    /// <summary>
    /// The ordered tracks the transport buttons and auto-advance follow. This
    /// is the list playback was started from, not always the default queue.
    /// </summary>
    private IReadOnlyList<Track> PlaybackScopeTracks()
    {
        return _playbackScope switch
        {
            PlaybackScope.CustomPlaylist => _customPlaylistTracks,
            PlaybackScope.Single => _currentTrack is null ? [] : [_currentTrack],
            _ => _queue
        };
    }

    private int IndexInPlaybackScope(Track track)
    {
        var scope = PlaybackScopeTracks();
        for (var index = 0; index < scope.Count; index++)
        {
            if (ReferenceEquals(scope[index], track) ||
                string.Equals(scope[index].Path, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Points playback at a list. The default queue and a custom playlist keep
    /// playing to the end of that list; a single track from Recently played
    /// stops once it ends unless repeat is on.
    /// </summary>
    private void SetPlaybackScope(PlaybackScope scope)
    {
        _playbackScope = scope;
    }

    /// <summary>
    /// Keeps the scope in sync after a custom playlist was edited or reloaded:
    /// if the playing track is no longer part of that playlist, fall back to
    /// the default queue rather than stranding playback on an empty scope.
    /// </summary>
    private void RefreshPlaybackScope()
    {
        if (_playbackScope != PlaybackScope.CustomPlaylist || _currentTrack is null)
        {
            return;
        }

        if (_customPlaylistTracks.Count == 0 ||
            IndexInPlaybackScope(_currentTrack) < 0)
        {
            _playbackScope = PlaybackScope.Queue;
        }
    }

    private static bool IsTrackPlayable(Track track)
    {
        return !track.IsMissing && (track.File is not null || File.Exists(track.Path));
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private async void NowPlayingAreaButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenPlayerPageAsync();
    }

    private async Task OpenPlayerPageAsync()
    {
        if (_isPlayerPageOpen || _isPageTransitioning)
        {
            return;
        }

        _isPageTransitioning = true;

        // Cross-fade instead of the old black veil: the player page fades in
        // over the workspace while the player bar fades out, 200 ms total.
        PlayerPage.Opacity = 0;
        PlayerPage.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        _isPlayerPageOpen = true;

        await Task.WhenAll(
            AnimateElementOpacityAsync(PlayerPage, 1, PlayerPageFadeMilliseconds),
            AnimatePlayerBarOpacityAsync(0, PlayerPageFadeMilliseconds));

        PlayerBar.Visibility = Visibility.Collapsed;
        PlayerBar.Opacity = 1;
        _isPageTransitioning = false;
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        await ClosePlayerPageAsync();
    }

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_isPlayerPageOpen || _isPageTransitioning)
        {
            return;
        }

        args.Handled = true;
        _ = ClosePlayerPageAsync();
    }

    private async Task ClosePlayerPageAsync()
    {
        if (!_isPlayerPageOpen || _isPageTransitioning)
        {
            return;
        }

        _isPageTransitioning = true;

        PlayerBar.Visibility = Visibility.Visible;
        PlayerBar.Opacity = 0;
        BackButton.Visibility = Visibility.Collapsed;

        await Task.WhenAll(
            AnimateElementOpacityAsync(PlayerPage, 0, PlayerPageFadeMilliseconds),
            AnimatePlayerBarOpacityAsync(1, PlayerPageFadeMilliseconds));

        PlayerPage.Visibility = Visibility.Collapsed;
        PlayerPage.Opacity = 1;
        _isPlayerPageOpen = false;
        NowPlayingAreaButton.Focus(FocusState.Programmatic);
        _isPageTransitioning = false;
    }

    /// <summary>Fades an element's opacity; used by the page cross-fade.</summary>
    private static Task AnimateElementOpacityAsync(
        UIElement element,
        double targetOpacity,
        int durationMilliseconds)
    {
        var completion = new TaskCompletionSource<bool>();
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult(true);
        storyboard.Begin();
        return completion.Task;
    }

    private Task AnimatePlayerBarOpacityAsync(double targetOpacity, int durationMilliseconds)
    {
        var completion = new TaskCompletionSource<bool>();
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, PlayerBar);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult(true);
        storyboard.Begin();
        return completion.Task;
    }

    private void QueueList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem item)
        {
            return;
        }

        item.PointerEntered -= QueueItem_PointerEntered;
        item.PointerExited -= QueueItem_PointerExited;
        item.PointerEntered += QueueItem_PointerEntered;
        item.PointerExited += QueueItem_PointerExited;
    }

    private void QueueItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetQueueActionsVisible(sender, true);
    }

    private void QueueItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetQueueActionsVisible(sender, false);
    }

    private static void SetQueueActionsVisible(object sender, bool visible)
    {
        if (sender is not FrameworkElement row)
        {
            return;
        }

        var panel = FindDescendantByName(row, "QueueActionsPanel") as FrameworkElement;
        if (panel is null)
        {
            return;
        }

        panel.Opacity = visible ? 1 : 0;
        panel.IsHitTestVisible = visible;
    }

    private static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return child;
            }

            var match = FindDescendantByName(child, name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async void QueuePlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetActionTrack(sender) is { } track)
        {
            await PlayTrackFromUiAsync(track);
        }
    }

    private async void QueueFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetActionTrack(sender) is not { } track)
        {
            return;
        }

        await ToggleFavoriteAsync(track);
    }

    private void QueueAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && GetActionTrack(sender) is { } track)
        {
            ShowAddToPlaylistFlyout(element, track);
        }
    }

    private void QueueMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || GetActionTrack(sender) is not { } track)
        {
            return;
        }

        // The row's "…" opens the same menu as a right-click, so both entry
        // points always offer the same commands.
        ShowTrackContextFlyout(track, element, new Point(0, 0));
    }

    private static Track? GetActionTrack(object sender)
    {
        if (sender is not FrameworkElement element)
        {
            return null;
        }

        return element.DataContext switch
        {
            Track track => track,
            SearchResult result => result.Track,
            _ => element.Tag as Track
        };
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        // The full player page star reflects the playing track, which may come
        // from any list, so it goes through the shared toggle.
        if (_currentTrack is null)
        {
            SetStatus(Strings.StatusFavoriteNoTrack);
            return;
        }

        await ToggleFavoriteAsync(_currentTrack);
    }

    private async void BarFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrack is null)
        {
            SetStatus(Strings.StatusFavoriteNoTrack);
            return;
        }

        await ToggleFavoriteAsync(_currentTrack);
    }

    /// <summary>
    /// Flips the favorite flag of a track, saves it, and refreshes every star
    /// that may be showing that track.
    /// </summary>
    private async Task ToggleFavoriteAsync(Track track)
    {
        track.ToggleFavorite();

        try
        {
            await TrackDataStore.PersistFavoriteAsync(track);
        }
        catch
        {
            SetStatus(Strings.StatusFavoriteSaveFailed);
        }

        SetStatus(track.IsFavorite
            ? Strings.StatusFavorited(track.Title)
            : Strings.StatusUnfavorited(track.Title));

        UpdateTrackFavoriteButtons(track);
        ApplyFavoriteStateToFavoritesPage(track);
    }

    /// <summary>
    /// Applies the current favorite state to the player page star and the player
    /// bar star, and reloads the favorites page when it is on screen.
    /// </summary>
    private void UpdateTrackFavoriteButtons(Track? track)
    {
        var isFavorite = track?.IsFavorite == true;
        var glyph = isFavorite ? "\uE735" : "\uE734";
        var label = isFavorite ? Strings.Unfavorite : Strings.Favorite;

        FavoriteIcon.Glyph = glyph;
        BarFavoriteIcon.Glyph = glyph;

        ToolTipService.SetToolTip(FavoriteButton, label);
        AutomationProperties.SetName(FavoriteButton, label);
        ToolTipService.SetToolTip(BarFavoriteButton, label);
        AutomationProperties.SetName(BarFavoriteButton, label);

        FavoriteButton.IsEnabled = track is not null;
        BarFavoriteButton.IsEnabled = track is not null;

        // Update the favorites list in place. Rebuilding it here used to reload
        // every record and reset the list on each track change.
        if (FavoritesPage.Visibility == Visibility.Visible && track is not null)
        {
            ApplyFavoriteStateToFavoritesPage(track);
        }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        // The "…" button opens the same menu as a right-click on the track.
        if (_currentTrack is null || sender is not FrameworkElement element)
        {
            SetStatus(Strings.StatusFavoriteNoTrack);
            return;
        }

        ShowTrackContextFlyout(_currentTrack, element, new Point(0, 0));
    }

    private void FullPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        PreviousButton_Click(sender, e);
    }

    private void FullPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        PlayPauseButton_Click(sender, e);
    }

    private void FullNextButton_Click(object sender, RoutedEventArgs e)
    {
        NextButton_Click(sender, e);
    }

    private void FullVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        VolumeButton_Click(sender, e);
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _repeatMode = _repeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.None
        };

        UpdateRepeatUi();
        SetStatus(Strings.RepeatState(_repeatMode));
    }

    private void UpdateRepeatUi()
    {
        var accent = _repeatMode == RepeatMode.None ? null : GetAccentBrush();
        var label = Strings.RepeatState(_repeatMode);
        var tooltip = Strings.RepeatTooltip(_repeatMode);

        RepeatIcon.Foreground = accent ?? _repeatIdleForeground;
        FullRepeatIcon.Foreground = accent ?? _fullRepeatIdleForeground;

        ToolTipService.SetToolTip(RepeatButton, tooltip);
        ToolTipService.SetToolTip(FullRepeatButton, tooltip);
        AutomationProperties.SetName(RepeatButton, label);
        AutomationProperties.SetName(FullRepeatButton, label);
    }

    private SolidColorBrush? GetAccentBrush()
    {
        if (_accentBrush is not null)
        {
            return _accentBrush;
        }

        try
        {
            _accentBrush = new SolidColorBrush(new UISettings().GetColorValue(UIColorType.Accent));
        }
        catch
        {
            // Fall back to the normal foreground when the system accent is unavailable.
        }

        return _accentBrush;
    }

    /// <summary>
    /// The secondary text colour used by the transport controls. Resolved from
    /// app resources because the theme resource lookup is not available from a
    /// detached element and can silently return no brush.
    /// </summary>
    private static SolidColorBrush GetSecondaryTextBrush()
    {
        if (Application.Current.Resources is { } resources &&
            resources.TryGetValue("TextFillColorSecondaryBrush", out var value) &&
            value is SolidColorBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrack is null)
        {
            return;
        }

        if (_player.PlaybackSession.Position > TimeSpan.FromSeconds(3))
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            return;
        }

        var index = IndexInPlaybackScope(_currentTrack);
        var previous = index > 0 ? FindPlayable(index - 1, -1) : null;
        if (previous is null && _repeatMode == RepeatMode.All)
        {
            previous = FindPlayable(PlaybackScopeTracks().Count - 1, -1);
        }

        if (previous is null)
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            SetStatus(Strings.StatusFirstInQueue);
            return;
        }

        if (ReferenceEquals(previous, _currentTrack))
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            _player.Play();
            return;
        }

        LoadTrack(previous, autoPlay: true);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTrack is null)
        {
            var firstPlayable = FindPlayable(0, 1);
            if (firstPlayable is not null)
            {
                LoadTrack(firstPlayable, autoPlay: true);
            }
            else if (_queue.Count > 0)
            {
                SetStatus(Strings.StatusNoPlayable);
            }
            else
            {
                SetStatus(Strings.StatusQueueEmpty);
            }

            return;
        }

        var index = IndexInPlaybackScope(_currentTrack);
        var next = index >= 0 ? FindPlayable(index + 1, 1) : null;
        if (next is null && _repeatMode == RepeatMode.All)
        {
            next = FindPlayable(0, 1);
        }

        if (next is not null)
        {
            LoadTrack(next, autoPlay: true);
        }
        else
        {
            SetStatus(Strings.StatusLastInQueue);
        }
    }

    private async void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (QueueList.SelectedItem is Track track)
        {
            await PlayTrackInScopeAsync(track, PlaybackScope.Queue);
        }
    }

    private async void RecentList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RecentList.SelectedItem is not Track track)
        {
            return;
        }

        if (!File.Exists(track.Path))
        {
            track.MarkMissing();
            RefreshRecentUi();
            await ShowMissingTracksDialogAsync([track]);
            return;
        }

        // Recently played is a history, not a queue: the picked track plays on
        // its own and stops when it ends unless repeat is turned on.
        await PlayTrackInScopeAsync(track, PlaybackScope.Single);
    }

    private void RecentList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not Track track)
        {
            return;
        }

        RecentList.SelectedItem = track;
        ShowTrackContextFlyout(track, RecentList, e.GetPosition(RecentList));
        e.Handled = true;
    }

    private Task PlayTrackFromUiAsync(Track track)
    {
        return PlayTrackInScopeAsync(track, GetPlaybackScopeFor(track));
    }

    /// <summary>
    /// The list a track should be played from: the custom playlist it was
    /// opened from, otherwise the default queue.
    /// </summary>
    private PlaybackScope GetPlaybackScopeFor(Track track)
    {
        return IsTrackInSelectedCustomPlaylist(track)
            ? PlaybackScope.CustomPlaylist
            : PlaybackScope.Queue;
    }

    private bool IsTrackInSelectedCustomPlaylist(Track track)
    {
        return _selectedCustomPlaylist is not null && _customPlaylistTracks.Any(candidate =>
            ReferenceEquals(candidate, track) ||
            string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Plays a track from the given list. A track that is not part of that
    /// list is not inserted into it; playback simply follows the list once the
    /// track itself finishes.
    /// </summary>
    private async Task PlayTrackInScopeAsync(Track track, PlaybackScope scope)
    {
        if (scope != PlaybackScope.Single)
        {
            // Prefer the instance that already lives in that list so row state
            // (current/favorite/missing) stays in sync with what is displayed.
            var listTrack = scope == PlaybackScope.CustomPlaylist
                ? _customPlaylistTracks.FirstOrDefault(candidate =>
                    string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase))
                : _queue.FirstOrDefault(candidate =>
                    string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase));
            if (listTrack is not null)
            {
                track = listTrack;
            }
        }
        else if (_currentTrack is not null &&
                 string.Equals(_currentTrack.Path, track.Path, StringComparison.OrdinalIgnoreCase))
        {
            // Replaying the single track that is already loaded keeps its state.
            track = _currentTrack;
        }

        if (track.IsMissing)
        {
            if (!File.Exists(track.Path))
            {
                await ShowMissingTracksDialogAsync([track]);
                return;
            }

            track.MarkAvailable();
            RefreshQueueUi();
            PersistPlaylist();
        }

        if (track.File is null && !File.Exists(track.Path))
        {
            track.MarkMissing();
            RefreshQueueUi();
            PersistPlaylist();
            await ShowMissingTracksDialogAsync([track]);
            return;
        }

        SetPlaybackScope(scope);
        LoadTrack(track, autoPlay: true);
    }

    private void QueueList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not Track track)
        {
            return;
        }

        QueueList.SelectedItem = track;
        ShowTrackContextFlyout(track, QueueList, e.GetPosition(QueueList));
        e.Handled = true;
    }

    /// <summary>
    /// A right-click anywhere on the player bar opens the track menu for the
    /// track that is currently loaded. Pointer events are used because the
    /// bar's buttons and sliders handle their own right-clicks.
    /// </summary>
    private void PlayerBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_currentTrack is null ||
            !e.GetCurrentPoint(PlayerBar).Properties.IsRightButtonPressed)
        {
            return;
        }

        ShowTrackContextFlyout(
            _currentTrack,
            PlayerBar,
            e.GetCurrentPoint(PlayerBar).Position);
        e.Handled = true;
    }

    /// <summary>
    /// The full player page offers the same menu on its transport area.
    /// </summary>
    private void PlayerPage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_currentTrack is null ||
            !e.GetCurrentPoint(PlayerPage).Properties.IsRightButtonPressed)
        {
            return;
        }

        ShowTrackContextFlyout(
            _currentTrack,
            PlayerPage,
            e.GetCurrentPoint(PlayerPage).Position);
        e.Handled = true;
    }

    private void ShowTrackContextFlyout(
        Track track,
        FrameworkElement target,
        Point position,
        TrackMenuContext context = TrackMenuContext.Playlist)
    {
        var flyout = new MenuFlyout();

        var playItem = new MenuFlyoutItem
        {
            Text = Strings.Play,
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playItem.Click += async (_, _) => await PlayTrackInScopeAsync(track, GetPlaybackScopeFor(track));
        flyout.Items.Add(playItem);

        var favoriteItem = new MenuFlyoutItem
        {
            Text = track.IsFavorite ? Strings.Unfavorite : Strings.Favorite,
            Icon = new FontIcon { Glyph = track.FavoriteGlyph }
        };
        favoriteItem.Click += async (_, _) => await ToggleFavoriteAsync(track);
        flyout.Items.Add(favoriteItem);

        // On the favorites page "remove" means unstar the song; in a playlist
        // it means take the entry out of that list.
        var removeItem = new MenuFlyoutItem
        {
            Text = context == TrackMenuContext.Favorites
                ? Strings.RemoveFromFavorites
                : Strings.Remove,
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        removeItem.Click += async (_, _) =>
        {
            if (context == TrackMenuContext.Favorites)
            {
                if (track.IsFavorite)
                {
                    await ToggleFavoriteAsync(track);
                }
            }
            else
            {
                RemoveTrack(track);
            }
        };
        flyout.Items.Add(removeItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Add to an existing custom playlist: a submenu listing every playlist.
        var addToExisting = new MenuFlyoutSubItem
        {
            Text = Strings.AddToExistingPlaylist,
            Icon = new FontIcon { Glyph = "\uE8FD" }
        };
        foreach (var playlist in _customPlaylists)
        {
            var item = new MenuFlyoutItem { Text = playlist.Name };
            item.Click += (_, _) => AddTrackToPlaylist(playlist, track);
            addToExisting.Items.Add(item);
        }

        if (addToExisting.Items.Count == 0)
        {
            addToExisting.IsEnabled = false;
        }

        flyout.Items.Add(addToExisting);

        var addToNew = new MenuFlyoutItem
        {
            Text = Strings.AddToNewPlaylist,
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        addToNew.Click += (_, _) => AddTrackToNewPlaylist(track);
        flyout.Items.Add(addToNew);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var detailsItem = new MenuFlyoutItem
        {
            Text = Strings.ViewDetails,
            Icon = new FontIcon { Glyph = "\uE946" }
        };
        detailsItem.Click += async (_, _) => await ShowTrackDetailsAsync(track);
        flyout.Items.Add(detailsItem);

        flyout.ShowAt(target, new FlyoutShowOptions { Position = position });
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoveButton is not null)
        {
            RemoveButton.IsEnabled = QueueList.SelectedItem is Track;
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is Track track)
        {
            RemoveTrack(track);
        }
    }

    private void RemoveTrack(Track track)
    {
        if (!_queue.Contains(track))
        {
            return;
        }

        var index = _queue.IndexOf(track);
        var wasCurrent = ReferenceEquals(track, _currentTrack);
        var wasPlaying = IsPlaying;

        if (wasCurrent)
        {
            _player.Pause();
            _player.Source = null;
            _fallbackStream?.Dispose();
            _fallbackStream = null;
            _pendingAutoPlay = false;
            track.IsCurrent = false;
            track.IsPlaying = false;
            _currentTrack = null;
        }

        _queue.Remove(track);
        _knownPaths.Remove(track.Path);

        if (wasCurrent)
        {
            // The default queue is the only list this page can remove from.
            _playbackScope = PlaybackScope.Queue;

            if (_queue.Count == 0)
            {
                ResetNowPlaying();
            }
            else
            {
                var next = FindPlayable(Math.Min(index, _queue.Count - 1), 1)
                    ?? FindPlayable(Math.Min(index, _queue.Count - 1) - 1, -1);

                if (next is null)
                {
                    ResetNowPlaying();
                }
                else
                {
                    LoadTrack(next, autoPlay: wasPlaying);
                }
            }
        }

        RefreshQueueUi();
        PersistPlaylist();
        SetStatus(Strings.StatusRemoved(track.Title));
    }

    private async Task ShowTrackDetailsAsync(Track track)
    {
        await EnsureWindowLoadedAsync();

        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var file = TryGetFileInfo(track.Path);
        var grid = new Grid
        {
            ColumnSpacing = 20,
            RowSpacing = 2,
            MinWidth = 430
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddDetailRow(grid, Strings.FileFormat, FormatFileType(track.FileType));
        AddDetailRow(grid, Strings.Bitrate, FormatBitrate(track, file));
        AddDetailRow(grid, Strings.FileLocation, track.Path);
        AddDetailRow(grid, Strings.FileSize, file is null ? Strings.FileNotFound : FormatFileSize(file.Length));
        AddDetailRow(grid, Strings.Album, string.IsNullOrWhiteSpace(track.Album) ? Strings.Unknown : track.Album);
        AddDetailRow(grid, Strings.AlbumArtist, string.IsNullOrWhiteSpace(track.AlbumArtist) ? Strings.Unknown : track.AlbumArtist);
        AddDetailRow(grid, Strings.Title, track.Title);
        AddDetailRow(grid, Strings.Duration, track.Duration > TimeSpan.Zero ? track.DurationText : Strings.Unknown);
        AddDetailRow(grid, Strings.AlbumCover, BuildCover(track));

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid
        };

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.Details,
            Content = scrollViewer,
            CloseButtonText = Strings.Close,
            DefaultButton = ContentDialogButton.Close
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // The dialog can fail to open if another modal is already active.
        }
    }

    private static void AddDetailRow(Grid grid, string label, object value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(0, 6, 0, 6),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        FrameworkElement valueElement;
        if (value is FrameworkElement customElement)
        {
            valueElement = customElement;
        }
        else
        {
            valueElement = new TextBlock
            {
                Text = value?.ToString() ?? Strings.Unknown,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        valueElement.Margin = new Thickness(0, 6, 0, 6);
        Grid.SetRow(valueElement, row);
        Grid.SetColumn(valueElement, 1);
        grid.Children.Add(valueElement);
    }

    private static FrameworkElement BuildCover(Track track)
    {
        if (track.Artwork is null)
        {
            var placeholder = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            placeholder.Children.Add(new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 42,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            placeholder.Children.Add(new TextBlock
            {
                Text = Strings.NoCover,
                FontSize = 12,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return new Border
            {
                Width = 220,
                Height = 220,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                Child = placeholder
            };
        }

        return new Border
        {
            Width = 220,
            Height = 220,
            CornerRadius = new CornerRadius(12),
            Child = new Image
            {
                Source = track.Artwork,
                Stretch = Stretch.UniformToFill
            }
        };
    }

    private static FileInfo? TryGetFileInfo(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFileType(string fileType)
    {
        var type = fileType.TrimStart('.');
        return string.IsNullOrWhiteSpace(type) ? Strings.Unknown : type.ToUpperInvariant();
    }

    private static string FormatBitrate(Track track, FileInfo? file)
    {
        var bitrate = track.Bitrate;

        if (bitrate == 0 &&
            file is not null &&
            track.Duration > TimeSpan.Zero)
        {
            bitrate = (uint)Math.Round(file.Length * 8d / track.Duration.TotalSeconds);
        }

        return bitrate > 0
            ? $"{Math.Round(bitrate / 1000d):0} kbps"
            : Strings.Unknown;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.#} MB";
        }

        return $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var count = _queue.Count;
        _player.Pause();
        _player.Source = null;
        _fallbackStream?.Dispose();
        _fallbackStream = null;
        _pendingAutoPlay = false;

        foreach (var track in _queue)
        {
            track.IsCurrent = false;
            track.IsPlaying = false;
        }

        _queue.Clear();
        _knownPaths.Clear();
        _currentTrack = null;
        _playbackScope = PlaybackScope.Queue;

        ResetNowPlaying();
        RefreshQueueUi();
        PersistPlaylist();
        SetStatus(Strings.StatusQueueCleared(count));
    }

    private void RefreshQueueUi()
    {
        var missingCount = _queue.Count(track => track.IsMissing);
        QueueCountText.Text = Strings.TrackCount(_queue.Count, missingCount);
        QueueList.Visibility = _queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyQueuePanel.Visibility = _queue.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = _queue.Count > 0;
        RefreshVisibleQueue();
        RemoveButton.IsEnabled = QueueList.SelectedItem is Track;
        RefreshPlaybackScope();
    }

    private void UpdateNowPlaying(Track track)
    {
        var displayTitle = $"{track.Title} - {track.ArtistDisplay}";
        HeroTitle.Text = displayTitle;
        HeroAlbum.Text = track.AlbumDisplay;
        FullTitle.Text = displayTitle;
        FullAlbum.Text = track.AlbumDisplay;
        ToolTipService.SetToolTip(HeroTitle, track.Title);
        ToolTipService.SetToolTip(HeroAlbum, track.AlbumDisplay);
        ToolTipService.SetToolTip(FullTitle, track.Title);
        ToolTipService.SetToolTip(FullAlbum, track.AlbumDisplay);

        HeroArt.Source = track.Artwork;
        HeroArt.Visibility = track.Artwork is null ? Visibility.Collapsed : Visibility.Visible;
        HeroArtPlaceholder.Visibility = track.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        FullArt.Source = track.Artwork;
        FullArt.Visibility = track.Artwork is null ? Visibility.Collapsed : Visibility.Visible;
        FullArtPlaceholder.Visibility = track.Artwork is null ? Visibility.Visible : Visibility.Collapsed;

        UpdateTrackFavoriteButtons(track);
        RefreshLyrics(track);

        SetProgressMaximum(track.Duration.TotalSeconds);
        UpdateTimeLabels(TimeSpan.Zero, track.Duration);
        _ = UpdateSystemMediaInfoAsync(track);
        _ = _dwmThumbnail?.SetArtworkAsync(track.ArtworkBytes);

    }

    private void ResetNowPlaying()
    {
        _currentTrack = null;
        QueueList.SelectedItem = null;
        NowStateText.Text = Strings.Ready;
        HeroTitle.Text = Strings.NotPlaying;
        HeroAlbum.Text = Strings.PickSongHint;
        FullTitle.Text = Strings.NotPlaying;
        FullAlbum.Text = Strings.PickSongHint;
        HeroArt.Source = null;
        HeroArt.Visibility = Visibility.Collapsed;
        HeroArtPlaceholder.Visibility = Visibility.Visible;
        FullArt.Source = null;
        FullArt.Visibility = Visibility.Collapsed;
        FullArtPlaceholder.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(HeroTitle, null);
        ToolTipService.SetToolTip(HeroAlbum, null);
        ToolTipService.SetToolTip(FullTitle, null);
        ToolTipService.SetToolTip(FullAlbum, null);

        _isUpdatingProgress = true;
        ProgressSlider.Maximum = 1;
        ProgressSlider.Value = 0;
        FullProgressSlider.Maximum = 1;
        FullProgressSlider.Value = 0;
        _isUpdatingProgress = false;

        CurrentTimeText.Text = "0:00";
        TotalTimeText.Text = "0:00";
        FullCurrentTimeText.Text = "0:00";
        FullTotalTimeText.Text = "0:00";
        PlayPauseIcon.Glyph = "\uE768";
        FullPlayPauseIcon.Glyph = "\uE768";
        UpdateTrackFavoriteButtons(null);
        RefreshLyrics(null);
        UpdateTransportUi();
        NowStateText.Text = Strings.Ready;
        _ = UpdateSystemMediaInfoAsync(null);
        _dwmThumbnail?.Clear();

    }

    private void UpdateTransportUi()
    {
        var hasTrack = _currentTrack is not null;
        var playing = hasTrack && IsPlaying;
        var canSeek = hasTrack && _player.PlaybackSession.NaturalDuration > TimeSpan.Zero;

        PlayPauseButton.IsEnabled = hasTrack;
        PreviousButton.IsEnabled = hasTrack;
        NextButton.IsEnabled = hasTrack;
        ProgressSlider.IsEnabled = canSeek;
        FullPlayPauseButton.IsEnabled = hasTrack;
        FullPreviousButton.IsEnabled = hasTrack;
        FullNextButton.IsEnabled = hasTrack;
        FullProgressSlider.IsEnabled = canSeek;
        PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
        FullPlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
        ToolTipService.SetToolTip(PlayPauseButton, playing ? Strings.Pause : Strings.Play);
        ToolTipService.SetToolTip(FullPlayPauseButton, playing ? Strings.Pause : Strings.Play);
        _taskbarService?.SetPlaying(playing);

        if (_systemMediaControls is not null)
        {
            _systemMediaControls.PlaybackStatus = !hasTrack
                ? MediaPlaybackStatus.Closed
                : playing
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
        }

        if (_currentTrack is not null)
        {
            _currentTrack.IsPlaying = playing;
        }

        if (!hasTrack)
        {
            NowStateText.Text = Strings.Ready;
        }
        else if (playing)
        {
            NowStateText.Text = Strings.Playing;
        }
        else if (NowStateText.Text is not (LoadingState or EndedState or LoadedState))
        {
            NowStateText.Text = Strings.Paused;
        }
    }

    // Stable state names used to tell "paused" apart from "loading/ended/loaded"
    // without comparing localized text.
    private const string LoadingState = "\u0001loading";
    private const string EndedState = "\u0001ended";
    private const string LoadedState = "\u0001loaded";

    private void Player_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_currentTrack is null)
            {
                return;
            }

            var duration = sender.PlaybackSession.NaturalDuration;
            if (duration > TimeSpan.Zero)
            {
                _currentTrack.SetDuration(duration);
                SetProgressMaximum(duration.TotalSeconds);
                TotalTimeText.Text = FormatClock(duration);
                FullTotalTimeText.Text = FormatClock(duration);
            }

            _ = UpdateSystemMediaInfoAsync(_currentTrack);

            if (_pendingAutoPlay)
            {
                _pendingAutoPlay = false;
                sender.Play();
            }

            UpdateTransportUi();

            if (!IsPlaying)
            {
                NowStateText.Text = LoadedState;
            }
        });
    }

    private void Player_MediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(AdvanceAfterEnd);
    }

    private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var message = string.IsNullOrWhiteSpace(args.ErrorMessage)
                ? Strings.UnsupportedFormat
                : args.ErrorMessage;
            SetStatus(Strings.StatusPlaybackFailed(message));

            if (_currentTrack is not null)
            {
                _currentTrack.IsPlaying = false;
            }

            UpdateTransportUi();
            NowStateText.Text = Strings.PlaybackFailed;
        });
    }

    private void Player_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTransportUi();

            if (sender.PlaybackState == MediaPlaybackState.Playing && _currentTrack is not null)
            {
                AddRecent(_currentTrack);
                SetStatus(Strings.StatusNowPlaying(_currentTrack.Title));
            }
        });
    }

    private void AdvanceAfterEnd()
    {
        if (_currentTrack is null)
        {
            return;
        }

        if (_repeatMode == RepeatMode.One)
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            LoadTrack(_currentTrack, autoPlay: true);
            return;
        }

        var index = IndexInPlaybackScope(_currentTrack);
        var next = index >= 0 ? FindPlayable(index + 1, 1) : null;
        if (next is null && _repeatMode == RepeatMode.All)
        {
            next = FindPlayable(0, 1);
        }

        if (next is not null)
        {
            if (ReferenceEquals(next, _currentTrack))
            {
                _player.PlaybackSession.Position = TimeSpan.Zero;
            }

            LoadTrack(next, autoPlay: true);
            return;
        }

        _pendingAutoPlay = false;
        UpdateTransportUi();
        NowStateText.Text = EndedState;
        SetStatus(Strings.StatusQueueFinished);

        _isUpdatingProgress = true;
        ProgressSlider.Value = ProgressSlider.Maximum;
        FullProgressSlider.Value = FullProgressSlider.Maximum;
        _isUpdatingProgress = false;
        CurrentTimeText.Text = TotalTimeText.Text;
        FullCurrentTimeText.Text = FullTotalTimeText.Text;
    }

    private void PositionTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_currentTrack is null || _player.Source is null)
        {
            return;
        }

        var session = _player.PlaybackSession;
        var duration = session.NaturalDuration;

        if (duration > TimeSpan.Zero &&
            Math.Abs(ProgressSlider.Maximum - duration.TotalSeconds) > 0.2)
        {
            SetProgressMaximum(duration.TotalSeconds);
        }

        if (_isSeeking)
        {
            return;
        }

        if (_isUpdatingProgress)
        {
            return;
        }

        _isUpdatingProgress = true;
        ProgressSlider.Value = Math.Clamp(session.Position.TotalSeconds, 0, ProgressSlider.Maximum);
        FullProgressSlider.Value = Math.Clamp(session.Position.TotalSeconds, 0, FullProgressSlider.Maximum);
        _isUpdatingProgress = false;

        CurrentTimeText.Text = FormatClock(session.Position);
        FullCurrentTimeText.Text = FormatClock(session.Position);

        if (duration > TimeSpan.Zero)
        {
            TotalTimeText.Text = FormatClock(duration);
            FullTotalTimeText.Text = FormatClock(duration);
        }
    }

    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyProgressValue(e.NewValue, sender as Slider);
    }

    private void FullProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyProgressValue(e.NewValue, sender as Slider);
    }

    private void ApplyProgressValue(double value, Slider? source)
    {
        if (_isUpdatingProgress ||
            _currentTrack is null ||
            _player.Source is null ||
            ProgressSlider is null ||
            FullProgressSlider is null)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Max(0, value));
        _player.PlaybackSession.Position = position;

        _isUpdatingProgress = true;
        if (source != ProgressSlider)
        {
            ProgressSlider.Value = value;
        }

        if (source != FullProgressSlider)
        {
            FullProgressSlider.Value = value;
        }

        _isUpdatingProgress = false;

        CurrentTimeText.Text = FormatClock(position);
        FullCurrentTimeText.Text = FormatClock(position);
    }

    private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        SeekToSliderPosition(sender as Slider ?? ProgressSlider);
        _isSeeking = false;
    }

    private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
    }

    private void SeekToSliderPosition(Slider slider)
    {
        if (_currentTrack is null || _player.Source is null)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(slider.Value);
        _player.PlaybackSession.Position = position;
        CurrentTimeText.Text = FormatClock(position);
        FullCurrentTimeText.Text = FormatClock(position);
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMuted)
        {
            Unmute();
        }
        else
        {
            Mute();
        }
    }

    private void Mute()
    {
        _volumeBeforeMute = VolumeSlider.Value > 0
            ? Math.Round(VolumeSlider.Value / 5d) * 5d
            : 65;
        _isMuted = true;
        _player.IsMuted = true;
        UpdateVolumeIcon();
        SetStatus(Strings.StatusMuted);
    }

    private void Unmute()
    {
        _isMuted = false;
        _player.IsMuted = false;

        if (VolumeSlider.Value <= 0.5)
        {
            VolumeSlider.Value = Math.Clamp(
                Math.Round(_volumeBeforeMute / 5d) * 5d,
                5,
                100);
        }

        UpdateVolumeIcon();
        SetStatus(Strings.StatusUnmuted);
    }

    private void UpdateVolumeIcon()
    {
        if (VolumeButton is null ||
            VolumeIcon is null ||
            FullVolumeButton is null ||
            FullVolumeIcon is null ||
            VolumeValueText is null)
        {
            return;
        }

        var muted = _isMuted || VolumeSlider.Value <= 0.5;
        var glyph = muted ? "\uE74F" : "\uE767";
        VolumeIcon.Glyph = glyph;
        FullVolumeIcon.Glyph = glyph;

        var volume = Math.Round(VolumeSlider.Value / 5d) * 5d;
        VolumeValueText.Text = $"{volume:0}";
        AutomationProperties.SetName(VolumeValueText, $"{Strings.CurrentVolume}: {volume:0}");

        var label = _isMuted ? Strings.Unmute : Strings.Mute;
        ToolTipService.SetToolTip(VolumeButton, label);
        ToolTipService.SetToolTip(FullVolumeButton, label);
        AutomationProperties.SetName(VolumeButton, label);
        AutomationProperties.SetName(FullVolumeButton, label);
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyVolumeValue(e.NewValue, sender as Slider);
    }

    private void FullVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyVolumeValue(e.NewValue, sender as Slider);
    }

    private void ApplyVolumeValue(double value, Slider? source)
    {
        if (_isUpdatingVolume || VolumeSlider is null || FullVolumeSlider is null)
        {
            return;
        }

        var snapped = Math.Clamp(Math.Round(value / 5d) * 5d, 0, 100);

        _isUpdatingVolume = true;
        if (source != VolumeSlider)
        {
            VolumeSlider.Value = snapped;
        }

        if (source != FullVolumeSlider)
        {
            FullVolumeSlider.Value = snapped;
        }

        _isUpdatingVolume = false;

        _player.Volume = snapped / 100d;

        if (VolumeButton is not null && snapped > 0.5 && _isMuted)
        {
            _isMuted = false;
            _player.IsMuted = false;
        }

        UpdateVolumeIcon();
    }

    private void ImportAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ImportFilesAsync();
    }

    private void PlayPauseAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Typing a space in a search box or any other text field must reach that
        // field instead of pausing playback, so the shortcut steps aside.
        if (IsTextInputFocused())
        {
            args.Handled = false;
            return;
        }

        args.Handled = true;
        TogglePlayback();
    }

    /// <summary>
    /// True when the keyboard focus sits in a control that consumes typed
    /// characters (search boxes, playlist rename editors, combo box editors).
    /// </summary>
    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        if (focused is null)
        {
            return false;
        }

        for (var element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox or RichEditBox or PasswordBox or AutoSuggestBox)
            {
                return true;
            }
        }

        return focused is TextBox or RichEditBox or PasswordBox;
    }

    private void SetProgressMaximum(double seconds)
    {
        _isUpdatingProgress = true;
        var maximum = Math.Max(seconds, 1);
        ProgressSlider.Maximum = maximum;
        FullProgressSlider.Maximum = maximum;
        ProgressSlider.Value = Math.Clamp(ProgressSlider.Value, 0, maximum);
        FullProgressSlider.Value = Math.Clamp(FullProgressSlider.Value, 0, maximum);
        _isUpdatingProgress = false;
    }

    private void UpdateTimeLabels(TimeSpan current, TimeSpan duration)
    {
        CurrentTimeText.Text = FormatClock(current);
        TotalTimeText.Text = FormatClock(duration);
        FullCurrentTimeText.Text = FormatClock(current);
        FullTotalTimeText.Text = FormatClock(duration);
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        ToolTipService.SetToolTip(StatusText, message);
    }

    private void RestartIfAtEnd()
    {
        var session = _player.PlaybackSession;
        if (session.NaturalDuration > TimeSpan.Zero &&
            session.Position >= session.NaturalDuration - TimeSpan.FromMilliseconds(250))
        {
            session.Position = TimeSpan.Zero;
        }
    }

    private static string FormatClock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    private void ResizeAndCenter()
    {
        const int preferredWidth = 1200;
        const int preferredHeight = 780;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            AppWindow.Resize(new SizeInt32(preferredWidth, preferredHeight));
            return;
        }

        var workArea = displayArea.WorkArea;
        var width = Math.Min(preferredWidth, Math.Max(880, workArea.Width - 80));
        var height = Math.Min(preferredHeight, Math.Max(640, workArea.Height - 80));
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void ConfigureSystemMediaControls()
    {
        try
        {
            _systemMediaControls = _player.SystemMediaTransportControls;
            _player.CommandManager.IsEnabled = false;
            var controls = _systemMediaControls;
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsNextEnabled = true;
            controls.IsPreviousEnabled = true;
            controls.IsStopEnabled = false;
            controls.IsFastForwardEnabled = false;
            controls.IsRewindEnabled = false;
            controls.IsRecordEnabled = false;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            controls.ButtonPressed += SystemMediaControls_ButtonPressed;
        }
        catch
        {
            _systemMediaControls = null;
        }
    }

    private void SystemMediaControls_ButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                case SystemMediaTransportControlsButton.Pause:
                    TogglePlayback();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    PreviousButton_Click(this, new RoutedEventArgs());
                    break;
                case SystemMediaTransportControlsButton.Next:
                    NextButton_Click(this, new RoutedEventArgs());
                    break;
            }
        });
    }

    private async Task UpdateSystemMediaInfoAsync(Track? track)
    {
        var controls = _systemMediaControls;
        if (controls is null)
        {
            return;
        }

        try
        {
            if (track is null)
            {
                controls.DisplayUpdater.ClearAll();
                controls.DisplayUpdater.Update();
                controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                _systemMediaThumbnail?.Dispose();
                _systemMediaThumbnail = null;
                _systemMediaArtworkStream?.Dispose();
                _systemMediaArtworkStream = null;
                return;
            }

            controls.DisplayUpdater.ClearAll();
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            var music = controls.DisplayUpdater.MusicProperties;
            music.Title = track.Title;
            music.Artist = track.ArtistDisplay;
            music.AlbumTitle = track.AlbumDisplay;
            music.AlbumArtist = track.AlbumArtistDisplay;
            music.TrackNumber = 0;

            StorageItemThumbnail? thumbnail = null;
            InMemoryRandomAccessStream? artworkStream = null;

            if (track.ArtworkBytes is { Length: > 0 } artworkBytes)
            {
                artworkStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(artworkStream))
                {
                    writer.WriteBytes(artworkBytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }

                artworkStream.Seek(0);
                controls.DisplayUpdater.Thumbnail =
                    RandomAccessStreamReference.CreateFromStream(artworkStream);
            }
            else
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(track.Path);
                    thumbnail = await file.GetThumbnailAsync(
                        ThumbnailMode.MusicView,
                        512,
                        ThumbnailOptions.UseCurrentScale);
                }
                catch
                {
                    // Files without shell metadata fall back to no artwork.
                }

                controls.DisplayUpdater.Thumbnail = thumbnail is not null
                    ? RandomAccessStreamReference.CreateFromStream(thumbnail)
                    : null;
            }

            controls.DisplayUpdater.Update();
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = track.Duration,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = track.Duration,
                Position = _player.PlaybackSession.Position
            });

            _systemMediaThumbnail?.Dispose();
            _systemMediaThumbnail = thumbnail;
            _systemMediaArtworkStream?.Dispose();
            _systemMediaArtworkStream = artworkStream;
        }
        catch
        {
            // Media controls are an enhancement and must not interrupt playback.
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange)
        {
            return;
        }

        const int minimumWidth = 1120;
        const int minimumHeight = 720;
        var size = sender.Size;

        if (size.Width < minimumWidth || size.Height < minimumHeight)
        {
            sender.Resize(new SizeInt32(
                Math.Max(size.Width, minimumWidth),
                Math.Max(size.Height, minimumHeight)));
        }


    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_taskbarInitialized)
        {
            return;
        }

        _taskbarInitialized = true;
        try
        {
            var iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Taskbar",
                "app.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // The executable icon remains as a fallback.
        }

        await Task.Delay(1200);
        try
        {
            _taskbarService = new TaskbarService(
                WindowNative.GetWindowHandle(this),
                DispatcherQueue,
                TogglePlayback,
                () => PreviousButton_Click(this, new RoutedEventArgs()),
                () => NextButton_Click(this, new RoutedEventArgs()));
            if (!_taskbarService.Initialize())
            {
                _taskbarService.Dispose();
                _taskbarService = null;
            }
            else
            {
                _taskbarService.SetPlaying(IsPlaying);
            }
        }
        catch
        {
            _taskbarService = null;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_playlistReady)
        {
            PlaylistStore.SavePaths(_queue.Select(track => track.Path));
        }

        Activated -= MainWindow_Activated;
        _taskbarService?.Dispose();
        _taskbarService = null;
        _minSizeService?.Dispose();
        _minSizeService = null;

        if (_systemMediaControls is not null)
        {
            _systemMediaControls.ButtonPressed -= SystemMediaControls_ButtonPressed;
            _systemMediaControls.IsEnabled = false;
            _systemMediaControls = null;
        }

        _systemMediaThumbnail?.Dispose();
        _systemMediaThumbnail = null;
        _systemMediaArtworkStream?.Dispose();
        _systemMediaArtworkStream = null;
        _dwmThumbnail?.Dispose();
        _dwmThumbnail = null;
        _positionTimer.Stop();
        _fallbackStream?.Dispose();
        _fallbackStream = null;
        _player.Dispose();
    }
}

/// <summary>
/// The list a started track belongs to. Auto-advance and the transport
/// buttons follow this list instead of always following the default queue.
/// </summary>
public enum PlaybackScope
{
    /// <summary>The default playlist shown on the main page.</summary>
    Queue,

    /// <summary>The custom playlist currently open in the sidebar.</summary>
    CustomPlaylist,

    /// <summary>A single track started from Recently played or a search result.</summary>
    Single
}

/// <summary>
/// Which page opened a track's context menu; decides what "remove" means.
/// </summary>
public enum TrackMenuContext
{
    /// <summary>A playlist page: remove the entry from the default queue.</summary>
    Playlist,

    /// <summary>The favorites page: unstar the song instead of unlinking it.</summary>
    Favorites
}
