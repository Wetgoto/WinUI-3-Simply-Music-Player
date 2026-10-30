using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Music.Models;
using Music.Services;
using Windows.Foundation;
using Windows.System;

namespace Music;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly Dictionary<string, Track> _searchTrackCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _searchIndexBuilding;
    private bool _searchPageOpen;
    private bool _suppressSearchNavigation;
    private string _searchReturnPage = "default";
    private SidebarPlaylistItem? _searchReturnPlaylist;
    private string _lastSearchQuery = string.Empty;

    private void SidebarSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen)
        {
            return;
        }

        var query = SidebarSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            _lastSearchQuery = string.Empty;
            _searchResults.Clear();
            RefreshSearchPanels();

            if (_searchPageOpen && !_suppressSearchNavigation)
            {
                ExitSearchPage();
            }

            return;
        }

        if (!_searchPageOpen)
        {
            EnterSearchPage();
        }

        _lastSearchQuery = query;
        RefreshSearchResults(query);
        _ = EnsureSearchIndexAsync();
    }

    private void SidebarSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        SidebarSearchBox.Text = string.Empty;
        SidebarSearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        SidebarSearchBox.Text = string.Empty;
        SidebarSearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SidebarSearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Hides the search results and clears the input, without restoring the
    /// previous page. Called by the sidebar navigation before it switches pages.
    /// </summary>
    private void ResetSidebarSearch()
    {
        _searchResults.Clear();
        _lastSearchQuery = string.Empty;

        if (SidebarSearchBox.Text.Length > 0)
        {
            _suppressSearchNavigation = true;
            SidebarSearchBox.Text = string.Empty;
            _suppressSearchNavigation = false;
        }

        if (_searchPageOpen)
        {
            _searchPageOpen = false;
            SearchPage.Visibility = Visibility.Collapsed;
        }
    }

    private void EnterSearchPage()
    {
        if (_searchPageOpen)
        {
            return;
        }

        _searchPageOpen = true;
        _searchReturnPlaylist = null;

        if (RecentPage.Visibility == Visibility.Visible)
        {
            _searchReturnPage = "recent";
        }
        else if (CustomPlaylistPage.Visibility == Visibility.Visible)
        {
            _searchReturnPage = "custom";
            _searchReturnPlaylist = _selectedCustomPlaylist;
        }
        else if (SettingsPage.Visibility == Visibility.Visible)
        {
            _searchReturnPage = "settings";
        }
        else
        {
            _searchReturnPage = "default";
        }

        MainWorkspace.Visibility = Visibility.Collapsed;
        RecentPage.Visibility = Visibility.Collapsed;
        CustomPlaylistPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        SearchPage.Visibility = Visibility.Visible;
    }

    private void ExitSearchPage()
    {
        _searchPageOpen = false;
        SearchPage.Visibility = Visibility.Collapsed;
        RestorePageAfterSearch();
    }

    private void RestorePageAfterSearch()
    {
        switch (_searchReturnPage)
        {
            case "recent":
                MainWorkspace.Visibility = Visibility.Collapsed;
                CustomPlaylistPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Collapsed;
                RecentPage.Visibility = Visibility.Visible;
                RefreshRecentUi();
                break;

            case "custom" when _searchReturnPlaylist is not null:
                MainWorkspace.Visibility = Visibility.Collapsed;
                RecentPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Collapsed;
                CustomPlaylistPage.Visibility = Visibility.Visible;
                CustomPlaylistTitleText.Text = _searchReturnPlaylist.Name;
                _ = LoadCustomPlaylistTracksAsync(_searchReturnPlaylist);
                break;

            case "settings":
                MainWorkspace.Visibility = Visibility.Collapsed;
                RecentPage.Visibility = Visibility.Collapsed;
                CustomPlaylistPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Visible;
                break;

            default:
                RecentPage.Visibility = Visibility.Collapsed;
                CustomPlaylistPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Collapsed;
                MainWorkspace.Visibility = Visibility.Visible;
                break;
        }
    }

    private void RefreshSearchResults(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            _searchResults.Clear();
            RefreshSearchPanels();
            return;
        }

        var sources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddSource(string? path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!sources.TryGetValue(path, out var labels))
            {
                labels = [];
                sources[path] = labels;
            }

            if (!labels.Contains(label, StringComparer.Ordinal))
            {
                labels.Add(label);
            }
        }

        foreach (var track in _queue)
        {
            AddSource(track.Path, "默认列表");
            _searchTrackCache[track.Path] = track;
        }

        foreach (var track in _recentTracks)
        {
            _searchTrackCache[track.Path] = track;
            if (!sources.ContainsKey(track.Path))
            {
                AddSource(track.Path, "最近的项目");
            }
        }

        foreach (var playlist in _customPlaylists)
        {
            foreach (var path in playlist.Paths)
            {
                AddSource(path, playlist.Name);
            }
        }

        var matches = new List<SearchResult>();
        foreach (var (path, labels) in sources)
        {
            var track = ResolveSearchTrack(path);
            if (SearchTrackMatches(track, query))
            {
                matches.Add(new SearchResult(track, string.Join(" · ", labels)));
            }
        }

        matches.Sort((left, right) => string.Compare(
            left.QueueTitle,
            right.QueueTitle,
            StringComparison.CurrentCultureIgnoreCase));

        _searchResults.Clear();
        foreach (var match in matches)
        {
            _searchResults.Add(match);
        }

        RefreshSearchPanels();
    }

    private Track ResolveSearchTrack(string path)
    {
        if (_searchTrackCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var track = new Track(path, isMissing: !File.Exists(path));
        _searchTrackCache[path] = track;
        return track;
    }

    private static bool SearchTrackMatches(Track track, string query)
    {
        return Contains(track.Title, query) ||
               Contains(track.Artist, query) ||
               Contains(track.Album, query) ||
               Contains(track.AlbumArtist, query) ||
               Contains(Path.GetFileName(track.Path), query) ||
               Contains(Path.GetFileNameWithoutExtension(track.Path), query);

        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshSearchPanels()
    {
        var hasResults = _searchResults.Count > 0;

        SearchList.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        SearchNoResultsPanel.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        SearchNoResultsTitle.Text = _searchIndexBuilding ? "正在搜索…" : "没有找到匹配的曲目";
        SearchNoResultsDetail.Text = _searchIndexBuilding
            ? "正在读取所有播放列表中的曲目信息。"
            : "换一个关键词，或检查播放列表里是否包含这首歌。";
        SearchCountText.Text = _searchIndexBuilding
            ? $"{_searchResults.Count} 个结果 · 正在索引所有播放列表…"
            : $"{_searchResults.Count} 个结果 · 搜索范围：所有播放列表";
        SearchClearButton.IsEnabled = SidebarSearchBox.Text.Length > 0;
    }

    /// <summary>
    /// Reads metadata for tracks that only exist inside custom playlists, so they
    /// can be matched by title/artist instead of just by file name.
    /// </summary>
    private async Task EnsureSearchIndexAsync()
    {
        if (_searchIndexBuilding)
        {
            return;
        }

        var pending = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPending(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                !_searchTrackCache.ContainsKey(path) &&
                seen.Add(path))
            {
                pending.Add(path);
            }
        }

        foreach (var track in _queue)
        {
            AddPending(track.Path);
        }

        foreach (var track in _recentTracks)
        {
            AddPending(track.Path);
        }

        foreach (var playlist in _customPlaylists)
        {
            foreach (var path in playlist.Paths)
            {
                AddPending(path);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        _searchIndexBuilding = true;
        RefreshSearchPanels();

        var loaded = 0;
        try
        {
            foreach (var path in pending)
            {
                if (!_searchPageOpen)
                {
                    break;
                }

                Track track;
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

                _searchTrackCache[track.Path] = track;

                if (++loaded % 4 == 0)
                {
                    RefreshSearchResults(_lastSearchQuery);
                }
            }
        }
        finally
        {
            _searchIndexBuilding = false;
        }

        if (_searchPageOpen)
        {
            RefreshSearchResults(_lastSearchQuery);
        }
    }

    private async void SearchList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SearchList.SelectedItem is SearchResult result)
        {
            await PlaySearchResultAsync(result.Track);
        }
    }

    private async Task PlaySearchResultAsync(Track track)
    {
        var queueTrack = _queue.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase));

        if (queueTrack is null)
        {
            queueTrack = track;
            _queue.Add(queueTrack);
            _knownPaths.Add(queueTrack.Path);
            RefreshQueueUi();
            PersistPlaylist();
        }

        await PlayTrackFromUiAsync(queueTrack);
    }

    private void SearchList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not SearchResult result)
        {
            return;
        }

        SearchList.SelectedItem = result;
        ShowSearchResultFlyout(result.Track, e.GetPosition(SearchList));
        e.Handled = true;
    }

    private void ShowSearchResultFlyout(Track track, Point position)
    {
        var flyout = new MenuFlyout();

        var playItem = new MenuFlyoutItem
        {
            Text = "播放",
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playItem.Click += async (_, _) => await PlaySearchResultAsync(track);
        flyout.Items.Add(playItem);

        if (!_queue.Any(candidate =>
                string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
        {
            var addToQueueItem = new MenuFlyoutItem
            {
                Text = "添加到默认列表",
                Icon = new FontIcon { Glyph = "\uE710" }
            };
            addToQueueItem.Click += (_, _) => AddSearchResultToQueue(track);
            flyout.Items.Add(addToQueueItem);
        }

        if (_customPlaylists.Count > 0)
        {
            var addToPlaylist = new MenuFlyoutSubItem
            {
                Text = "添加到播放列表",
                Icon = new FontIcon { Glyph = "\uE8FD" }
            };

            foreach (var playlist in _customPlaylists)
            {
                var item = new MenuFlyoutItem { Text = playlist.Name };
                item.Click += (_, _) => AddTrackToPlaylist(playlist, track);
                addToPlaylist.Items.Add(item);
            }

            flyout.Items.Add(addToPlaylist);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var detailsItem = new MenuFlyoutItem
        {
            Text = "查看详细信息",
            Icon = new FontIcon { Glyph = "\uE946" }
        };
        detailsItem.Click += async (_, _) => await ShowTrackDetailsAsync(track);
        flyout.Items.Add(detailsItem);

        flyout.ShowAt(SearchList, new FlyoutShowOptions { Position = position });
    }

    private void AddSearchResultToQueue(Track track)
    {
        if (_queue.Any(candidate =>
                string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("这首歌已经在默认列表里");
            return;
        }

        _queue.Add(track);
        _knownPaths.Add(track.Path);
        RefreshQueueUi();
        PersistPlaylist();
        SetStatus($"已添加到默认列表 · {track.Title}");
    }
}
