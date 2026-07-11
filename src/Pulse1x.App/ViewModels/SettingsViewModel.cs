using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Wpf.Ui.Appearance;

namespace Pulse1x.App.ViewModels;

/// <summary>Opção de idioma exibida no seletor. O rótulo aparece no próprio idioma (não se traduz).</summary>
public sealed record LanguageOption(string Label, AppLanguage Value);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly GitHubUpdateService _updateService;
    private readonly Action<int> _onIntervalChanged;
    private readonly Action<bool> _onMinimizeToTrayChanged;

    private UpdateCheckResult? _lastCheck;

    [ObservableProperty] private bool isDarkTheme;
    [ObservableProperty] private int updateIntervalMs;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool minimizeToTray;
    [ObservableProperty] private bool animationsEnabled;
    [ObservableProperty] private LanguageOption selectedLanguage;

    // ---- Atualização do aplicativo (via Releases do GitHub) ----
    [ObservableProperty] private bool isCheckingUpdate;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool isUpdateAvailable;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool isDownloadingUpdate;
    [ObservableProperty] private double downloadProgress;
    [ObservableProperty] private string updateStatusText = "";
    public string CurrentVersionText => $"v{GitHubUpdateService.CurrentVersion.ToString(3)}";

    public int[] AvailableIntervalsMs { get; } = { 500, 1000, 2000, 5000 };

    public LanguageOption[] Languages { get; } =
    {
        new("Português", AppLanguage.Portuguese),
        new("English", AppLanguage.English),
    };

    public SettingsViewModel(SettingsService settingsService, GitHubUpdateService updateService,
        Action<int> onIntervalChanged, Action<bool> onMinimizeToTrayChanged)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _onIntervalChanged = onIntervalChanged;
        _onMinimizeToTrayChanged = onMinimizeToTrayChanged;

        var current = settingsService.Current;
        isDarkTheme = current.DarkTheme;
        updateIntervalMs = current.UpdateIntervalMs;
        startWithWindows = current.StartWithWindows;
        minimizeToTray = current.MinimizeToTray;
        animationsEnabled = current.AnimationsEnabled;
        selectedLanguage = Languages.FirstOrDefault(l => l.Value == Loc.Instance.Language) ?? Languages[0];

        updateStatusText = Loc.S("Settings_UpdateCheckIdle");
        // Checagem silenciosa em segundo plano ao abrir o app — não bloqueia a UI nem incomoda
        // o usuário se falhar (sem internet, GitHub fora do ar): o texto de status simplesmente
        // permanece "verificando" até o resultado chegar, sem diálogos.
        _ = CheckForUpdatesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return;
        IsCheckingUpdate = true;
        IsUpdateAvailable = false;
        UpdateStatusText = Loc.S("Settings_UpdateChecking");
        try
        {
            _lastCheck = await _updateService.CheckAsync();
            UpdateStatusText = _lastCheck.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => Loc.F("Settings_UpdateAvailable", _lastCheck.LatestVersion ?? "?"),
                UpdateCheckStatus.UpToDate => Loc.S("Settings_UpdateUpToDate"),
                _ => Loc.S("Settings_UpdateCheckFailed"),
            };
            IsUpdateAvailable = _lastCheck.Status == UpdateCheckStatus.UpdateAvailable;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private bool CanInstallUpdate() => IsUpdateAvailable && !IsDownloadingUpdate;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_lastCheck is not { Status: UpdateCheckStatus.UpdateAvailable, DownloadUrl: { } url, AssetName: { } name })
            return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        UpdateStatusText = Loc.S("Settings_UpdateDownloading");
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = Math.Round(p * 100));
            bool started = await _updateService.DownloadAndInstallAsync(url, name, progress);
            UpdateStatusText = started ? Loc.S("Settings_UpdateInstalling") : Loc.S("Settings_UpdateDownloadFailed");
        }
        finally
        {
            IsDownloadingUpdate = false;
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
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
