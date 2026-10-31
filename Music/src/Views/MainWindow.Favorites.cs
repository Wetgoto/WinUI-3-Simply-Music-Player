using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Music.Models;
using Music.Services;

namespace Music;

/// <summary>
/// The favorites page lists every track whose record is marked with the star,
/// regardless of which playlist it belongs to.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<Track> _favoriteTracks = [];
    private readonly ObservableCollection<Track> _visibleFavoriteTracks = [];
    private readonly Dictionary<string, Track> _favoriteTrackCache = new(StringComparer.OrdinalIgnoreCase);
    private RecentSortMode _favoriteSortMode = RecentSortMode.RecentlyPlayed;
    private bool _favoritesLoaded;

    private void InitializeFavorites()
    {
        FavoritesList.ItemsSource = _visibleFavoriteTracks;
        FavoritesList.ContainerContentChanging += QueueList_ContainerContentChanging;
        FavoritesList.AddHandler(
            UIElement.RightTappedEvent,
            new RightTappedEventHandler(FavoritesList_RightTapped),
            true);
    }

    /// <summary>
    /// Adds or removes one track in the favorites list in place, so starring a
    /// song shows up immediately without reloading the whole page.
    /// </summary>
    private void ApplyFavoriteStateToFavoritesPage(Track track)
    {
        var existing = _favoriteTracks.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, track.Path, StringComparison.OrdinalIgnoreCase));

        if (track.IsFavorite)
        {
            if (existing is null)
            {
                // Reuse the instance the user acted on so row state stays shared.
                _favoriteTrackCache[track.Path] = track;
                _favoriteTracks.Insert(0, track);
                RefreshFavoritesUi();
            }

            return;
        }

        if (existing is not null)
        {
            _favoriteTracks.Remove(existing);
            RefreshFavoritesUi();
        }
    }

    /// <summary>
    /// Reloads the favorite records from disk and rebuilds the page. Called
    /// when the page is opened, not on every track change.
    /// </summary>
    private void RefreshFavorites()
    {
        var records = TrackDataStore.LoadFavorites();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _favoriteTracks.Clear();

        foreach (var record in records)
        {
            if (!seen.Add(record.Path))
            {
                continue;
            }

            var track = ResolveFavoriteTrack(record);
            track.SetFavorite(true);
            _favoriteTracks.Add(track);
        }

        _favoritesLoaded = true;
        RefreshFavoritesUi();
    }

    /// <summary>
    /// Reuses an already loaded Track instance so the row keeps artwork and
    /// shares the current/favorite state with the rest of the window.
    /// </summary>
    private Track ResolveFavoriteTrack(TrackDataRecord record)
    {
        if (_favoriteTrackCache.TryGetValue(record.Path, out var cached))
        {
            if (File.Exists(record.Path))
            {
                cached.MarkAvailable();
            }
            else
            {
                cached.MarkMissing();
            }

            return cached;
        }

        // Prefer an instance already in use (the playing track or a default
        // list row) so current/favorite state stays shared across the window.
        Track? track = _currentTrack is not null &&
            string.Equals(_currentTrack.Path, record.Path, StringComparison.OrdinalIgnoreCase)
                ? _currentTrack
                : _queue.FirstOrDefault(candidate =>
                    string.Equals(candidate.Path, record.Path, StringComparison.OrdinalIgnoreCase));

        if (track is null)
        {
            var exists = File.Exists(record.Path);
            track = new Track(record.Path, isMissing: !exists);
            if (exists)
            {
                track.ApplyMetadata(
                    record.Title,
                    record.Artist,
                    record.Album,
                    record.AlbumArtist,
                    TimeSpan.FromTicks(record.DurationTicks),
                    record.Bitrate);
                track.SetImportedAt(record.ImportedAt);
                track.SetFileSize(record.FileSize);
            }
        }

        if (!File.Exists(record.Path))
        {
            track.MarkMissing();
        }

        _favoriteTrackCache[record.Path] = track;
        return track;
    }

    private void RefreshFavoritesUi()
    {
        var selected = FavoritesList.SelectedItem as Track;
        var query = FavoritesSearchBox?.Text?.Trim();
        var searching = !string.IsNullOrWhiteSpace(query);

        IEnumerable<Track> ordered = _favoriteSortMode switch
        {
            RecentSortMode.Name => _favoriteTracks
                .OrderBy(track => track.QueueTitle, StringComparer.CurrentCultureIgnoreCase),
            RecentSortMode.Duration => _favoriteTracks.OrderBy(track => track.Duration),
            _ => _favoriteTracks
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

        _visibleFavoriteTracks.Clear();
        foreach (var track in ordered)
        {
            _visibleFavoriteTracks.Add(track);
        }

        FavoritesList.SelectedItem = selected is not null && _visibleFavoriteTracks.Contains(selected)
            ? selected
            : null;
        FavoritesList.Visibility = _visibleFavoriteTracks.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FavoritesEmptyPanel.Visibility = _favoriteTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FavoritesNoResultsPanel.Visibility = searching &&
            _favoriteTracks.Count > 0 &&
            _visibleFavoriteTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FavoritesCountText.Text = searching
            ? Strings.MatchCount(_visibleFavoriteTracks.Count)
            : Strings.ItemCount(_favoriteTracks.Count);

        static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void FavoritesSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        RefreshFavoritesUi();
    }

    private void FavoritesSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesSearchBox.Visibility == Visibility.Visible)
        {
            FavoritesSearchBox.Text = string.Empty;
            FavoritesSearchBox.Visibility = Visibility.Collapsed;
            FavoritesSearchButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0));
            RefreshFavoritesUi();
            return;
        }

        FavoritesSearchBox.Visibility = Visibility.Visible;
        FavoritesSearchButton.Background = CreateNavSelectionBrush();
        FavoritesSearchBox.Focus(FocusState.Programmatic);
    }

    private void FavoritesSortButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var byRecent = new MenuFlyoutItem { Text = Strings.SortByRecent };
        var byName = new MenuFlyoutItem { Text = Strings.SortByName };
        var byDuration = new MenuFlyoutItem { Text = Strings.SortByDuration };

        byRecent.Click += (_, _) => SetFavoriteSortMode(RecentSortMode.RecentlyPlayed);
        byName.Click += (_, _) => SetFavoriteSortMode(RecentSortMode.Name);
        byDuration.Click += (_, _) => SetFavoriteSortMode(RecentSortMode.Duration);

        flyout.Items.Add(byRecent);
        flyout.Items.Add(byName);
        flyout.Items.Add(byDuration);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void SetFavoriteSortMode(RecentSortMode mode)
    {
        _favoriteSortMode = mode;
        RefreshFavoritesUi();
    }

    private async void FavoritesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not Track track)
        {
            return;
        }

        if (!File.Exists(track.Path))
        {
            track.MarkMissing();
            RefreshFavoritesUi();
            await ShowMissingTracksDialogAsync([track]);
            return;
        }

        // Like Recently played, a favorite is a standalone entry: it plays on
        // its own and playback does not continue into another list afterwards.
        await PlayTrackInScopeAsync(track, PlaybackScope.Single);
    }

    private void FavoritesList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is not Track track)
        {
            return;
        }

        FavoritesList.SelectedItem = track;
        ShowTrackContextFlyout(
            track,
            FavoritesList,
            e.GetPosition(FavoritesList),
            TrackMenuContext.Favorites);
        e.Handled = true;
    }

    private void ShowFavoritesPage()
    {
        if (!_favoritesLoaded)
        {
            RefreshFavorites();
        }
        else
        {
            RefreshFavoritesUi();
        }

        HideAllPages();
        FavoritesPage.Visibility = Visibility.Visible;
    }

    private void HideFavoritesPage()
    {
        FavoritesPage.Visibility = Visibility.Collapsed;
    }
}
