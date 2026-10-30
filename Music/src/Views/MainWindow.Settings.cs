using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Music.Models;
using Music.Services;

namespace Music;

public sealed partial class MainWindow
{
    private AppSettings _settings = new();
    private ThemePreference _themePreference = ThemePreference.System;
    private bool _isApplyingSettings = true;

    private void InitializeSettings()
    {
        _settings = AppSettingsStore.Load();
        _themePreference = ParseTheme(_settings.Theme);
        ApplyTheme(_themePreference, persist: false);
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
        LanguageComboBox.SelectedIndex = 0;

        var informational = GetInformationalVersion();
        AppVersionText.Text = GetAlphaLabel(informational);
        AppVersionDetailText.Text = $"版本 {informational} · WinUI 3 · .NET 10";
        AppVersionTechText.Text = "Windows App SDK 1.8 · MIT License";

        _isApplyingSettings = false;
    }

    private static ThemePreference ParseTheme(string? value)
    {
        return Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var theme)
            ? theme
            : ThemePreference.System;
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

        if (_selectedNavButton is not null)
        {
            _selectedNavButton.Background = CreateNavSelectionBrush();
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
    /// Selected sidebar rows and active page-search buttons use a translucent
    /// overlay; white works on dark themes, black on light ones.
    /// </summary>
    private SolidColorBrush CreateNavSelectionBrush()
    {
        return new SolidColorBrush(IsDarkThemeEffective
            ? Windows.UI.Color.FromArgb(36, 255, 255, 255)
            : Windows.UI.Color.FromArgb(22, 0, 0, 0));
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
        SetStatus($"界面主题：{ThemeName(preference)}");
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.Language = "zh-CN";
        AppSettingsStore.Save(_settings);
    }

    private static string ThemeName(ThemePreference preference)
    {
        return preference switch
        {
            ThemePreference.Light => "浅色",
            ThemePreference.Dark => "深色",
            _ => "跟随系统"
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
