using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace Music.Models;

/// <summary>
/// One audio file in the playlist, including the metadata shown in the queue.
/// </summary>
public sealed class Track : INotifyPropertyChanged
{
    private string _title;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _albumArtist = string.Empty;
    private uint _bitrate;
    private long _fileSize;
    private TimeSpan _duration;
    private DateTimeOffset _importedAt = DateTimeOffset.Now;
    private bool _isFavorite;
    private ImageSource? _artwork;
    private bool _isCurrent;
    private bool _isPlaying;
    private bool _isMissing;

    public Track(StorageFile file)
        : this(file.Path)
    {
        File = file;
        _title = file.DisplayName;
    }

    public Track(string path, bool isMissing = false)
    {
        Path = path;
        _title = System.IO.Path.GetFileNameWithoutExtension(path);
        _isMissing = isMissing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StorageFile? File { get; }

    public string Path { get; }

    public string FileType => System.IO.Path.GetExtension(Path);

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(QueueTitle));
            }
        }
    }

    public string Artist
    {
        get => _artist;
        private set
        {
            if (SetProperty(ref _artist, value))
            {
                OnPropertyChanged(nameof(ArtistDisplay));
                OnPropertyChanged(nameof(QueueTitle));
                OnPropertyChanged(nameof(QueueSubtitle));
                OnPropertyChanged(nameof(DetailsLine));
            }
        }
    }

    public string Album
    {
        get => _album;
        private set
        {
            if (SetProperty(ref _album, value))
            {
                OnPropertyChanged(nameof(AlbumDisplay));
                OnPropertyChanged(nameof(QueueSubtitle));
                OnPropertyChanged(nameof(RecentSubtitle));
                OnPropertyChanged(nameof(DetailsLine));
            }
        }
    }

    public string AlbumArtist
    {
        get => _albumArtist;
        private set
        {
            if (SetProperty(ref _albumArtist, value))
            {
                OnPropertyChanged(nameof(AlbumArtistDisplay));
            }
        }
    }

    public uint Bitrate
    {
        get => _bitrate;
        private set
        {
            if (SetProperty(ref _bitrate, value))
            {
                OnPropertyChanged(nameof(FileInfoText));
            }
        }
    }

    public long FileSize
    {
        get => _fileSize;
        private set
        {
            if (SetProperty(ref _fileSize, value))
            {
                OnPropertyChanged(nameof(FileInfoText));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        private set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteToolTip));
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(TrailingText));
            }
        }
    }

    public DateTimeOffset ImportedAt
    {
        get => _importedAt;
        private set => SetProperty(ref _importedAt, value);
    }

    public ImageSource? Artwork
    {
        get => _artwork;
        private set
        {
            if (SetProperty(ref _artwork, value))
            {
                OnPropertyChanged(nameof(ArtworkVisibility));
                OnPropertyChanged(nameof(PlaceholderVisibility));
            }
        }
    }

    public byte[]? ArtworkBytes { get; private set; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(CurrentVisibility));
                OnPropertyChanged(nameof(QueueSubtitle));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(QueueSubtitle));
            }
        }
    }

    public bool IsMissing
    {
        get => _isMissing;
        private set
        {
            if (SetProperty(ref _isMissing, value))
            {
                OnPropertyChanged(nameof(RowOpacity));
                OnPropertyChanged(nameof(MissingVisibility));
                OnPropertyChanged(nameof(TrailingText));
                OnPropertyChanged(nameof(QueueSubtitle));
                OnPropertyChanged(nameof(RecentSubtitle));
                OnPropertyChanged(nameof(DetailsLine));
                OnPropertyChanged(nameof(FileInfoText));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    public double RowOpacity => IsMissing ? 0.45 : 1.0;

    public Visibility MissingVisibility => IsMissing ? Visibility.Visible : Visibility.Collapsed;

    public string TrailingText => IsMissing ? Strings.FileMissing : DurationText;

    public string ToolTipText => IsMissing ? $"{Strings.FileNotFound}: {Path}" : Path;

    public string ArtistDisplay => string.IsNullOrWhiteSpace(Artist) ? Strings.UnknownArtist : Artist;

    public string AlbumDisplay => string.IsNullOrWhiteSpace(Album) ? Strings.UnknownAlbum : Album;

    public string AlbumArtistDisplay => string.IsNullOrWhiteSpace(AlbumArtist) ? Strings.UnknownAlbumArtist : AlbumArtist;

    public string DurationText => FormatDuration(Duration);

    public string DetailsLine
    {
        get
        {
            if (IsMissing)
            {
                return $"{Strings.FileNotFound}: {Path}";
            }

            if (string.IsNullOrWhiteSpace(Album))
            {
                return ArtistDisplay;
            }

            return $"{ArtistDisplay}  ·  {Album}";
        }
    }

    public string QueueTitle => $"{Title} - {ArtistDisplay}";

    public string RecentSubtitle => IsMissing ? Strings.FileNotFound : AlbumDisplay;

    public string QueueSubtitle
    {
        get
        {
            if (IsMissing)
            {
                return Strings.FileNotFoundDoubleClick;
            }

            return AlbumDisplay;
        }
    }

    public string FileInfoText
    {
        get
        {
            if (IsMissing)
            {
                return Strings.FileMissing;
            }

            var bitrate = Bitrate > 0 ? $"{Bitrate / 1000} kbps" : Strings.UnknownBitrate;
            var size = FileSize > 0 ? FormatFileSize(FileSize) : Strings.UnknownSize;
            return $"{bitrate} | {size}";
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteToolTip => IsFavorite ? Strings.Unfavorite : Strings.Favorite;

    public Visibility ArtworkVisibility => Artwork is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PlaceholderVisibility => Artwork is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public void ApplyMetadata(
        string? title,
        string? artist,
        string? album,
        string? albumArtist,
        TimeSpan duration,
        uint bitrate)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(artist))
        {
            Artist = artist.Trim();
        }

        if (!string.IsNullOrWhiteSpace(album))
        {
            Album = album.Trim();
        }

        if (!string.IsNullOrWhiteSpace(albumArtist))
        {
            AlbumArtist = albumArtist.Trim();
        }

        if (duration > TimeSpan.Zero)
        {
            Duration = duration;
        }

        if (bitrate > 0)
        {
            Bitrate = bitrate;
        }
    }

    public void SetDuration(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            Duration = duration;
        }
    }

    public void SetArtwork(ImageSource? artwork)
    {
        Artwork = artwork;
    }

    public void SetArtworkBytes(byte[]? artworkBytes)
    {
        ArtworkBytes = artworkBytes;
    }

    public void SetImportedAt(DateTimeOffset importedAt)
    {
        if (importedAt != default)
        {
            ImportedAt = importedAt;
        }
    }

    public void SetFileSize(long fileSize)
    {
        if (fileSize > 0)
        {
            FileSize = fileSize;
        }
    }

    public void SetFavorite(bool isFavorite)
    {
        IsFavorite = isFavorite;
    }

    public bool ToggleFavorite()
    {
        IsFavorite = !IsFavorite;
        return IsFavorite;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }

    public void MarkMissing()
    {
        Artwork = null;
        IsPlaying = false;
        IsMissing = true;
    }

    public void MarkAvailable()
    {
        IsMissing = false;
    }

    public static string FormatDuration(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return "--:--";
        }

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
