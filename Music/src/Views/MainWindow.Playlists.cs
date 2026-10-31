using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Music.Models;
using Music.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Music;

public sealed partial class MainWindow
{
    private SidebarPlaylistItem? _selectedCustomPlaylist;

    private void ConfigureAddPlaylistButton()
    {
        try
        {
            var accent = new UISettings().GetColorValue(UIColorType.Accent);
            AddPlaylistButton.Background = new SolidColorBrush(accent);
            AddPlaylistButton.Resources["ButtonBackgroundPointerOver"] =
                new SolidColorBrush(Darken(accent, 0.86));
            AddPlaylistButton.Resources["ButtonBackgroundPressed"] =
                new SolidColorBrush(Darken(accent, 0.74));
            AddPlaylistButton.Resources["ButtonBackgroundDisabled"] =
                new SolidColorBrush(accent);
            AddPlaylistButton.Resources["ButtonForegroundPointerOver"] =
                new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            AddPlaylistButton.Resources["ButtonForegroundPressed"] =
                new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        }
        catch
        {
            // Keep the themed accent style if the system color is unavailable.
        }

        static Color Darken(Color color, double factor)
        {
            return Color.FromArgb(
                color.A,
                (byte)Math.Clamp(color.R * factor, 0, 255),
                (byte)Math.Clamp(color.G * factor, 0, 255),
                (byte)Math.Clamp(color.B * factor, 0, 255));
        }
    }

    private void ConfigureCustomPlaylistDrop()
    {
        CustomPlaylistPage.AllowDrop = true;
        CustomPlaylistPage.AddHandler(
            UIElement.DragOverEvent,
            new DragEventHandler(CustomPlaylist_DragOver),
            true);
        CustomPlaylistPage.AddHandler(
            UIElement.DragLeaveEvent,
            new DragEventHandler(CustomPlaylist_DragLeave),
            true);
        CustomPlaylistPage.AddHandler(
            UIElement.DropEvent,
            new DragEventHandler(CustomPlaylist_Drop),
            true);
    }

    private void CustomPlaylist_DragOver(object sender, DragEventArgs e)
    {
        if (CustomPlaylistPage.Visibility != Visibility.Visible ||
            _selectedCustomPlaylist is null ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.AddToPlaylistCaption;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        CustomPlaylistDropOverlay.Visibility = Visibility.Visible;
    }

    private void CustomPlaylist_DragLeave(object sender, DragEventArgs e)
    {
        CustomPlaylistDropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void CustomPlaylist_Drop(object sender, DragEventArgs e)
    {
        CustomPlaylistDropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (_selectedCustomPlaylist is null ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
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

        var playlist = _selectedCustomPlaylist;
        foreach (var file in files)
        {
            if (playlist.Paths.All(path =>
                    !string.Equals(path, file.Path, StringComparison.OrdinalIgnoreCase)))
            {
                playlist.Paths.Add(file.Path);
            }
        }

        PersistCustomPlaylists();
        await LoadCustomPlaylistTracksAsync(playlist);
        SetStatus(Strings.StatusAddedToPlaylist(playlist.Name));
    }

    private void LoadCustomPlaylists()
    {
        foreach (var entry in PlaylistCatalogStore.Load())
        {
            if (string.IsNullOrWhiteSpace(entry.Id) ||
                _customPlaylists.Any(item => item.Id == entry.Id))
            {
                continue;
            }

            AddCustomPlaylistItem(
                entry.Id,
                string.IsNullOrWhiteSpace(entry.Name) ? Strings.Playlist : entry.Name,
                select: false,
                beginEditing: false,
                paths: entry.Paths);
        }
    }

    private SidebarPlaylistItem AddCustomPlaylistItem(
        string id,
        string name,
        bool select,
        bool beginEditing,
        bool freshName = false,
        IEnumerable<string>? paths = null)
    {
        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE8FD",
            FontSize = 14
        });
        content.Children.Add(label);

        var button = new Button
        {
            Style = RootGrid.Resources["SidebarPlaylistButtonStyle"] as Style,
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(button, name);

        var editor = new TextBox
        {
            Height = 30,
            Margin = new Thickness(25, 0, 8, 0),
            Padding = new Thickness(0, 6, 4, 6),
            FontSize = 12,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            PlaceholderText = Strings.PlaylistNameHint,
            Visibility = Visibility.Collapsed
        };

        var root = new Grid
        {
            Height = 34,
            Margin = new Thickness(0, 1, 0, 0)
        };
        root.Children.Add(button);
        root.Children.Add(editor);

        var item = new SidebarPlaylistItem
        {
            Id = id,
            Name = name,
            Root = root,
            Button = button,
            Label = label,
            Editor = editor
        };
        item.Paths.AddRange(paths ?? []);

        button.Click += (_, _) =>
        {
            if (!item.IsEditing)
            {
                SelectCustomPlaylist(item);
            }
        };
        button.RightTapped += (_, args) =>
        {
            args.Handled = true;
            ShowPlaylistMenu(item, args.GetPosition(button));
        };
        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                CommitPlaylistRename(item);
                args.Handled = true;
            }
            else if (args.Key == Windows.System.VirtualKey.Escape)
            {
                CancelPlaylistRename(item);
                args.Handled = true;
            }
        };
        editor.LostFocus += (_, _) =>
        {
            if (item.IsEditing)
            {
                CommitPlaylistRename(item);
            }
        };

        CustomPlaylistsPanel.Children.Add(root);
        _customPlaylists.Add(item);

        if (select)
        {
            SelectCustomPlaylist(item);
        }

        if (beginEditing)
        {
            BeginPlaylistRename(item, freshName);
        }

        return item;
    }

    private void AddPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        AddCustomPlaylistItem(
            Guid.NewGuid().ToString("N"),
            Strings.NewPlaylist,
            select: true,
            beginEditing: true,
            freshName: true);
        PersistCustomPlaylists();
    }

    private async void SelectCustomPlaylist(SidebarPlaylistItem item)
    {
        ResetSidebarSearch();

        if (_selectedNavButton is not null && _selectedNavButton != item.Button)
        {
            _selectedNavButton.Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        _selectedNavButton = item.Button;
        _selectedCustomPlaylist = item;
        item.Button.Background = CreateNavSelectionBrush();

        MainWorkspace.Visibility = Visibility.Collapsed;
        RecentPage.Visibility = Visibility.Collapsed;
        CustomPlaylistPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        CustomPlaylistTitleText.Text = item.Name;
        CustomSearchBox.Text = string.Empty;
        CustomSearchBox.Visibility = Visibility.Collapsed;
        CustomSearchButton.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));

        await LoadCustomPlaylistTracksAsync(item);
    }

    private async Task LoadCustomPlaylistTracksAsync(SidebarPlaylistItem item)
    {
        var version = ++_customPlaylistLoadVersion;
        _customPlaylistTracks.Clear();
        RefreshCustomPlaylistUi();

        foreach (var path in item.Paths
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Track? track = _queue.FirstOrDefault(candidate =>
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (track is null)
            {
                try
                {
                    track = File.Exists(path)
                        ? await MusicLibrary.LoadAsync(path)
                        : new Track(path, isMissing: true);
                }
                catch
                {
                    track = new Track(path, isMissing: true);
                }
            }

            if (version != _customPlaylistLoadVersion ||
                !ReferenceEquals(_selectedCustomPlaylist, item))
            {
                return;
            }

            if (!File.Exists(path))
            {
                track.MarkMissing();
            }

            if (_customPlaylistTracks.All(candidate =>
                    !string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _customPlaylistTracks.Add(track);
            }
        }

        if (version == _customPlaylistLoadVersion)
        {
            RefreshCustomPlaylistUi();
        }
    }

    private void RefreshCustomPlaylistUi()
    {
        var selected = CustomPlaylistList.SelectedItem as Track;
        var query = CustomSearchBox?.Text?.Trim();
        var searching = !string.IsNullOrWhiteSpace(query);

        IEnumerable<Track> ordered = _customSortMode switch
        {
            CustomSortMode.Name => _customPlaylistTracks
                .OrderBy(track => track.QueueTitle, StringComparer.CurrentCultureIgnoreCase),
            CustomSortMode.Duration => _customPlaylistTracks.OrderBy(track => track.Duration),
            _ => _customPlaylistTracks
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

        _visibleCustomPlaylistTracks.Clear();
        foreach (var track in ordered)
        {
            _visibleCustomPlaylistTracks.Add(track);
        }

        CustomPlaylistList.SelectedItem = selected is not null &&
            _visibleCustomPlaylistTracks.Contains(selected)
            ? selected
            : null;
        CustomPlaylistList.Visibility = _visibleCustomPlaylistTracks.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomPlaylistEmptyPanel.Visibility = _customPlaylistTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomPlaylistNoResultsPanel.Visibility = searching &&
            _customPlaylistTracks.Count > 0 &&
            _visibleCustomPlaylistTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomPlaylistCountText.Text = searching
            ? Strings.MatchCount(_visibleCustomPlaylistTracks.Count)
            : Strings.TrackCount(_customPlaylistTracks.Count, 0);
        CustomPlaylistStatusText.Text = _customPlaylistTracks.Count == 0
            ? Strings.CustomStatusEmpty
            : Strings.CustomStatusCount(_customPlaylistTracks.Count);

        RefreshPlaybackScope();

        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void CustomSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        RefreshCustomPlaylistUi();
    }

    private void CustomSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (CustomSearchBox.Visibility == Visibility.Visible)
        {
            CustomSearchBox.Text = string.Empty;
            CustomSearchBox.Visibility = Visibility.Collapsed;
            CustomSearchButton.Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0));
            RefreshCustomPlaylistUi();
            return;
        }

        CustomSearchBox.Visibility = Visibility.Visible;
        CustomSearchButton.Background = CreateNavSelectionBrush();
        CustomSearchBox.Focus(FocusState.Programmatic);
    }

    private void CustomSortButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var byAdded = new MenuFlyoutItem { Text = Strings.SortByAddedOrder };
        var byName = new MenuFlyoutItem { Text = Strings.SortByName };
        var byDuration = new MenuFlyoutItem { Text = Strings.SortByDuration };

        byAdded.Click += (_, _) => SetCustomSortMode(CustomSortMode.AddedOrder);
        byName.Click += (_, _) => SetCustomSortMode(CustomSortMode.Name);
        byDuration.Click += (_, _) => SetCustomSortMode(CustomSortMode.Duration);

        flyout.Items.Add(byAdded);
        flyout.Items.Add(byName);
        flyout.Items.Add(byDuration);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void SetCustomSortMode(CustomSortMode mode)
    {
        _customSortMode = mode;
        RefreshCustomPlaylistUi();
    }

    private async void CustomImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomPlaylist is null)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List
        };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        var playlist = _selectedCustomPlaylist;
        foreach (var file in files)
        {
            if (playlist.Paths.Any(path =>
                    string.Equals(path, file.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            playlist.Paths.Add(file.Path);
        }

        PersistCustomPlaylists();
        if (ReferenceEquals(_selectedCustomPlaylist, playlist))
        {
            await LoadCustomPlaylistTracksAsync(playlist);
        }
    }

    private void CustomPlaylistList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (CustomPlaylistList.SelectedItem is not Track track)
        {
            return;
        }

        if (!File.Exists(track.Path))
        {
            track.MarkMissing();
            RefreshCustomPlaylistUi();
            return;
        }

        _ = PlayTrackFromUiAsync(track);
    }

    private void ShowAddToPlaylistFlyout(FrameworkElement target, Track track)
    {
        var flyout = new MenuFlyout();
        if (_customPlaylists.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = Strings.NoCustomPlaylists,
                IsEnabled = false
            });
        }
        else
        {
            foreach (var playlist in _customPlaylists)
            {
                var item = new MenuFlyoutItem { Text = playlist.Name };
                item.Click += (_, _) => AddTrackToPlaylist(playlist, track);
                flyout.Items.Add(item);
            }
        }

        flyout.ShowAt(target);
    }

    private void AddTrackToPlaylist(SidebarPlaylistItem playlist, Track track)
    {
        if (!playlist.Paths.Any(path =>
                string.Equals(path, track.Path, StringComparison.OrdinalIgnoreCase)))
        {
            playlist.Paths.Add(track.Path);
            PersistCustomPlaylists();
        }

        if (ReferenceEquals(_selectedCustomPlaylist, playlist))
        {
            if (_customPlaylistTracks.All(candidate =>
                    !string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _customPlaylistTracks.Add(track);
                RefreshCustomPlaylistUi();
            }
        }

        SetStatus(Strings.StatusAddedToPlaylist(playlist.Name));
    }

    private void BeginPlaylistRename(SidebarPlaylistItem item, bool freshName)
    {
        if (item.IsEditing)
        {
            return;
        }

        item.IsEditing = true;
        item.Editor.Text = freshName ? string.Empty : item.Name;
        item.Button.Visibility = Visibility.Collapsed;
        item.Editor.Visibility = Visibility.Visible;
        item.Editor.Focus(FocusState.Programmatic);
        if (!freshName)
        {
            item.Editor.SelectAll();
        }
    }

    private void CommitPlaylistRename(SidebarPlaylistItem item)
    {
        if (!item.IsEditing)
        {
            return;
        }

        var name = item.Editor.Text.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            item.Name = name;
            item.Label.Text = name;
            AutomationProperties.SetName(item.Button, name);
        }

        item.IsEditing = false;
        item.Editor.Visibility = Visibility.Collapsed;
        item.Button.Visibility = Visibility.Visible;
        PersistCustomPlaylists();

        if (ReferenceEquals(_selectedCustomPlaylist, item))
        {
            CustomPlaylistTitleText.Text = item.Name;
        }
    }

    private void CancelPlaylistRename(SidebarPlaylistItem item)
    {
        if (!item.IsEditing)
        {
            return;
        }

        item.IsEditing = false;
        item.Editor.Text = item.Name;
        item.Editor.Visibility = Visibility.Collapsed;
        item.Button.Visibility = Visibility.Visible;
    }

    private void ShowPlaylistMenu(SidebarPlaylistItem item, Windows.Foundation.Point position)
    {
        var flyout = new MenuFlyout();

        var renameItem = new MenuFlyoutItem
        {
            Text = Strings.Rename,
            Icon = new FontIcon { Glyph = "\uE8AC" }
        };
        renameItem.Click += (_, _) => BeginPlaylistRename(item, freshName: false);

        var deleteItem = new MenuFlyoutItem
        {
            Text = Strings.Delete,
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += async (_, _) => await DeletePlaylistAsync(item);

        flyout.Items.Add(renameItem);
        flyout.Items.Add(deleteItem);
        flyout.ShowAt(item.Button, new FlyoutShowOptions
        {
            Position = position
        });
    }

    private async Task DeletePlaylistAsync(SidebarPlaylistItem item)
    {
        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.DeletePlaylistTitle(item.Name),
            Content = Strings.DeletePlaylistDetail,
            PrimaryButtonText = Strings.Delete,
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

        var wasSelected = ReferenceEquals(_selectedCustomPlaylist, item);
        item.IsEditing = false;
        _customPlaylists.Remove(item);
        CustomPlaylistsPanel.Children.Remove(item.Root);
        PersistCustomPlaylists();

        if (_playbackScope == PlaybackScope.CustomPlaylist &&
            _customPlaylistTracks.Any(track => item.Paths.Any(path =>
                string.Equals(path, track.Path, StringComparison.OrdinalIgnoreCase))))
        {
            _playbackScope = PlaybackScope.Queue;
        }

        if (wasSelected)
        {
            SelectSidebarNav(DefaultPlaylistButton, "default");
        }
    }

    private void PersistCustomPlaylists()
    {
        PlaylistCatalogStore.Save(_customPlaylists.Select(item => new PlaylistCatalogEntry
        {
            Id = item.Id,
            Name = item.Name,
            Paths = item.Paths.ToList()
        }));

        if (_searchPageOpen && _lastSearchQuery.Length > 0)
        {
            RefreshSearchResults(_lastSearchQuery);
        }
    }

    private sealed class SidebarPlaylistItem
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Grid Root { get; init; } = null!;
        public Button Button { get; init; } = null!;
        public TextBlock Label { get; init; } = null!;
        public TextBox Editor { get; init; } = null!;
        public List<string> Paths { get; } = [];
        public bool IsEditing { get; set; }
    }
}
