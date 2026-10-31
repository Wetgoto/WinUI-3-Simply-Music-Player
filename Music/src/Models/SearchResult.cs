using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Music.Models;

/// <summary>
/// One row on the global search page: a track plus the playlists it belongs to.
/// </summary>
public sealed class SearchResult : INotifyPropertyChanged
{
    public SearchResult(Track track, string sourceText)
    {
        Track = track;
        SourceText = sourceText;
        track.PropertyChanged += Track_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Track Track { get; }

    /// <summary>Playlist names that contain this track, for example "默认列表 · 通勤歌单".</summary>
    public string SourceText { get; }

    public string QueueTitle => Track.QueueTitle;

    public string Subtitle => Track.IsMissing
        ? "文件不存在"
        : string.IsNullOrWhiteSpace(Track.Album)
            ? Track.ArtistDisplay
            : $"{Track.ArtistDisplay} · {Track.Album}";

    public string TrailingText => Track.TrailingText;

    /// <summary>
    /// The row itself now looks exactly like every other list, so the playlists
    /// that contain the track are folded into the tooltip instead of a column.
    /// </summary>
    public string ToolTipText => string.IsNullOrWhiteSpace(SourceText)
        ? Track.ToolTipText
        : $"{Track.ToolTipText}\n{SourceText}";

    public double RowOpacity => Track.RowOpacity;

    public ImageSource? Artwork => Track.Artwork;

    public Visibility ArtworkVisibility => Track.ArtworkVisibility;

    public Visibility PlaceholderVisibility => Track.PlaceholderVisibility;

    private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Track.Title):
            case nameof(Track.Artist):
            case nameof(Track.Album):
            case nameof(Track.Duration):
            case nameof(Track.IsMissing):
                OnPropertyChanged(nameof(QueueTitle));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(TrailingText));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(RowOpacity));
                OnPropertyChanged(nameof(Artwork));
                OnPropertyChanged(nameof(ArtworkVisibility));
                OnPropertyChanged(nameof(PlaceholderVisibility));
                break;

            case nameof(Track.Artwork):
                OnPropertyChanged(nameof(Artwork));
                OnPropertyChanged(nameof(ArtworkVisibility));
                OnPropertyChanged(nameof(PlaceholderVisibility));
                break;
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
