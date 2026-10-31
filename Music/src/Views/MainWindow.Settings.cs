using System.Text.RegularExpressions;
using System.Reflection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Music.Models;
using Music.Services;
using Windows.UI;

namespace Music;

public sealed partial class MainWindow
{
    private AppSettings _settings = new();
    private ThemePreference _themePreference = ThemePreference.System;
    private AppLanguage _language = AppLanguage.ChineseSimplified;
    private bool _isApplyingSettings = true;
    private bool _transparentBackground = true;

    private void InitializeSettings()
    {
        _settings = AppSettingsStore.Load();
        _themePreference = ParseTheme(_settings.Theme);
        _language = ParseLanguage(_settings.Language);
        Strings.SetLanguage(_language);

        ApplyLocalizedStaticText();
        ApplyTheme(_themePreference, persist: false);
        ApplyTransparentBackground(_settings.TransparentBackground);
        UpdateSettingsUi();
    }

    private void UpdateSettingsUi()
    {
        _isApplyingSettings = true;

        ThemeComboBox.SelectedIndex = _themePreference switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };
        LanguageComboBox.SelectedIndex = _language == AppLanguage.English ? 1 : 0;
        PromptPlaylistNameSwitch.IsOn = _settings.PromptPlaylistName;
        TransparentBackgroundSwitch.IsOn = _settings.TransparentBackground;
        ConfirmDeleteSwitch.IsOn = _settings.ConfirmBeforeDelete;

        // After the selection is in place, so the closed combo shows the
        // translated choice rather than the XAML default text.
        LocalizeThemeItems();

        var informational = GetInformationalVersion();
        AppVersionText.Text = GetAlphaLabel(informational);
        AppVersionDetailText.Text = Strings.VersionLine(informational);
        AppVersionTechText.Text = Strings.SdkLine;

        UpdateSwitchStateLabels();

        _isApplyingSettings = false;
    }

    /// <summary>
    /// The on/off word beside each switch is its own text block: the built-in
    /// toggle content sits to the right of the switch, and every row needs the
    /// switch itself flush with the card's right edge, in one column.
    /// </summary>
    private void UpdateSwitchStateLabels()
    {
        UpdateSwitchStateLabel(PromptPlaylistNameSwitch, PromptPlaylistNameStateText);
        UpdateSwitchStateLabel(TransparentBackgroundSwitch, TransparentBackgroundStateText);
        UpdateSwitchStateLabel(ConfirmDeleteSwitch, ConfirmDeleteStateText);
    }

    private static void UpdateSwitchStateLabel(ToggleSwitch toggle, TextBlock label)
    {
        if (toggle is null || label is null)
        {
            return;
        }

        label.Text = toggle.IsOn ? Strings.SwitchOn : Strings.SwitchOff;
    }

    private static ThemePreference ParseTheme(string? value)
    {
        return Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var theme)
            ? theme
            : ThemePreference.System;
    }

    /// <summary>
    /// Translates the theme choices. ComboBox items live in the popup, so the
    /// visual-tree walk in ApplyLocalizedTags never reaches them while the
    /// popup is closed.
    /// </summary>
    private void LocalizeThemeItems()
    {
        foreach (var item in ThemeComboBox.Items.OfType<ComboBoxItem>())
        {
            LocalizedValue(item);
        }

        ThemeComboBox.SelectedItem = LocalizedValue(ThemeComboBox.SelectedItem);
    }

    private static AppLanguage ParseLanguage(string? value)
    {
        return string.Equals(value, "en-US", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "English", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.English
            : AppLanguage.ChineseSimplified;
    }

    /// <summary>
    /// Text that never changes at runtime (labels, page titles, empty states,
    /// tooltips) is stored in the XAML Tag attribute and assigned here in the
    /// language chosen in the settings page.
    /// </summary>
    private void ApplyLocalizedStaticText()
    {
        ApplyLocalizedTags(RootGrid);

        SearchScopeText.Text = Strings.SearchScopeAll;

        AutomationProperties.SetName(QueueList, Strings.Queue);
        AutomationProperties.SetName(RecentList, Strings.RecentTracks);
        AutomationProperties.SetName(CustomPlaylistList, Strings.CustomTracks);
        AutomationProperties.SetName(SearchList, Strings.SearchResults);

        DefaultListTitleText.Text = Strings.DefaultList;
        UpdateRepeatUi();
        UpdateTrackFavoriteButtons(_currentTrack);
        UpdateVolumeIcon();
        UpdateTransportUi();
        RefreshQueueUi();
        RefreshRecentUi();
        RefreshCustomPlaylistUi();
        RefreshSearchPanels();

        if (_currentTrack is null)
        {
            ResetNowPlaying();
        }
        else
        {
            // The tag-based pass above rewrites the "not playing" placeholders
            // into these labels, so a playing track must be re-applied or the
            // player would fall back to "Nothing playing" after a language
            // switch while the audio keeps playing.
            UpdateNowPlaying(_currentTrack);
        }
    }

    private static void ApplyLocalizedTags(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is FrameworkElement element && LocalizedTag.TryParse(element.Tag, out var tag))
            {
                ApplyLocalizedTag(element, tag);
            }
            else if (child is TextBlock label && !LocalizedTag.TryParse(label.Tag, out _))
            {
                // A Tag on the Button's AutomationProperties.Name applies to the
                // button, not to the TextBlock inside it, so look the label up in
                // the shared name table instead.
                var chinese = string.IsNullOrWhiteSpace(label.Text)
                    ? AutomationProperties.GetName(label)
                    : label.Text;
                if (chinese.Length > 0 && DescriptiveNamePattern().IsMatch(chinese))
                {
                    ApplyLocalizedTag(label, new LocalizedTag(chinese, EnglishFor(chinese)));
                }
            }

            // Content controls keep their children in their content template,
            // which only exists once the control is realised; walk the content
            // directly so pages that were never shown are translated too.
            if (child is ContentControl { Content: DependencyObject content })
            {
                ApplyLocalizedTags(content);
            }

            ApplyLocalizedTags(child);
        }
    }

    // A Tag set on a Button's AutomationProperties.Name applies to the button
    // itself, so Chinese-named TextBlocks inside buttons carry no Tag. Their
    // names come from the Chinese-to-English table instead.
    private static readonly Dictionary<string, string> AutomationNameEnglish =
        new(StringComparer.Ordinal)
        {
            ["最近的项目"] = "Recently played",
            ["播放列表"] = "Playlists",
            ["默认列表"] = "Default list",
            ["添加播放列表"] = "Add playlist",
            ["设置"] = "Settings",
            ["导入音乐"] = "Import music",
            ["搜索"] = "Search",
            ["排序"] = "Sort",
            ["返回"] = "Back",
            ["播放"] = "Play",
            ["收藏"] = "Favorite",
            ["添加到播放列表"] = "Add to playlist",
            ["更多选项"] = "More options",
            ["本地音乐"] = "Local music",
            ["搜索全部音乐"] = "Search all music"
        };

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex DescriptiveNamePattern();

    private static string EnglishFor(string chinese) =>
        AutomationNameEnglish.TryGetValue(chinese, out var english) ? english : chinese;
    private static void ApplyLocalizedTag(FrameworkElement element, LocalizedTag tag)
    {
        var chinese = tag.Text;
        var english = string.IsNullOrEmpty(tag.English) ? tag.Text : tag.English;

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = Strings.T(chinese, english);
                break;

            case ComboBoxItem comboBoxItem:
                comboBoxItem.Content = Strings.T(chinese, english);
                break;

            case TextBox textBox:
                textBox.PlaceholderText = Strings.T(chinese, english);
                break;

            case AutoSuggestBox suggestBox:
                suggestBox.PlaceholderText = Strings.T(chinese, english);
                break;

            case Button button:
                ToolTipService.SetToolTip(button, Strings.T(chinese, english));
                AutomationProperties.SetName(button, Strings.T(chinese, english));
                break;

            case Slider slider:
                ToolTipService.SetToolTip(slider, Strings.T(chinese, english));
                AutomationProperties.SetName(slider, Strings.T(chinese, english));
                break;

            default:
                AutomationProperties.SetName(element, Strings.T(chinese, english));
                break;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var language = LanguageComboBox.SelectedIndex == 1
            ? AppLanguage.English
            : AppLanguage.ChineseSimplified;

        if (language == _language)
        {
            return;
        }

        _language = language;
        Strings.SetLanguage(language);
        _settings.Language = language == AppLanguage.English ? "en-US" : "zh-CN";
        AppSettingsStore.Save(_settings);

        // Translate the selected item in place first: refreshing the combo
        // below would restore the original Chinese text if it ran afterwards.
        LanguageComboBox.SelectedItem = LocalizedValue(LanguageComboBox.SelectedItem);

        ApplyLocalizedStaticText();

        // The on/off words are set in code, so the tag pass above would have
        // rewritten them; restore them in the newly selected language.
        UpdateSwitchStateLabels();

        // The version lines are built from the assembly at runtime, so they are
        // re-rendered here after a language switch instead of coming from Tag.
        var informational = GetInformationalVersion();
        AppVersionText.Text = GetAlphaLabel(informational);
        AppVersionDetailText.Text = Strings.VersionLine(informational);
        AppVersionTechText.Text = Strings.SdkLine;
        LocalizeThemeItems();

        if (_taskbarService is not null)
        {
            _taskbarService.SetToolTips(
                Strings.PreviousTrack,
                Strings.PlayPause,
                Strings.NextTrack);
        }

        // Rows and menus created in code keep whatever language was active
        // when they were built, so refresh the page the router is on.
        if (_currentPage == AppPage.Recent)
        {
            RefreshRecentUi();
        }
        else if (_currentPage == AppPage.CustomPlaylist &&
                 _selectedCustomPlaylist is not null)
        {
            _ = LoadCustomPlaylistTracksAsync(_selectedCustomPlaylist);
        }

        if (_searchPageOpen && _lastSearchQuery.Length > 0)
        {
            RefreshSearchResults(_lastSearchQuery);
        }

        SetStatus(Strings.StatusLanguage(language == AppLanguage.English ? "English" : "简体中文"));
    }

    /// <summary>
    /// Replaces a ComboBoxItem's content with its translation without changing
    /// the selection, so assigning SelectedItem does not fire another event.
    /// </summary>
    private static object? LocalizedValue(object? item)
    {
        if (item is ComboBoxItem { Tag: LocalizedTag tag } comboItem)
        {
            comboItem.Content = Strings.T(tag.Text, tag.English);
        }

        return item;
    }

    /// <summary>
    /// Sizes the settings cards to the available width, capped at 820 px. The
    /// panel is centred, so wide windows show a centred column and narrow ones
    /// use the full width.
    /// </summary>
    private void SettingsPanelHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SettingsPanel is null || SettingsPanelHost is null)
        {
            return;
        }

        // e.NewSize is the space the cards can occupy (the ScrollViewer minus
        // its padding). Capping it at 820 keeps the column readable, and the
        // panel's HorizontalAlignment="Center" centres the leftover space.
        var available = e.NewSize.Width;
        if (available <= 0)
        {
            return;
        }

        SettingsPanel.Width = Math.Min(available, 820);
    }

    private void ApplyTheme(ThemePreference preference, bool persist)
    {
        _themePreference = preference;
        RootGrid.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        var dark = IsDarkThemeEffective;

        ApplyNativeTitleBarTheme(dark);
        ApplyCustomTitleBarTheme(dark);

        if (NavigationRail is not null)
        {
            NavigationRail.Background = CreateSidebarBackgroundBrush();
        }

        if (_selectedNavButton is not null)
        {
            _selectedNavButton.Background = CreateNavSelectionBrush();
        }

        // Repaint the opaque background in the new theme's colour.
        if (!_transparentBackground)
        {
            ApplyTransparentBackground(false);
        }

        if (persist)
        {
            _settings.Theme = preference.ToString();
            AppSettingsStore.Save(_settings);
        }
    }

    private bool IsDarkThemeEffective =>
        _themePreference == ThemePreference.Dark ||
        (_themePreference == ThemePreference.System &&
         Application.Current.RequestedTheme == ApplicationTheme.Dark);

    /// <summary>
    /// The sidebar is a translucent tint rather than a solid fill. A solid fill
    /// hides the Mica backdrop, which leaves the rail a flat darker block next
    /// to the tinted content area; a semi-transparent wash keeps both surfaces
    /// reading as one window while still separating the rail.
    /// </summary>
    private SolidColorBrush CreateSidebarBackgroundBrush()
    {
        return new SolidColorBrush(IsDarkThemeEffective
            ? Windows.UI.Color.FromArgb(40, 0, 0, 0)
            : Windows.UI.Color.FromArgb(18, 0, 0, 0));
    }

    /// <summary>
    /// Selected sidebar rows and active page-search buttons use a translucent
    /// overlay; white works on dark themes, black on light ones.
    /// </summary>
    private SolidColorBrush CreateNavSelectionBrush()
    {
        return new SolidColorBrush(IsDarkThemeEffective
            ? Windows.UI.Color.FromArgb(36, 255, 255, 255)
            : Windows.UI.Color.FromArgb(22, 0, 0, 0));
    }

    private void PromptPlaylistNameSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings || PromptPlaylistNameSwitch is null)
        {
            return;
        }

        _settings.PromptPlaylistName = PromptPlaylistNameSwitch.IsOn;
        AppSettingsStore.Save(_settings);
        UpdateSwitchStateLabels();
    }

    /// <summary>
    /// Turns the translucent window backdrop on or off. Acrylic is preferred
    /// where available, Mica otherwise, and an opaque theme background when
    /// the effect is disabled or unsupported.
    /// </summary>
    private void ApplyTransparentBackground(bool transparent)
    {
        _transparentBackground = transparent;

        if (transparent && DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else if (transparent && MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else
        {
            // No backdrop: the window paints an opaque theme colour instead.
            SystemBackdrop = null;
        }

        // A solid window needs an explicit background; without a backdrop the
        // root grid would otherwise show whatever is behind the window. The
        // colour is resolved from the current theme each time.
        RootGrid.Background = transparent
            ? null
            : new SolidColorBrush(IsDarkThemeEffective
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243));
    }

    private void TransparentBackgroundSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings || TransparentBackgroundSwitch is null)
        {
            return;
        }

        ApplyTransparentBackground(TransparentBackgroundSwitch.IsOn);
        _settings.TransparentBackground = TransparentBackgroundSwitch.IsOn;
        AppSettingsStore.Save(_settings);
        UpdateSwitchStateLabels();
    }

    private void ConfirmDeleteSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings || ConfirmDeleteSwitch is null)
        {
            return;
        }

        _settings.ConfirmBeforeDelete = ConfirmDeleteSwitch.IsOn;
        AppSettingsStore.Save(_settings);
        UpdateSwitchStateLabels();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var preference = ThemeComboBox.SelectedIndex switch
        {
            1 => ThemePreference.Light,
            2 => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        if (preference == _themePreference)
        {
            return;
        }

        ApplyTheme(preference, persist: true);
        SetStatus(Strings.StatusTheme(ThemeName(preference)));
    }

    private static string ThemeName(ThemePreference preference)
    {
        return preference switch
        {
            ThemePreference.Light => Strings.LightTheme,
            ThemePreference.Dark => Strings.DarkTheme,
            _ => Strings.SystemTheme
        };
    }

    private static string GetInformationalVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : informational;
    }

    private static string GetAlphaLabel(string informational)
    {
        var numeric = informational.Split('-', '+')[0];
        var parts = numeric.Split('.');

        return parts.Length >= 2
            ? $"Alpha {parts[0]}.{parts[1]}"
            : "Alpha";
    }
}
