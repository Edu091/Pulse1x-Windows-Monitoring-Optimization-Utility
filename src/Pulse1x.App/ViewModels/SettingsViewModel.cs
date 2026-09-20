using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;
using Wpf.Ui.Appearance;

namespace Pulse1x.App.ViewModels;

/// <summary>Opção de idioma exibida no seletor. O rótulo aparece no próprio idioma (não se traduz).</summary>
public sealed record LanguageOption(string Label, AppLanguage Value);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly GitHubUpdateService _updateService;
    private readonly Action<int> _onIntervalChanged;
    private readonly Action<bool> _onMinimizeToTrayChanged;

    /// <summary>Evita salvar/reaplicar o tema a cada propriedade enquanto o painel é carregado.</summary>
    private bool _loadingAppearance = true;

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

    // =====================================================================================
    //  Personalização visual (vale para todo o Pulse1x, não só para o GameHub)
    // =====================================================================================

    [ObservableProperty] private string primaryColor = "#DC2626";
    [ObservableProperty] private string secondaryColor = "#B91C1C";
    [ObservableProperty] private string accentColor = "#F87171";
    [ObservableProperty] private double transparency;
    [ObservableProperty] private double blurIntensity = 0.35;
    [ObservableProperty] private double animationIntensity = 1.0;

    [ObservableProperty] private BackgroundMode backgroundMode = BackgroundMode.Default;
    [ObservableProperty] private string? backgroundImagePath;
    [ObservableProperty] private string backgroundColor = "#0B0B0F";
    [ObservableProperty] private string gradientStart = "#1A1A24";
    [ObservableProperty] private string gradientEnd = "#0B0B0F";
    [ObservableProperty] private BackgroundFit backgroundFit = BackgroundFit.Fill;
    [ObservableProperty] private double backgroundOpacity = 0.55;
    [ObservableProperty] private double backgroundBlur = 18;
    [ObservableProperty] private double backgroundDarken = 0.45;
    [ObservableProperty] private double backgroundSaturation = 1.0;
    [ObservableProperty] private bool adaptColorsToGame;

    /// <summary>Pedido de escolha da imagem de fundo, atendido pelo code-behind da página.</summary>
    public event Func<string?>? PickBackgroundImageRequested;

    /// <summary>Cores sugeridas — atalhos para quem não quer digitar um código hexadecimal.</summary>
    public string[] ColorPresets { get; } =
    {
        "#DC2626", "#EA580C", "#D97706", "#16A34A", "#0891B2",
        "#2563EB", "#7C3AED", "#DB2777", "#64748B", "#0F172A",
    };

    public SettingsViewModel(SettingsService settingsService, ThemeService themeService,
        GitHubUpdateService updateService,
        Action<int> onIntervalChanged, Action<bool> onMinimizeToTrayChanged)
    {
        _settingsService = settingsService;
        _themeService = themeService;
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

        var appearance = settingsService.Current.Appearance;
        primaryColor = appearance.PrimaryColor;
        secondaryColor = appearance.SecondaryColor;
        accentColor = appearance.AccentColor;
        transparency = appearance.Transparency;
        blurIntensity = appearance.BlurIntensity;
        animationIntensity = appearance.AnimationIntensity;
        backgroundMode = appearance.Background;
        backgroundImagePath = appearance.BackgroundImagePath;
        backgroundColor = appearance.BackgroundColor;
        gradientStart = appearance.GradientStart;
        gradientEnd = appearance.GradientEnd;
        backgroundFit = appearance.BackgroundFit;
        backgroundOpacity = appearance.BackgroundOpacity;
        backgroundBlur = appearance.BackgroundBlur;
        backgroundDarken = appearance.BackgroundDarken;
        backgroundSaturation = appearance.BackgroundSaturation;
        adaptColorsToGame = appearance.AdaptColorsToGame;

        LoadGameHubSettings();
        LoadLaunchers();
        _loadingAppearance = false;

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
            var result = await _updateService.DownloadAndInstallAsync(url, name, progress);
            UpdateStatusText = result.Started
                ? Loc.S("Settings_UpdateInstalling")
                : Loc.F("Settings_UpdateDownloadFailedDetail", result.ErrorMessage ?? "?");
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
        _themeService.Apply();
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
        ApplyAppearance();
    }

    // =====================================================================================
    //  Personalização
    // =====================================================================================

    /// <summary>
    /// Copia o que está na tela para as configurações e manda o tema se reaplicar. Todas as
    /// propriedades de aparência caem aqui, então a mudança aparece na hora em todo o app.
    /// </summary>
    private void ApplyAppearance()
    {
        if (_loadingAppearance) return;

        var appearance = _settingsService.Current.Appearance;
        appearance.PrimaryColor = PrimaryColor;
        appearance.SecondaryColor = SecondaryColor;
        appearance.AccentColor = AccentColor;
        appearance.Transparency = Transparency;
        appearance.BlurIntensity = BlurIntensity;
        appearance.AnimationIntensity = AnimationIntensity;
        appearance.Background = BackgroundMode;
        appearance.BackgroundImagePath = BackgroundImagePath;
        appearance.BackgroundColor = BackgroundColor;
        appearance.GradientStart = GradientStart;
        appearance.GradientEnd = GradientEnd;
        appearance.BackgroundFit = BackgroundFit;
        appearance.BackgroundOpacity = BackgroundOpacity;
        appearance.BackgroundBlur = BackgroundBlur;
        appearance.BackgroundDarken = BackgroundDarken;
        appearance.BackgroundSaturation = BackgroundSaturation;
        appearance.AdaptColorsToGame = AdaptColorsToGame;

        _themeService.RefreshBackgroundImage();
        _themeService.Save();
    }

    partial void OnPrimaryColorChanged(string value) => ApplyAppearance();
    partial void OnSecondaryColorChanged(string value) => ApplyAppearance();
    partial void OnAccentColorChanged(string value) => ApplyAppearance();
    partial void OnTransparencyChanged(double value) => ApplyAppearance();
    partial void OnBlurIntensityChanged(double value) => ApplyAppearance();
    partial void OnAnimationIntensityChanged(double value) => ApplyAppearance();
    partial void OnBackgroundModeChanged(BackgroundMode value) => ApplyAppearance();
    partial void OnBackgroundImagePathChanged(string? value) => ApplyAppearance();
    partial void OnBackgroundColorChanged(string value) => ApplyAppearance();
    partial void OnGradientStartChanged(string value) => ApplyAppearance();
    partial void OnGradientEndChanged(string value) => ApplyAppearance();
    partial void OnBackgroundFitChanged(BackgroundFit value) => ApplyAppearance();
    partial void OnBackgroundOpacityChanged(double value) => ApplyAppearance();
    partial void OnBackgroundBlurChanged(double value) => ApplyAppearance();
    partial void OnBackgroundDarkenChanged(double value) => ApplyAppearance();
    partial void OnBackgroundSaturationChanged(double value) => ApplyAppearance();
    partial void OnAdaptColorsToGameChanged(bool value) => ApplyAppearance();

    [RelayCommand]
    private void PickBackgroundImage()
    {
        string? file = PickBackgroundImageRequested?.Invoke();
        if (file is null) return;
        BackgroundImagePath = file;
        // Escolher uma imagem já indica a intenção: muda o modo junto, para o usuário não precisar
        // de dois passos para ver o resultado.
        BackgroundMode = BackgroundMode.Image;
    }

    [RelayCommand]
    private void PickPrimaryColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) PrimaryColor = hex;
    }

    // =====================================================================================
    //  GameHub
    // =====================================================================================

    [ObservableProperty] private bool startInGameHub;
    [ObservableProperty] private bool maximizeGameHub = true;
    [ObservableProperty] private bool immersiveGameHub = true;
    [ObservableProperty] private bool soundEnabled = true;
    [ObservableProperty] private double soundVolume = 0.35;
    [ObservableProperty] private bool onlineArtEnabled = true;
    [ObservableProperty] private bool showTitlesOnCards = true;
    [ObservableProperty] private bool scanOnOpen;
    [ObservableProperty] private CardSize cardSize = CardSize.Medium;
    [ObservableProperty] private string controllerGlyphs = "Auto";
    [ObservableProperty] private bool metricsEnabled = true;

    /// <summary>O serviço de som, para aplicar volume/arquivos e tocar a prévia ao ajustar.</summary>
    public GameHubSoundService? Sounds { get; set; }

    /// <summary>O registro de estatísticas, para ligar/desligar junto com a preferência.</summary>
    public PlayMetricsService? Metrics { get; set; }

    /// <summary>
    /// Launchers instalados nesta máquina que podem subir junto com o GameHub. A lista é montada
    /// na hora, então só aparece o que realmente existe aqui.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<LauncherToggle> AutoStartLaunchers { get; } = new();

    private void LoadLaunchers()
    {
        AutoStartLaunchers.Clear();
        var saved = _settingsService.Current.GameHub.AutoStartLaunchers;

        foreach (var launcher in new LauncherStartupService().Detect())
        {
            var toggle = new LauncherToggle(launcher.Kind, launcher.Name)
            {
                IsEnabled = saved.Contains(launcher.Kind.ToString()),
            };
            toggle.Changed += SaveLaunchers;
            AutoStartLaunchers.Add(toggle);
        }
    }

    private void SaveLaunchers()
    {
        if (_loadingAppearance) return;
        _settingsService.Current.GameHub.AutoStartLaunchers = AutoStartLaunchers
            .Where(l => l.IsEnabled)
            .Select(l => l.Kind.ToString())
            .ToList();
        _settingsService.Save();
    }

    /// <summary>Pedido de escolher um .wav para um evento, atendido pela página.</summary>
    public event Func<string?>? PickSoundRequested;

    /// <summary>
    /// Carrega as preferências do GameHub nos campos. Escrevemos nos campos, e não nas propriedades,
    /// de propósito: é a carga inicial, então não deve disparar notificação nem regravar o arquivo
    /// — daí a supressão do aviso do toolkit.
    /// </summary>
#pragma warning disable MVVMTK0034
    private void LoadGameHubSettings()
    {
        var hub = _settingsService.Current.GameHub;
        startInGameHub = hub.StartInGameHub;
        maximizeGameHub = hub.MaximizeOnOpen;
        immersiveGameHub = hub.Immersive;
        soundEnabled = hub.SoundEnabled;
        soundVolume = hub.SoundVolume;
        onlineArtEnabled = hub.OnlineArtEnabled;
        showTitlesOnCards = hub.ShowTitlesOnCards;
        scanOnOpen = hub.ScanOnOpen;
        cardSize = hub.CardSize;
        controllerGlyphs = hub.ControllerGlyphs;
        metricsEnabled = hub.MetricsEnabled;
    }
#pragma warning restore MVVMTK0034

    /// <summary>Grava as preferências do GameHub e as repassa aos serviços que já estão rodando.</summary>
    private void ApplyGameHub(bool previewSound = false)
    {
        if (_loadingAppearance) return;

        var hub = _settingsService.Current.GameHub;
        hub.StartInGameHub = StartInGameHub;
        hub.MaximizeOnOpen = MaximizeGameHub;
        hub.Immersive = ImmersiveGameHub;
        hub.SoundEnabled = SoundEnabled;
        hub.SoundVolume = SoundVolume;
        hub.OnlineArtEnabled = OnlineArtEnabled;
        hub.ShowTitlesOnCards = ShowTitlesOnCards;
        hub.ScanOnOpen = ScanOnOpen;
        hub.CardSize = CardSize;
        hub.ControllerGlyphs = ControllerGlyphs;
        hub.MetricsEnabled = MetricsEnabled;
        if (Metrics is not null) Metrics.Enabled = MetricsEnabled;

        if (Sounds is not null)
        {
            Sounds.Enabled = SoundEnabled;
            // O volume entra na síntese do tom, então os sons padrão precisam ser regerados.
            if (Math.Abs(Sounds.Volume - SoundVolume) > 0.001)
            {
                Sounds.Volume = SoundVolume;
                Sounds.RegenerateDefaults();
            }

            if (previewSound && SoundEnabled) Sounds.Play(HubSound.Navigate);
        }

        _settingsService.Save();
    }

    partial void OnStartInGameHubChanged(bool value) => ApplyGameHub();
    partial void OnMaximizeGameHubChanged(bool value) => ApplyGameHub();
    partial void OnImmersiveGameHubChanged(bool value) => ApplyGameHub();
    partial void OnSoundEnabledChanged(bool value) => ApplyGameHub(previewSound: true);
    partial void OnSoundVolumeChanged(double value) => ApplyGameHub(previewSound: true);
    partial void OnOnlineArtEnabledChanged(bool value) => ApplyGameHub();
    partial void OnShowTitlesOnCardsChanged(bool value) => ApplyGameHub();
    partial void OnScanOnOpenChanged(bool value) => ApplyGameHub();
    partial void OnCardSizeChanged(CardSize value) => ApplyGameHub();
    partial void OnControllerGlyphsChanged(string value) => ApplyGameHub();
    partial void OnMetricsEnabledChanged(bool value) => ApplyGameHub();

    /// <summary>Escolhe um .wav próprio para um dos eventos de navegação.</summary>
    [RelayCommand]
    private void PickSound(string? sound)
    {
        if (sound is null || Sounds is null) return;
        string? file = PickSoundRequested?.Invoke();
        if (file is null) return;

        Sounds.CustomSounds[sound] = file;
        _settingsService.Current.GameHub.CustomSounds[sound] = file;
        Sounds.Reset();
        _settingsService.Save();

        if (Enum.TryParse<HubSound>(sound, out var kind)) Sounds.Play(kind);
    }

    /// <summary>Volta todos os sons ao padrão gerado pelo Pulse1x.</summary>
    [RelayCommand]
    private void ResetSounds()
    {
        if (Sounds is null) return;
        Sounds.CustomSounds.Clear();
        _settingsService.Current.GameHub.CustomSounds.Clear();
        Sounds.RegenerateDefaults();
        _settingsService.Save();
        Sounds.Play(HubSound.Confirm);
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        _themeService.ResetToDefaults();

        var appearance = _settingsService.Current.Appearance;
        _loadingAppearance = true;
        PrimaryColor = appearance.PrimaryColor;
        SecondaryColor = appearance.SecondaryColor;
        AccentColor = appearance.AccentColor;
        Transparency = appearance.Transparency;
        BlurIntensity = appearance.BlurIntensity;
        AnimationIntensity = appearance.AnimationIntensity;
        BackgroundMode = appearance.Background;
        BackgroundImagePath = appearance.BackgroundImagePath;
        BackgroundColor = appearance.BackgroundColor;
        GradientStart = appearance.GradientStart;
        GradientEnd = appearance.GradientEnd;
        BackgroundFit = appearance.BackgroundFit;
        BackgroundOpacity = appearance.BackgroundOpacity;
        BackgroundBlur = appearance.BackgroundBlur;
        BackgroundDarken = appearance.BackgroundDarken;
        BackgroundSaturation = appearance.BackgroundSaturation;
        AdaptColorsToGame = appearance.AdaptColorsToGame;
        _loadingAppearance = false;
    }
}

/// <summary>Um launcher que pode ser aberto junto com o GameHub.</summary>
public partial class LauncherToggle : ObservableObject
{
    public Models.GameHub.LauncherKind Kind { get; }
    public string Name { get; }

    /// <summary>Disparado quando o usuário marca/desmarca, para as Configurações salvarem.</summary>
    public event Action? Changed;

    [ObservableProperty] private bool isEnabled;

    public LauncherToggle(Models.GameHub.LauncherKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    partial void OnIsEnabledChanged(bool value) => Changed?.Invoke();
}
