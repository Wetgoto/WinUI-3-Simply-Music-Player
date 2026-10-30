namespace Music.Models;

/// <summary>
/// How playback continues when the current track ends, or when the queue runs out.
/// </summary>
public enum RepeatMode
{
    /// <summary>Play through the queue once, then stop.</summary>
    None,

    /// <summary>Wrap around and keep playing the whole queue.</summary>
    All,

    /// <summary>Replay the current track forever.</summary>
    One
}
