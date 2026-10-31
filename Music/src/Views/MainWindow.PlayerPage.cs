using System.Collections.ObjectModel;
using System.Text;
using Music.Models;
using Music.Services;

namespace Music;

/// <summary>
/// The right-hand panel of the full player page: either the lyrics of the
/// playing track or the play queue.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<string> _lyricsLines = [];
    private bool _showingLyrics = true;

    private void InitializePlayerPagePanel()
    {
        LyricsList.ItemsSource = _lyricsLines;
        FullQueueList.ItemsSource = _visibleQueue;
        UpdatePlayerPagePanelTabs();
    }

    private void LyricsTabButton_Click(object sender, object e)
    {
        _showingLyrics = true;
        UpdatePlayerPagePanelTabs();
    }

    private void QueueTabButton_Click(object sender, object e)
    {
        _showingLyrics = false;
        UpdatePlayerPagePanelTabs();
    }

    private void UpdatePlayerPagePanelTabs()
    {
        LyricsScrollViewer.Visibility = _showingLyrics
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        QueueTabPanel.Visibility = _showingLyrics
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        if (!_showingLyrics)
        {
            FullQueueEmptyText.Visibility = _visibleQueue.Count == 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        // The active tab is highlighted with the selection brush.
        var active = CreateNavSelectionBrush();
        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
        LyricsTabButton.Background = _showingLyrics ? active : transparent;
        QueueTabButton.Background = _showingLyrics ? transparent : active;
    }

    private async void FullQueueList_DoubleTapped(object sender, object e)
    {
        if (FullQueueList.SelectedItem is Track track)
        {
            await PlayTrackFromUiAsync(track);
        }
    }

    /// <summary>
    /// Loads the lyrics shown beside the player page controls. A .lrc file next
    /// to the audio wins; otherwise its ID3/FLAC lyrics tag is used. When
    /// neither exists a placeholder line replaces the list.
    /// </summary>
    private void RefreshLyrics(Track? track)
    {
        _lyricsLines.Clear();

        if (track is null)
        {
            _lyricsLines.Add(Strings.NoLyrics);
            return;
        }

        var lines = ReadLyrics(track);
        if (lines.Count == 0)
        {
            _lyricsLines.Add(Strings.NoLyrics);
            return;
        }

        foreach (var line in lines)
        {
            _lyricsLines.Add(line);
        }

        LyricsScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static List<string> ReadLyrics(Track track)
    {
        // 1. A sidecar .lrc or .txt with the same base name.
        foreach (var extension in new[] { ".lrc", ".txt" })
        {
            try
            {
                var candidate = Path.ChangeExtension(track.Path, extension);
                if (File.Exists(candidate))
                {
                    var text = File.ReadAllText(candidate, DetectEncoding(candidate));
                    var parsed = ParseLyrics(text);
                    if (parsed.Count > 0)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // A broken sidecar must not stop the panel from rendering.
            }
        }

        // 2. Lyrics embedded in the audio file itself (ID3/FLAC lyrics tags are
        // not read by the metadata reader yet), so nothing more to try here.
        return [];
    }

    private static Encoding DetectEncoding(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false);
            }
        }
        catch
        {
        }

        return new UTF8Encoding(false);
    }

    /// <summary>
    /// Accepts both plain-text and LRC lyrics. LRC time tags ([mm:ss.xx]) are
    /// stripped; metadata lines such as [ar:] are dropped. Duplicate blank
    /// lines are collapsed.
    /// </summary>
    private static List<string> ParseLyrics(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();

            // Drop LRC metadata tags like [ti:...], [ar:...].
            if (line.StartsWith('[') && line.Contains(':'))
            {
                var closing = line.IndexOf(']');
                if (closing > 0 && !IsTimeTag(line[..(closing + 1)]))
                {
                    continue;
                }
            }

            // Strip any leading time tags ([00:12.34][00:15.00]).
            while (line.StartsWith('['))
            {
                var closing = line.IndexOf(']');
                if (closing <= 0 || !IsTimeTag(line[..(closing + 1)]))
                {
                    break;
                }

                line = line[(closing + 1)..].TrimStart();
            }

            if (line.Length == 0)
            {
                if (result.Count == 0 || result[^1].Length == 0)
                {
                    continue;
                }

                result.Add(string.Empty);
                continue;
            }

            result.Add(line);
        }

        while (result.Count > 0 && result[^1].Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static bool IsTimeTag(string tag)
    {
        // [mm:ss], [mm:ss.xx], [mm:ss:xx]
        return tag.Length > 2 &&
            tag[1] is >= '0' and <= '9' &&
            tag.Contains(':');
    }
}
