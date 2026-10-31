using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Music.Models;
using Music.Services;

namespace Music;

/// <summary>
/// One lyric line. Timed lines carry the moment the line starts so the page can
/// highlight it and seek when it is tapped; untimed lyrics keep a zero timestamp
/// and simply render as a readable block.
/// </summary>
public sealed class LyricLine : System.ComponentModel.INotifyPropertyChanged
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Start of the line, or <see cref="TimeSpan.Zero"/> for plain lyrics.</summary>
    public TimeSpan Time { get; set; }

    private bool _isActive;
    private double _opacity = 0.45;
    private Microsoft.UI.Xaml.Media.Brush _brush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

    /// <summary>True while this line is the one being sung.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            Raise(nameof(IsActive));
        }
    }

    /// <summary>Dimmed for inactive lines, matching the reference layout.</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (Math.Abs(_opacity - value) < 0.001)
            {
                return;
            }

            _opacity = value;
            Raise(nameof(Opacity));
        }
    }

    public Microsoft.UI.Xaml.Media.Brush Brush
    {
        get => _brush;
        set
        {
            if (ReferenceEquals(_brush, value))
            {
                return;
            }

            _brush = value;
            Raise(nameof(Brush));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>
/// The right-hand panel of the full player page: either the lyrics of the
/// playing track or the play queue.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<LyricLine> _lyricsLines = [];
    private bool _showingLyrics = true;
    private bool _lyricsAreTimed;
    private int _activeLyricIndex = -1;
    private bool _isUserScrollingLyrics;

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
    /// Tapping a lyric line seeks to that line's timestamp. Lines without a
    /// timestamp have nothing to seek to, so the tap is ignored.
    /// </summary>
    private void LyricsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LyricLine line || !_lyricsAreTimed)
        {
            return;
        }

        if (line.Time <= TimeSpan.Zero && _activeLyricIndex != _lyricsLines.IndexOf(line))
        {
            // A zero timestamp on the first line is legitimate (it starts at 0).
            if (_lyricsLines.IndexOf(line) != 0)
            {
                return;
            }
        }

        SeekTo(line.Time);
    }

    /// <summary>Moves playback to a position, keeping both sliders in sync.</summary>
    private void SeekTo(TimeSpan position)
    {
        if (_player is null || position < TimeSpan.Zero)
        {
            return;
        }

        var max = _player.PlaybackSession.NaturalDuration;
        if (max > TimeSpan.Zero && position > max)
        {
            position = max;
        }

        _player.PlaybackSession.Position = position;
        ProgressSlider.Value = position.TotalSeconds;
        FullProgressSlider.Value = position.TotalSeconds;
        CurrentTimeText.Text = FormatClock(position);
        FullCurrentTimeText.Text = FormatClock(position);

        // Seeking jumps playback, so update the highlight without waiting for
        // the next position tick.
        UpdateLyricHighlight(position);
    }

    /// <summary>
    /// Loads the lyrics shown beside the player page controls. A .lrc file next
    /// to the audio wins; otherwise its ID3/FLAC lyrics tag is used. When
    /// neither exists a placeholder line replaces the list.
    /// </summary>
    private void RefreshLyrics(Track? track)
    {
        _lyricsLines.Clear();
        _activeLyricIndex = -1;
        _lyricsAreTimed = false;

        if (track is null)
        {
            _lyricsLines.Add(new LyricLine { Text = Strings.NoLyrics, Opacity = 1 });
            ApplyLyricBrushes();
            return;
        }

        var parsed = ReadLyrics(track);
        if (parsed.Count == 0)
        {
            _lyricsLines.Add(new LyricLine { Text = Strings.NoLyrics, Opacity = 1 });
            ApplyLyricBrushes();
            return;
        }

        _lyricsAreTimed = parsed.Any(l => l.Time > TimeSpan.Zero);
        foreach (var line in parsed)
        {
            _lyricsLines.Add(line);
        }

        ApplyLyricBrushes();
        LyricsScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static List<LyricLine> ReadLyrics(Track track)
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
    /// Accepts both plain-text and LRC lyrics. LRC time tags become the line's
    /// start time so the panel can follow playback; metadata lines such as
    /// [ar:] are dropped, and duplicate blank lines are collapsed.
    /// </summary>
    private static List<LyricLine> ParseLyrics(string text)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            var lineTime = TimeSpan.Zero;

            // Drop LRC metadata tags like [ti:...], [ar:...].
            if (line.StartsWith('[') && line.Contains(':'))
            {
                var closing = line.IndexOf(']');
                if (closing > 0 && !IsTimeTag(line[..(closing + 1)]))
                {
                    continue;
                }
            }

            // Strip any leading time tags ([00:12.34][00:15.00]); the first one
            // is the line's start.
            var first = true;
            while (line.StartsWith('['))
            {
                var closing = line.IndexOf(']');
                if (closing <= 0 || !IsTimeTag(line[..(closing + 1)]))
                {
                    break;
                }

                if (first)
                {
                    lineTime = ParseTimeTag(line[..(closing + 1)]);
                    first = false;
                }

                line = line[(closing + 1)..].TrimStart();
            }

            if (line.Length == 0)
            {
                if (result.Count == 0 || result[^1].Text.Length == 0)
                {
                    continue;
                }

                result.Add(new LyricLine { Text = string.Empty, Time = lineTime });
                continue;
            }

            result.Add(new LyricLine { Text = line, Time = lineTime });
        }

        while (result.Count > 0 && result[^1].Text.Length == 0)
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

    /// <summary>Parses [mm:ss], [mm:ss.xx] or [mm:ss:xx] into a time span.</summary>
    private static TimeSpan ParseTimeTag(string tag)
    {
        var inner = tag.Trim('[', ']');
        var parts = inner.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var minutes))
        {
            return TimeSpan.Zero;
        }

        // Seconds may carry a fractional part after '.' or ':'.
        var secondsText = parts[1].Replace('.', ',');
        double seconds = 0;
        var commaIndex = secondsText.IndexOf(',');
        if (commaIndex >= 0)
        {
            var whole = secondsText[..commaIndex];
            var frac = secondsText[(commaIndex + 1)..];
            double.TryParse(whole, out seconds);
            if (int.TryParse(frac, out var fracValue) && frac.Length > 0)
            {
                seconds += fracValue / Math.Pow(10, frac.Length);
            }
        }
        else if (parts.Length >= 3)
        {
            // [mm:ss:xx]
            double.TryParse(parts[1], out seconds);
            if (int.TryParse(parts[2], out var hundredths))
            {
                seconds += hundredths / 100.0;
            }
        }
        else
        {
            double.TryParse(secondsText, out seconds);
        }

        return TimeSpan.FromSeconds(minutes * 60 + seconds);
    }

    /// <summary>
    /// Follows playback: highlights the line whose window contains the current
    /// position and scrolls it towards the middle of the panel.
    /// </summary>
    private void UpdateLyricHighlight(TimeSpan position)
    {
        if (!_showingLyrics || !_lyricsAreTimed || _lyricsLines.Count == 0)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < _lyricsLines.Count; i++)
        {
            if (_lyricsLines[i].Time <= position)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        if (index == _activeLyricIndex)
        {
            return;
        }

        _activeLyricIndex = index;
        ApplyLyricBrushes();

        if (!_isUserScrollingLyrics && index >= 0)
        {
            ScrollActiveLyricIntoView(index);
        }
    }

    private void ScrollActiveLyricIntoView(int index)
    {
        if (LyricsList.ContainerFromIndex(index) is not Microsoft.UI.Xaml.FrameworkElement container)
        {
            return;
        }

        try
        {
            var transform = container.TransformToVisual(LyricsList);
            var top = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var viewport = LyricsScrollViewer.ViewportHeight;
            var target = LyricsScrollViewer.VerticalOffset + top - (viewport / 2) + (container.ActualHeight / 2);
            LyricsScrollViewer.ChangeView(null, Math.Max(0, target), null, disableAnimation: false);
        }
        catch
        {
            // A mid-layout container can fail to transform; the next tick retries.
        }
    }

    private void ApplyLyricBrushes()
    {
        for (var i = 0; i < _lyricsLines.Count; i++)
        {
            var line = _lyricsLines[i];
            var isActive = i == _activeLyricIndex;
            line.IsActive = isActive;
            // Active line is fully opaque and bright; the rest fade back.
            line.Opacity = isActive ? 1.0 : 0.42;
        }
    }
}
