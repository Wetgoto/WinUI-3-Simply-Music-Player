using System.Text.Json;

namespace Music.Services;

/// <summary>
/// Small, portable recent-project list stored next to the playlist.
/// </summary>
public static class RecentStore
{
    private static string PrimaryPath => Path.Combine(AppPaths.ConfigDirectory, "recent.json");

    public static async Task<IReadOnlyList<string>> LoadPathsAsync()
    {
        try
        {
            if (!File.Exists(PrimaryPath))
            {
                return [];
            }

            await using var stream = File.OpenRead(PrimaryPath);
            var file = await JsonSerializer.DeserializeAsync<RecentFile>(stream);
            return file?.Paths?.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(
                StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void SavePaths(IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            var file = new RecentFile
            {
                Paths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Take(50).ToList()
            };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            var temporary = Path.Combine(
                AppPaths.ConfigDirectory,
                $"recent.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, json);
            File.Move(temporary, PrimaryPath, overwrite: true);
        }
        catch
        {
            // The recent list is optional.
        }
    }

    private sealed class RecentFile
    {
        public List<string> Paths { get; set; } = [];
    }
}
