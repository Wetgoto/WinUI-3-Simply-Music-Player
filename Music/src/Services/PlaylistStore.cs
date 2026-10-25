using System.Text.Json;

namespace Music.Services;

/// <summary>
/// Stores the playlist in a portable config folder next to the executable,
/// falling back to the user's local app data folder when that is unavailable.
/// </summary>
public static class PlaylistStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string PrimaryPath => Path.Combine(AppPaths.ConfigDirectory, "playlist.json");

    private static string LegacyPath => Path.Combine(AppPaths.ApplicationDirectory, "playlist.json");

    private static string FallbackPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music",
        "playlist.json");

    public static async Task<IReadOnlyList<string>> LoadPathsAsync()
    {
        foreach (var path in new[] { PrimaryPath, LegacyPath, FallbackPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var playlist = JsonSerializer.Deserialize<PlaylistFile>(json);
                if (playlist?.Paths is null)
                {
                    continue;
                }

                return Normalize(playlist.Paths);
            }
            catch
            {
                // Try the next location if this file is unreadable.
            }
        }

        return [];
    }

    public static void SavePaths(IEnumerable<string> paths)
    {
        var playlist = new PlaylistFile
        {
            Version = CurrentVersion,
            Paths = Normalize(paths).ToList()
        };

        try
        {
            var json = JsonSerializer.Serialize(playlist, JsonOptions);
            WriteFile(PrimaryPath, json);
        }
        catch
        {
            try
            {
                var json = JsonSerializer.Serialize(playlist, JsonOptions);
                WriteFile(FallbackPath, json);
            }
            catch
            {
                // A missing playlist file must never crash playback.
            }
        }
    }

    private static void WriteFile(string path, string json)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }

        return result;
    }

    private sealed class PlaylistFile
    {
        public int Version { get; set; } = CurrentVersion;

        public List<string> Paths { get; set; } = [];
    }
}
