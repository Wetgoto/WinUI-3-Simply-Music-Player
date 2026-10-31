using System.Text.Json;

namespace Music.Services;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>"zh-CN" (default) or "en-US".</summary>
    public string Language { get; set; } = "zh-CN";
}

/// <summary>
/// Stores user preferences next to the executable, with the same
/// local-app-data fallback used by the other configuration stores.
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string PrimaryPath => Path.Combine(AppPaths.ConfigDirectory, "settings.json");

    private static string FallbackPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music",
        "settings.json");

    public static AppSettings Load()
    {
        foreach (var path in new[] { PrimaryPath, FallbackPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
            catch
            {
                // Try the next location if this file is unreadable.
            }
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Write(PrimaryPath, settings);
        }
        catch
        {
            try
            {
                Write(FallbackPath, settings);
            }
            catch
            {
                // A missing settings file must never break the player.
            }
        }
    }

    private static void Write(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
