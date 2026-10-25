namespace Music.Services;

/// <summary>
/// Resolves folders next to the real executable. This also works for
/// single-file publishing, where AppContext.BaseDirectory points at the
/// temporary extraction directory instead of the application folder.
/// </summary>
public static class AppPaths
{
    public static string ApplicationDirectory
    {
        get
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath);
            return string.IsNullOrWhiteSpace(directory)
                ? AppContext.BaseDirectory
                : directory;
        }
    }

    public static string ConfigDirectory => Path.Combine(ApplicationDirectory, "config");

    public static string DataDirectory => Path.Combine(ApplicationDirectory, "data");

    public static string TrackDataDirectory => Path.Combine(DataDirectory, "tracks");

    public static string LogsDirectory => Path.Combine(ApplicationDirectory, "logs");
}
