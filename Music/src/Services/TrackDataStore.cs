using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Music.Models;

namespace Music.Services;

public sealed class TrackDataRecord
{
    public int SchemaVersion { get; set; } = 1;

    public string Path { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; }

    public long FileSize { get; set; }

    public DateTimeOffset FileLastWriteUtc { get; set; }

    public string? Title { get; set; }

    public string? Artist { get; set; }

    public string? Album { get; set; }

    public string? AlbumArtist { get; set; }

    public long DurationTicks { get; set; }

    public uint Bitrate { get; set; }

    public string? FileType { get; set; }

    public bool Favorite { get; set; }
}

/// <summary>
/// Per-track JSON records under data\tracks. A stable hash of the canonical
/// path is used as the file name, so importing/reordering never rewrites the
/// whole library. Writes are made through a temporary file and an atomic
/// replace to avoid partial/corrupt records.
/// </summary>
public static class TrackDataStore
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task EnsureRecordAsync(Track track, bool forceWrite = false)
    {
        if (track.IsMissing || string.IsNullOrWhiteSpace(track.Path))
        {
            return;
        }

        var canonicalPath = GetCanonicalPath(track.Path);
        var file = new FileInfo(canonicalPath);
        if (!file.Exists)
        {
            return;
        }

        track.SetFileSize(file.Length);
        var existing = await ReadAsync(canonicalPath);
        if (existing is not null)
        {
            track.SetImportedAt(existing.ImportedAt == default
                ? DateTimeOffset.Now
                : existing.ImportedAt);

            if (!forceWrite)
            {
                track.SetFavorite(existing.Favorite);
            }

            if (!forceWrite &&
                existing.FileSize == file.Length &&
                existing.FileLastWriteUtc == file.LastWriteTimeUtc)
            {
                return;
            }

            await WriteAsync(CreateRecord(track, file, existing.ImportedAt));
            return;
        }

        var importedAt = track.ImportedAt == default
            ? DateTimeOffset.Now
            : track.ImportedAt;
        track.SetImportedAt(importedAt);
        await WriteAsync(CreateRecord(track, file, importedAt));
    }

    public static Task PersistFavoriteAsync(Track track)
    {
        return EnsureRecordAsync(track, forceWrite: true);
    }

    public static async Task<DateTimeOffset?> GetImportedAtAsync(string path)
    {
        try
        {
            var record = await ReadAsync(GetCanonicalPath(path));
            return record?.ImportedAt;
        }
        catch
        {
            return null;
        }
    }

    private static TrackDataRecord CreateRecord(
        Track track,
        FileInfo file,
        DateTimeOffset importedAt)
    {
        return new TrackDataRecord
        {
            SchemaVersion = CurrentSchemaVersion,
            Path = GetCanonicalPath(track.Path),
            ImportedAt = importedAt,
            FileSize = file.Length,
            FileLastWriteUtc = file.LastWriteTimeUtc,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            DurationTicks = track.Duration.Ticks,
            Bitrate = track.Bitrate,
            FileType = track.FileType,
            Favorite = track.IsFavorite
        };
    }

    private static async Task<TrackDataRecord?> ReadAsync(string canonicalPath)
    {
        var recordPath = GetRecordPath(canonicalPath);
        if (!File.Exists(recordPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(recordPath);
            var record = JsonSerializer.Deserialize<TrackDataRecord>(json, JsonOptions);
            if (record is null ||
                record.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(record.Path, canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return record;
        }
        catch
        {
            // A damaged record must not prevent playback. It will be rewritten
            // the next time the track is loaded.
            return null;
        }
    }

    private static async Task WriteAsync(TrackDataRecord record)
    {
        Directory.CreateDirectory(AppPaths.TrackDataDirectory);
        var recordPath = GetRecordPath(record.Path);
        var temporaryPath = $"{recordPath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(record, JsonOptions);

        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, recordPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static string GetRecordPath(string canonicalPath)
    {
        var key = canonicalPath.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return System.IO.Path.Combine(
            AppPaths.TrackDataDirectory,
            $"{Convert.ToHexString(hash).ToLowerInvariant()}.json");
    }

    private static string GetCanonicalPath(string path)
    {
        return System.IO.Path.GetFullPath(path);
    }
}
