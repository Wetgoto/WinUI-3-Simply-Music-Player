using Music.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Music.Services;

/// <summary>
/// Reads display metadata and cover art through the Windows storage APIs.
/// </summary>
public static class MusicLibrary
{
    public static readonly string[] SupportedExtensions =
    [
        ".mp3",
        ".m4a",
        ".m4b",
        ".aac",
        ".wav",
        ".flac",
        ".ogg",
        ".opus",
        ".wma",
        ".aif",
        ".aiff",
        ".alac"
    ];

    public static bool IsSupported(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<Track> LoadAsync(StorageFile file)
    {
        var track = new Track(file);
        EmbeddedMetadata? embedded = null;

        try
        {
            using var stream = await file.OpenStreamForReadAsync();
            if (stream.CanSeek)
            {
                embedded = await Task.Run(() => EmbeddedMetadataReader.Read(stream));
            }
        }
        catch
        {
            // Fall through to the path-based reader.
        }

        if (embedded is null && !string.IsNullOrWhiteSpace(file.Path))
        {
            try
            {
                embedded = await Task.Run(() => EmbeddedMetadataReader.Read(file.Path));
            }
            catch
            {
                // Embedded tags are an optional enhancement.
            }
        }

        try
        {
            var properties = await file.Properties.GetMusicPropertiesAsync();
            track.ApplyMetadata(
                properties.Title,
                properties.Artist,
                properties.Album,
                properties.AlbumArtist,
                properties.Duration,
                properties.Bitrate);
        }
        catch
        {
            // Some files have no readable property store; the file name is a fine fallback.
        }

        if (embedded is not null)
        {
            track.ApplyMetadata(
                embedded.Title,
                embedded.Artist,
                embedded.Album,
                embedded.AlbumArtist,
                embedded.Duration,
                embedded.Bitrate);
        }

        try
        {
            ImageSource? artwork = null;
            if (embedded?.Picture is { Length: > 0 })
            {
                track.SetArtworkBytes(embedded.Picture);
                try
                {
                    artwork = await LoadArtworkAsync(embedded.Picture);
                }
                catch
                {
                    // Fall back to the shell thumbnail below.
                }
            }

            artwork ??= await LoadArtworkAsync(file);
            if (artwork is not null)
            {
                track.SetArtwork(artwork);
            }
        }
        catch
        {
            // Cover art is optional.
        }

        await SaveTrackDataAsync(track);
        return track;
    }

    public static async Task<Track> LoadAsync(string path)
    {
        var track = new Track(path);

        try
        {
            var embedded = await Task.Run(() => EmbeddedMetadataReader.Read(path));
            if (embedded is not null)
            {
                track.ApplyMetadata(
                    embedded.Title,
                    embedded.Artist,
                    embedded.Album,
                    embedded.AlbumArtist,
                    embedded.Duration,
                    embedded.Bitrate);

                if (embedded.Picture is { Length: > 0 })
                {
                    track.SetArtworkBytes(embedded.Picture);
                    var artwork = await LoadArtworkAsync(embedded.Picture);
                    if (artwork is not null)
                    {
                        track.SetArtwork(artwork);
                    }
                }
            }
        }
        catch
        {
            // Path-based fallback metadata is optional.
        }

        await SaveTrackDataAsync(track);
        return track;
    }

    private static async Task SaveTrackDataAsync(Track track)
    {
        try
        {
            await TrackDataStore.EnsureRecordAsync(track);
        }
        catch
        {
            // Track metadata persistence must never break playback.
        }
    }

    private static async Task<ImageSource?> LoadArtworkAsync(byte[] picture)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(picture);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var bitmap = new BitmapImage
        {
            DecodePixelWidth = 400
        };
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static async Task<ImageSource?> LoadArtworkAsync(StorageFile file)
    {
        using var thumbnail = await file.GetThumbnailAsync(
            ThumbnailMode.MusicView,
            400,
            ThumbnailOptions.UseCurrentScale);

        if (thumbnail is null || thumbnail.Size == 0)
        {
            return null;
        }

        // Copy the thumbnail into memory so the image does not depend on the file stream.
        using var buffer = new InMemoryRandomAccessStream();
        await RandomAccessStream.CopyAsync(thumbnail, buffer);
        buffer.Seek(0);

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = 400
        };
        await bitmap.SetSourceAsync(buffer);
        return bitmap;
    }
}
