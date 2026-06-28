using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Wpf.Ui.Appearance;

namespace Pulse1x.App.ViewModels;

/// <summary>Opção de idioma exibida no seletor. O rótulo aparece no próprio idioma (não se traduz).</summary>
public sealed record LanguageOption(string Label, AppLanguage Value);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly Action<int> _onIntervalChanged;
    private readonly Action<bool> _onMinimizeToTrayChanged;

    [ObservableProperty] private bool isDarkTheme;
    [ObservableProperty] private int updateIntervalMs;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool minimizeToTray;
    [ObservableProperty] private bool animationsEnabled;
    [ObservableProperty] private LanguageOption selectedLanguage;

    public int[] AvailableIntervalsMs { get; } = { 500, 1000, 2000, 5000 };

    public LanguageOption[] Languages { get; } =
    {
        new("Português", AppLanguage.Portuguese),
        new("English", AppLanguage.English),
    };

    public SettingsViewModel(SettingsService settingsService, Action<int> onIntervalChanged, Action<bool> onMinimizeToTrayChanged)
    {
        _settingsService = settingsService;
        _onIntervalChanged = onIntervalChanged;
        _onMinimizeToTrayChanged = onMinimizeToTrayChanged;

        var current = settingsService.Current;
        isDarkTheme = current.DarkTheme;
        updateIntervalMs = current.UpdateIntervalMs;
        startWithWindows = current.StartWithWindows;
        minimizeToTray = current.MinimizeToTray;
        animationsEnabled = current.AnimationsEnabled;
        selectedLanguage = Languages.FirstOrDefault(l => l.Value == Loc.Instance.Language) ?? Languages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        Loc.Instance.SetLanguage(value.Value);
        _settingsService.Current.Language = Loc.Instance.LanguageCode;
        _settingsService.Save();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _settingsService.Current.DarkTheme = value;
        _settingsService.Save();
        ApplicationThemeManager.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    partial void OnUpdateIntervalMsChanged(int value)
    {
        _settingsService.Current.UpdateIntervalMs = value;
        _settingsService.Save();
        _onIntervalChanged(value);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settingsService.SetStartWithWindows(value);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.Current.MinimizeToTray = value;
        _settingsService.Save();
        _onMinimizeToTrayChanged(value);
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        AnimationSettings.Enabled = value;
        _settingsService.Current.AnimationsEnabled = value;
        _settingsService.Save();
    }
}
