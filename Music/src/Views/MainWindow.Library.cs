using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Music.Models;

namespace Music;

/// <summary>
/// The library page: one flat list of every track the app knows about, drawn
/// from the default queue plus all custom playlists, with its own search.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<Track> _libraryAll = [];
    private readonly ObservableCollection<Track> _visibleLibrary = [];

    private void InitializeLibrary()
    {
        LibraryList.ItemsSource = _visibleLibrary;
        RefreshLibrary();
    }

    /// <summary>
    /// Rebuilds the library from the current queue and playlists, de-duplicated
    /// by file path so a track in several lists appears once.
    /// </summary>
    private void RefreshLibrary()
    {
        _libraryAll.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in _queue)
        {
            AddToLibrary(track, seen);
        }

        foreach (var playlist in _customPlaylists)
        {
            foreach (var path in playlist.Paths)
            {
                // Custom playlist entries are stored as paths; reuse the loaded
                // track when the queue already has it, otherwise make one.
                var known = _queue.FirstOrDefault(candidate =>
                    string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
                if (known is not null)
                {
                    AddToLibrary(known, seen);
                }
            }
        }

        ApplyLibraryFilter(LibrarySearchBox?.Text ?? string.Empty);
    }

    private void AddToLibrary(Track track, HashSet<string> seen)
    {
        if (track is null || string.IsNullOrEmpty(track.Path) || !seen.Add(track.Path))
        {
            return;
        }

        _libraryAll.Add(track);
    }

    private void LibrarySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyLibraryFilter(sender.Text ?? string.Empty);
    }

    private void ApplyLibraryFilter(string query)
    {
        var trimmed = query.Trim();
        _visibleLibrary.Clear();

        foreach (var track in _libraryAll)
        {
            if (trimmed.Length == 0 ||
                (track.Title ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (track.Artist ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (track.Album ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                _visibleLibrary.Add(track);
            }
        }

        UpdateLibraryState();
    }

    private void UpdateLibraryState()
    {
        if (LibraryCountText is null || LibraryEmptyPanel is null || LibraryList is null)
        {
            return;
        }

        LibraryCountText.Text = Strings.T(
            $"{_libraryAll.Count} 首曲目",
            $"{_libraryAll.Count} tracks");

        // The empty panel only speaks for an actually empty library; a filtered
        // list with no matches simply shows nothing.
        LibraryEmptyPanel.Visibility = _libraryAll.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        LibraryList.Visibility = _visibleLibrary.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void LibraryList_DoubleTapped(object sender, object e)
    {
        if (LibraryList.SelectedItem is Track track)
        {
            await PlayTrackFromUiAsync(track);
        }
    }
}
