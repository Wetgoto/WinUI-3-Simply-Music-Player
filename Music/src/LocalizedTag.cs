namespace Music;

/// <summary>
/// Stores the simplified-Chinese text of a static string together with its
/// English translation. XAML keeps it as text in the Tag attribute, which the
/// window parses back whenever the interface language changes.
/// </summary>
public sealed class LocalizedTag
{
    public LocalizedTag()
    {
    }

    public LocalizedTag(string text, string english)
    {
        Text = text;
        English = string.IsNullOrEmpty(english) ? text : english;
    }

    /// <summary>Simplified Chinese text, used when the app language is Chinese.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>English text, used when the app language is English.</summary>
    public string English { get; set; } = string.Empty;

    public override string ToString() => Format(Text, English);

    public static LocalizedTag Parse(string value)
    {
        TryParse(value, out var tag);
        return tag;
    }

    /// <summary>
    /// Parses the text form used in XAML, for example
    /// local:LocalizedTag(Text='播放', English='Play').
    /// </summary>
    public static bool TryParse(object? value, out LocalizedTag tag)
    {
        tag = new LocalizedTag();
        if (value is LocalizedTag existing)
        {
            tag = existing;
            return true;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var chinese = Extract(text, "Text");
        if (chinese is null)
        {
            return false;
        }

        tag = new LocalizedTag(chinese, Extract(text, "English") ?? chinese);
        return true;
    }

    private static string Format(string text, string english) =>
        string.IsNullOrEmpty(english) || english == text
            ? $"local:LocalizedTag(Text='{text}')"
            : $"local:LocalizedTag(Text='{text}', English='{english}')";

    private static string? Extract(string text, string key)
    {
        var marker = key + "='";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = text.IndexOf('\'', start);
        return end < 0 ? null : text[start..end];
    }
}
