using System.Text.Json;

namespace Music.Services;

public sealed class PlaylistCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Paths { get; set; } = [];
}

/// <summary>
/// Persists user-created playlist names; the default queue playlist is fixed.
/// </summary>
public static class PlaylistCatalogStore
{
    private static string FilePath => Path.Combine(AppPaths.ConfigDirectory, "playlists.json");

    public static IReadOnlyList<PlaylistCatalogEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize<PlaylistCatalogFile>(json);
            return file?.Playlists?
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<PlaylistCatalogEntry> playlists)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            var file = new PlaylistCatalogFile
            {
                Playlists = playlists.ToList()
            };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            var temporary = Path.Combine(
                AppPaths.ConfigDirectory,
                $"playlists.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch
        {
            // Playlist persistence is best-effort.
        }
    }

    private sealed class PlaylistCatalogFile
    {
        public List<PlaylistCatalogEntry> Playlists { get; set; } = [];
    }
}
