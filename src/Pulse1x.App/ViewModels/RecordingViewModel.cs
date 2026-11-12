using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Recording;
using Pulse1x.App.Views.Recording;

namespace Pulse1x.App.ViewModels;

/// <summary>Opção de um seletor da Gravação (chave estável + nome traduzido).</summary>
public sealed class RecOption
{
    public RecOption(string key, string name) { Key = key; Name = name; }
    public string Key { get; }
    public string Name { get; }
    public override string ToString() => Name;
}

/// <summary>Um vídeo na lista "Gravações recentes".</summary>
public partial class RecordingItemViewModel : ObservableObject
{
    public RecordingItemViewModel(RecordingFile file)
    {
        File = file;
    }

    public RecordingFile File { get; }
    public string Path => File.Path;
    public string Name => System.IO.Path.GetFileName(File.Path);
    public string DateText => File.Created.ToString("g");
    public string SizeText => RecordingLibrary.FormatSize(File.SizeBytes);

    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private string _durationText = "";
}

/// <summary>
/// Categoria Gravação: grava a tela (monitor inteiro ou área) com FPS, resolução, codec, qualidade
/// e encoder de GPU escolhidos, áudio do sistema e microfone, atalhos globais, menu flutuante e a
/// lista das últimas gravações. Também mostra e alterna o Game DVR e a Xbox Game Bar do Windows.
/// </summary>
public partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly AdvancedOptimizationService _advanced;
    private readonly FfmpegService _ffmpeg;
    private readonly ScreenRecorder _recorder = new();
    private readonly RecordingHotkeys? _hotkeys;
    private readonly DispatcherTimer _clock;
    private RecordingIndicatorWindow? _indicator;
    private RecordingOverlayWindow? _overlay;
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _installCts;
    private List<CaptureMonitor> _monitorList = new();
    private bool _loading;
    private bool _encodersProbed;

    private RecordingSettings S => _settings.Current.Recording;

    /// <summary>O menu flutuante pediu para abrir o Pulse1x nesta categoria.</summary>
    public event Action? OpenAppRequested;

    public RecordingViewModel(SettingsService settings, AdvancedOptimizationService advanced)
    {
        _settings = settings;
        _advanced = advanced;
        _ffmpeg = new FfmpegService(() => S.FfmpegPath);

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => Tick();

        _recorder.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(OnRecorderStateChanged);
        _recorder.StoppedUnexpectedly += (path, error) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            HideIndicator();
            StatusText = Loc.S(error.MessageKey) + (path is null ? "" : "\n" + Loc.F("Rec_SavedAs", System.IO.Path.GetFileName(path)));
            StatusIsError = true;
            _ = RefreshLibraryAsync();
        });

        try
        {
            _hotkeys = new RecordingHotkeys();
            _hotkeys.Pressed += OnHotkey;
        }
        catch { _hotkeys = null; }
        Controls.HotkeyBox.CaptureStarted += () => _hotkeys?.Apply(new RecordingSettings { HotkeysEnabled = false });
        Controls.HotkeyBox.CaptureEnded += ApplyHotkeys;

        Loc.Instance.LanguageChanged += () => { BuildOptions(); OnRecorderStateChanged(); RefreshHotkeyStatus(); };
        _advanced.OptimizationsChanged += () => Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshGameBarAsync());

        BuildOptions();
        LoadFromSettings();
        ApplyHotkeys();
        OnRecorderStateChanged();
        // Dentro do dispatcher: cada await volta para a thread da interface (as listas são ligadas a controles).
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) _ = InitializeAsync();
        else _ = dispatcher.InvokeAsync(InitializeAsync, DispatcherPriority.Background);
    }

    private async Task InitializeAsync()
    {
        await RefreshEngineAsync();
        await RefreshLibraryAsync();
        await RefreshGameBarAsync();
    }

    /// <summary>Chamado quando a página aparece: relê monitores, microfones, gravações e Game Bar.</summary>
    public void OnShown()
    {
        RefreshMonitors();
        RefreshMicrophones();
        _ = RefreshLibraryAsync();
        _ = RefreshGameBarAsync();
        if (!FfmpegFound) _ = RefreshEngineAsync();
    }

    // =====================================================================================
    //  Opções dos seletores
    // =====================================================================================

    public ObservableCollection<RecOption> Monitors { get; } = new();
    public ObservableCollection<RecOption> FpsOptions { get; } = new();
    public ObservableCollection<RecOption> Resolutions { get; } = new();
    public ObservableCollection<RecOption> Codecs { get; } = new();
    public ObservableCollection<RecOption> Encoders { get; } = new();
    public ObservableCollection<RecOption> Qualities { get; } = new();
    public ObservableCollection<RecOption> Containers { get; } = new();
    public ObservableCollection<RecOption> Countdowns { get; } = new();
    public ObservableCollection<RecOption> Microphones { get; } = new();

    private void BuildOptions()
    {
        bool wasLoading = _loading;
        _loading = true;

        Replace(FpsOptions, RecordingSettings.FpsOptions.Select(f => new RecOption(f.ToString(), $"{f} FPS")));
        Replace(Resolutions, new[]
        {
            new RecOption("Native", Loc.S("Rec_ResNative")),
            new RecOption("2160", "2160p (4K)"),
            new RecOption("1440", "1440p (2K)"),
            new RecOption("1080", "1080p (Full HD)"),
            new RecOption("720", "720p (HD)"),
        });
        Replace(Codecs, new[]
        {
            new RecOption("H264", Loc.S("Rec_CodecH264")),
            new RecOption("HEVC", Loc.S("Rec_CodecHevc")),
            new RecOption("AV1", Loc.S("Rec_CodecAv1")),
        });
        BuildEncoderOptions();
        Replace(Qualities, new[]
        {
            new RecOption("Low", Loc.S("Rec_QualityLow")),
            new RecOption("Medium", Loc.S("Rec_QualityMedium")),
            new RecOption("High", Loc.S("Rec_QualityHigh")),
            new RecOption("Ultra", Loc.S("Rec_QualityUltra")),
            new RecOption("Custom", Loc.S("Rec_QualityCustom")),
        });
        Replace(Containers, new[]
        {
            new RecOption("mp4", Loc.S("Rec_ContainerMp4")),
            new RecOption("mkv", Loc.S("Rec_ContainerMkv")),
        });
        Replace(Countdowns, new[]
        {
            new RecOption("0", Loc.S("Rec_CountdownNone")),
            new RecOption("3", Loc.F("Rec_CountdownSeconds", 3)),
            new RecOption("5", Loc.F("Rec_CountdownSeconds", 5)),
            new RecOption("10", Loc.F("Rec_CountdownSeconds", 10)),
        });
        RefreshMonitors();
        RefreshMicrophones();

        _loading = wasLoading;
        if (!wasLoading) LoadFromSettings();
    }

    private readonly Dictionary<string, bool> _vendorWorks = new();

    private void BuildEncoderOptions()
    {
        string Tag(string vendor)
        {
            if (vendor == "Auto") return "";
            if (!_encodersProbed) return "";
            return _vendorWorks.TryGetValue(vendor, out bool ok) && ok ? "  ✓" : "  " + Loc.S("Rec_Unavailable");
        }
        Replace(Encoders, new[]
        {
            new RecOption("Auto", Loc.S("Rec_EncAuto")),
            new RecOption("NVENC", "NVIDIA NVENC" + Tag("NVENC")),
            new RecOption("AMF", "AMD AMF" + Tag("AMF")),
            new RecOption("QSV", "Intel Quick Sync" + Tag("QSV")),
            new RecOption("CPU", Loc.S("Rec_EncCpu") + Tag("CPU")),
        });
    }

    private static void Replace(ObservableCollection<RecOption> target, IEnumerable<RecOption> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    private static RecOption? Find(IEnumerable<RecOption> list, string? key) =>
        list.FirstOrDefault(o => o.Key == key) ?? list.FirstOrDefault();

    public void RefreshMonitors()
    {
        _monitorList = DisplayEnumerator.GetMonitors();
        bool wasLoading = _loading;
        _loading = true;
        Replace(Monitors, _monitorList.Select(m => new RecOption(m.DeviceName,
            Loc.F("Rec_MonitorName", m.Number, m.Width, m.Height) + (m.IsPrimary ? " · " + Loc.S("Rec_Primary") : ""))));
        SelectedMonitor = Find(Monitors, DisplayEnumerator.Resolve(_monitorList, S.MonitorDeviceName)?.DeviceName);
        _loading = wasLoading;
        UpdateRegionText();
    }

    private void RefreshMicrophones()
    {
        bool wasLoading = _loading;
        _loading = true;
        var list = new List<RecOption> { new("", Loc.S("Rec_MicDefault")) };
        list.AddRange(AudioCapture.ListMicrophones().Select(m => new RecOption(m.Id, m.Name)));
        Replace(Microphones, list);
        SelectedMicrophone = Find(Microphones, S.MicrophoneId ?? "");
        _loading = wasLoading;
    }

    // =====================================================================================
    //  Configurações (cada mudança vai direto para o settings.json)
    // =====================================================================================

    [ObservableProperty] private RecOption? _selectedMonitor;
    [ObservableProperty] private bool _isRegion;
    [ObservableProperty] private string _regionText = "";
    [ObservableProperty] private RecOption? _selectedFps;
    [ObservableProperty] private RecOption? _selectedResolution;
    [ObservableProperty] private RecOption? _selectedCodec;
    [ObservableProperty] private RecOption? _selectedEncoder;
    [ObservableProperty] private RecOption? _selectedQuality;
    [ObservableProperty] private int _customBitrate;
    [ObservableProperty] private RecOption? _selectedContainer;
    [ObservableProperty] private bool _drawMouse;
    [ObservableProperty] private bool _recordSystemAudio;
    [ObservableProperty] private bool _recordMicrophone;
    [ObservableProperty] private RecOption? _selectedMicrophone;
    [ObservableProperty] private double _systemVolume;
    [ObservableProperty] private double _microphoneVolume;
    [ObservableProperty] private int _audioOffsetMs;
    [ObservableProperty] private RecOption? _selectedCountdown;
    [ObservableProperty] private bool _showIndicator;
    [ObservableProperty] private bool _soundCues;
    [ObservableProperty] private bool _openFolderWhenDone;
    [ObservableProperty] private bool _hotkeysEnabled;
    [ObservableProperty] private HotkeyBinding? _startHotkey;
    [ObservableProperty] private HotkeyBinding? _stopHotkey;
    [ObservableProperty] private HotkeyBinding? _pauseHotkey;
    [ObservableProperty] private HotkeyBinding? _overlayHotkey;
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _hotkeyWarning = "";

    public bool IsCustomQuality => SelectedQuality?.Key == "Custom";
    public string EstimateText
    {
        get
        {
            // Tamanho aproximado por minuto, para o usuário ter noção do espaço em disco.
            var m = DisplayEnumerator.Resolve(_monitorList, S.MonitorDeviceName);
            if (m is null) return "";
            var g = RecordingCommandBuilder.Geometry(S, m.Width, m.Height);
            double mbps = S.Quality == "Custom" ? S.CustomBitrateMbps : EstimateMbps(g.OutWidth, g.OutHeight, S.Fps, S.Quality, S.Codec);
            double mbPerMin = mbps * 60 / 8;
            return Loc.F("Rec_Estimate", g.OutWidth, g.OutHeight, S.Fps, mbPerMin >= 1024 ? $"{mbPerMin / 1024:0.0} GB" : $"{mbPerMin:0} MB");
        }
    }

    internal static double EstimateMbps(int w, int h, int fps, string quality, string codec)
    {
        // Referência empírica para tela de jogo/área de trabalho em 1080p60 com H.264.
        double baseMbps = quality switch { "Low" => 6, "Medium" => 12, "Ultra" => 40, _ => 22 };
        double scale = (w * (double)h) / (1920 * 1080) * (fps / 60.0);
        double codecFactor = codec switch { "HEVC" => 0.65, "AV1" => 0.55, _ => 1.0 };
        return baseMbps * Math.Pow(scale, 0.8) * codecFactor;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        IsRegion = S.CaptureTarget == "Region";
        SelectedFps = Find(FpsOptions, S.Fps.ToString());
        SelectedResolution = Find(Resolutions, S.Resolution);
        SelectedCodec = Find(Codecs, S.Codec);
        SelectedEncoder = Find(Encoders, S.Encoder);
        SelectedQuality = Find(Qualities, S.Quality);
        CustomBitrate = S.CustomBitrateMbps;
        SelectedContainer = Find(Containers, S.Container);
        DrawMouse = S.DrawMouse;
        RecordSystemAudio = S.RecordSystemAudio;
        RecordMicrophone = S.RecordMicrophone;
        SelectedMicrophone = Find(Microphones, S.MicrophoneId ?? "");
        SystemVolume = Math.Round(S.SystemVolume * 100);
        MicrophoneVolume = Math.Round(S.MicrophoneVolume * 100);
        AudioOffsetMs = S.AudioOffsetMs;
        SelectedCountdown = Find(Countdowns, S.CountdownSeconds.ToString());
        ShowIndicator = S.ShowIndicator;
        SoundCues = S.SoundCues;
        OpenFolderWhenDone = S.OpenFolderWhenDone;
        HotkeysEnabled = S.HotkeysEnabled;
        StartHotkey = S.StartHotkey.Clone();
        StopHotkey = S.StopHotkey.Clone();
        PauseHotkey = S.PauseHotkey.Clone();
        OverlayHotkey = S.OverlayHotkey.Clone();
        OutputFolder = S.EffectiveOutputFolder;
        _loading = false;
        UpdateRegionText();
        OnPropertyChanged(nameof(IsCustomQuality));
        OnPropertyChanged(nameof(EstimateText));
    }

    private void Save(bool estimate = false)
    {
        if (_loading) return;
        _settings.Save();
        if (estimate) OnPropertyChanged(nameof(EstimateText));
    }

    partial void OnSelectedMonitorChanged(RecOption? value)
    {
        if (_loading || value is null) return;
        if (!string.Equals(S.MonitorDeviceName, value.Key, StringComparison.OrdinalIgnoreCase))
        {
            // A área é relativa ao monitor: trocar de monitor volta para a tela inteira.
            S.MonitorDeviceName = value.Key;
            S.RegionWidth = S.RegionHeight = 0;
            if (IsRegion) IsRegion = false;
        }
        UpdateRegionText();
        Save(true);
    }

    partial void OnIsRegionChanged(bool value)
    {
        if (_loading) return;
        S.CaptureTarget = value ? "Region" : "Monitor";
        Save(true);
        if (value && (S.RegionWidth < 16 || S.RegionHeight < 16)) SelectRegion();
        UpdateRegionText();
    }

    private void UpdateRegionText()
    {
        RegionText = S.RegionWidth >= 16 && S.RegionHeight >= 16
            ? Loc.F("Rec_RegionValue", S.RegionWidth, S.RegionHeight, S.RegionX, S.RegionY)
            : Loc.S("Rec_RegionNone");
    }

    partial void OnSelectedFpsChanged(RecOption? value) { if (!_loading && value is not null && int.TryParse(value.Key, out int f)) { S.Fps = f; Save(true); } }
    partial void OnSelectedResolutionChanged(RecOption? value) { if (!_loading && value is not null) { S.Resolution = value.Key; Save(true); } }
    partial void OnSelectedCodecChanged(RecOption? value)
    {
        if (_loading || value is null) return;
        S.Codec = value.Key;
        Save(true);
        _ = ProbeEncodersAsync(force: true);
    }
    partial void OnSelectedEncoderChanged(RecOption? value) { if (!_loading && value is not null) { S.Encoder = value.Key; Save(); } }
    partial void OnSelectedQualityChanged(RecOption? value)
    {
        if (_loading || value is null) return;
        S.Quality = value.Key;
        OnPropertyChanged(nameof(IsCustomQuality));
        Save(true);
    }
    partial void OnCustomBitrateChanged(int value) { if (!_loading) { S.CustomBitrateMbps = Math.Clamp(value, 2, 150); Save(true); } }
    partial void OnSelectedContainerChanged(RecOption? value) { if (!_loading && value is not null) { S.Container = value.Key; Save(); } }
    partial void OnDrawMouseChanged(bool value) { if (!_loading) { S.DrawMouse = value; Save(); } }
    partial void OnRecordSystemAudioChanged(bool value) { if (!_loading) { S.RecordSystemAudio = value; Save(); } }
    partial void OnRecordMicrophoneChanged(bool value) { if (!_loading) { S.RecordMicrophone = value; Save(); } }
    partial void OnSelectedMicrophoneChanged(RecOption? value) { if (!_loading && value is not null) { S.MicrophoneId = value.Key == "" ? null : value.Key; Save(); } }
    partial void OnSystemVolumeChanged(double value) { if (!_loading) { S.SystemVolume = Math.Clamp(value / 100, 0, 2); Save(); } }
    partial void OnMicrophoneVolumeChanged(double value) { if (!_loading) { S.MicrophoneVolume = Math.Clamp(value / 100, 0, 2); Save(); } }
    partial void OnAudioOffsetMsChanged(int value) { if (!_loading) { S.AudioOffsetMs = Math.Clamp(value, -1000, 1000); Save(); } }
    partial void OnSelectedCountdownChanged(RecOption? value) { if (!_loading && value is not null && int.TryParse(value.Key, out int c)) { S.CountdownSeconds = c; Save(); } }
    partial void OnShowIndicatorChanged(bool value) { if (!_loading) { S.ShowIndicator = value; Save(); } }
    partial void OnSoundCuesChanged(bool value) { if (!_loading) { S.SoundCues = value; Save(); } }
    partial void OnOpenFolderWhenDoneChanged(bool value) { if (!_loading) { S.OpenFolderWhenDone = value; Save(); } }
    partial void OnHotkeysEnabledChanged(bool value) { if (!_loading) { S.HotkeysEnabled = value; Save(); ApplyHotkeys(); } }
    partial void OnStartHotkeyChanged(HotkeyBinding? value) { if (!_loading && value is not null) { S.StartHotkey = value; Save(); ApplyHotkeys(); } }
    partial void OnStopHotkeyChanged(HotkeyBinding? value) { if (!_loading && value is not null) { S.StopHotkey = value; Save(); ApplyHotkeys(); } }
    partial void OnPauseHotkeyChanged(HotkeyBinding? value) { if (!_loading && value is not null) { S.PauseHotkey = value; Save(); ApplyHotkeys(); } }
    partial void OnOverlayHotkeyChanged(HotkeyBinding? value) { if (!_loading && value is not null) { S.OverlayHotkey = value; Save(); ApplyHotkeys(); } }

    [RelayCommand]
    private void SelectRegion()
    {
        RefreshMonitors();
        var monitor = DisplayEnumerator.Resolve(_monitorList, S.MonitorDeviceName);
        if (monitor is null) return;
        var picker = new RegionSelectorWindow(monitor);
        picker.ShowDialog();
        if (picker.Result is { } r)
        {
            S.MonitorDeviceName = monitor.DeviceName;
            S.RegionX = r.X; S.RegionY = r.Y; S.RegionWidth = r.Width; S.RegionHeight = r.Height;
            S.CaptureTarget = "Region";
        }
        else if (S.RegionWidth < 16 || S.RegionHeight < 16)
        {
            S.CaptureTarget = "Monitor";
        }
        _loading = true;
        IsRegion = S.CaptureTarget == "Region";
        _loading = false;
        _settings.Save();
        UpdateRegionText();
        OnPropertyChanged(nameof(EstimateText));
    }

    [RelayCommand]
    private void ChangeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.S("Rec_ChooseFolder"),
            InitialDirectory = S.EffectiveOutputFolder,
        };
        if (dialog.ShowDialog() != true) return;
        S.OutputFolder = dialog.FolderName;
        OutputFolder = S.EffectiveOutputFolder;
        _settings.Save();
        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void ResetFolder()
    {
        S.OutputFolder = null;
        OutputFolder = S.EffectiveOutputFolder;
        _settings.Save();
        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(S.EffectiveOutputFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{S.EffectiveOutputFolder}\"") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private void RestoreHotkeys()
    {
        var d = new RecordingSettings();
        _loading = true;
        StartHotkey = d.StartHotkey; StopHotkey = d.StopHotkey; PauseHotkey = d.PauseHotkey; OverlayHotkey = d.OverlayHotkey;
        _loading = false;
        S.StartHotkey = d.StartHotkey; S.StopHotkey = d.StopHotkey; S.PauseHotkey = d.PauseHotkey; S.OverlayHotkey = d.OverlayHotkey;
        _settings.Save();
        ApplyHotkeys();
    }

    // =====================================================================================
    //  Atalhos globais
    // =====================================================================================

    private List<RecordingHotkeyAction> _failedHotkeys = new();

    private void ApplyHotkeys()
    {
        _failedHotkeys = _hotkeys?.Apply(S) ?? new List<RecordingHotkeyAction>();
        RefreshHotkeyStatus();
    }

    private void RefreshHotkeyStatus()
    {
        if (_failedHotkeys.Count == 0) { HotkeyWarning = ""; return; }
        var names = _failedHotkeys.Select(a => Loc.S(a switch
        {
            RecordingHotkeyAction.Start => "Rec_HkStart",
            RecordingHotkeyAction.Stop => "Rec_HkStop",
            RecordingHotkeyAction.PauseResume => "Rec_HkPause",
            _ => "Rec_HkOverlay",
        }));
        HotkeyWarning = Loc.F("Rec_HotkeyConflict", string.Join(", ", names));
    }

    private void OnHotkey(RecordingHotkeyAction action)
    {
        switch (action)
        {
            case RecordingHotkeyAction.Start:
                if (_countdownCts is null && _recorder.State == RecorderState.Idle) _ = StartRecordingAsync();
                else if (_recorder.State == RecorderState.Paused) _ = ResumeAsync();
                break;
            case RecordingHotkeyAction.Stop:
                if (_countdownCts is not null) _countdownCts.Cancel();
                else _ = StopRecordingAsync();
                break;
            case RecordingHotkeyAction.StartStop:
                _ = StartStopAsync();
                break;
            case RecordingHotkeyAction.PauseResume:
                _ = PauseResumeAsync();
                break;
            case RecordingHotkeyAction.Overlay:
                ToggleOverlay();
                break;
        }
    }

    // =====================================================================================
    //  Motor (ffmpeg)
    // =====================================================================================

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInstallButton))] private bool _ffmpegFound;
    [ObservableProperty] private string _engineStatus = "";
    [ObservableProperty] private string _engineStatusColor = "#F97316";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInstallButton))] private bool _isInstalling;
    [ObservableProperty] private double _installProgress;

    public bool ShowInstallButton => !FfmpegFound && !IsInstalling;

    [RelayCommand]
    private void RefreshMonitorsNow() => RefreshMonitors();

    private async Task RefreshEngineAsync()
    {
        FfmpegFound = await Task.Run(_ffmpeg.Locate);
        if (!FfmpegFound)
        {
            EngineStatus = Loc.S("Rec_EngineMissing");
            EngineStatusColor = "#F97316";
            return;
        }
        await _ffmpeg.ProbeAsync();
        EngineStatus = Loc.F("Rec_EngineReady", _ffmpeg.Version ?? "?", _ffmpeg.HasDdagrab ? "Desktop Duplication" : "GDI");
        EngineStatusColor = "#22C55E";
        _ = ProbeEncodersAsync(force: false);
    }

    /// <summary>Testa quais encoders de GPU funcionam para o codec escolhido (atualiza a lista).</summary>
    private async Task ProbeEncodersAsync(bool force)
    {
        if (!FfmpegFound || (_encodersProbed && !force)) return;
        var results = new Dictionary<string, bool>();
        foreach (var vendor in new[] { "NVENC", "AMF", "QSV", "CPU" })
            results[vendor] = await _ffmpeg.EncoderWorksAsync(RecordingCommandBuilder.EncoderName(S.Codec, vendor));
        OnUi(() =>
        {
            _vendorWorks.Clear();
            foreach (var kv in results) _vendorWorks[kv.Key] = kv.Value;
            _encodersProbed = true;
            bool wasLoading = _loading;
            _loading = true;
            BuildEncoderOptions();
            SelectedEncoder = Find(Encoders, S.Encoder);
            _loading = wasLoading;
            UpdateEncoderHint();
        });
    }

    /// <summary>As listas estão ligadas a controles: só podem mudar na thread da interface.</summary>
    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    [ObservableProperty] private string _encoderHint = "";

    private void UpdateEncoderHint()
    {
        if (!_encodersProbed) { EncoderHint = ""; return; }
        var gpu = new[] { "NVENC", "AMF", "QSV" }.Where(v => _vendorWorks.GetValueOrDefault(v)).ToList();
        EncoderHint = gpu.Count > 0
            ? Loc.F("Rec_EncoderHintGpu", string.Join(", ", gpu.Select(v => v switch { "NVENC" => "NVIDIA", "AMF" => "AMD", _ => "Intel" })))
            : Loc.S("Rec_EncoderHintCpu");
    }

    [RelayCommand]
    private async Task InstallFfmpegAsync()
    {
        if (IsInstalling) return;
        IsInstalling = true;
        InstallProgress = 0;
        EngineStatus = Loc.S("Rec_EngineDownloading");
        _installCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                InstallProgress = p * 100;
                EngineStatus = Loc.F("Rec_EngineDownloadingPct", (int)(p * 100));
            });
            await _ffmpeg.InstallAsync(progress, _installCts.Token);
            await RefreshEngineAsync();
        }
        catch (OperationCanceledException)
        {
            EngineStatus = Loc.S("Rec_EngineMissing");
        }
        catch (Exception ex)
        {
            EngineStatus = Loc.F("Rec_EngineDownloadFailed", ex.Message);
            EngineStatusColor = "#EF4444";
        }
        finally
        {
            IsInstalling = false;
            _installCts = null;
        }
    }

    [RelayCommand]
    private void CancelInstall() => _installCts?.Cancel();

    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.S("Rec_BrowseFfmpeg"),
            Filter = "ffmpeg.exe|ffmpeg.exe",
        };
        if (dialog.ShowDialog() != true) return;
        S.FfmpegPath = dialog.FileName;
        _settings.Save();
        _encodersProbed = false;
        await RefreshEngineAsync();
    }

    // =====================================================================================
    //  Gravar / pausar / terminar
    // =====================================================================================

    [ObservableProperty] private bool _isIdle = true;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _stateColor = "#9CA3AF";
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _mainButtonText = "";
    [ObservableProperty] private string _pauseButtonText = "";
    [ObservableProperty] private string _activeInfo = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _micMuted;
    [ObservableProperty] private bool _hasMicrophoneTrack;

    partial void OnMicMutedChanged(bool value) => _recorder.MicMuted = value;

    private void OnRecorderStateChanged()
    {
        var state = _recorder.State;
        bool counting = _countdownCts is not null;
        IsRecording = state == RecorderState.Recording;
        IsPaused = state == RecorderState.Paused;
        IsBusy = state is RecorderState.Starting or RecorderState.Stopping || counting;
        IsIdle = state == RecorderState.Idle && !counting;
        IsActive = state is RecorderState.Recording or RecorderState.Paused;
        HasMicrophoneTrack = _recorder.HasMicrophone;

        (StateText, StateColor) = state switch
        {
            RecorderState.Starting => (Loc.S("Rec_StateStarting"), "#EAB308"),
            RecorderState.Recording => (Loc.S("Rec_StateRecording"), "#EF4444"),
            RecorderState.Paused => (Loc.S("Rec_StatePaused"), "#EAB308"),
            RecorderState.Stopping => (Loc.S("Rec_StateSaving"), "#EAB308"),
            _ when counting => (Loc.S("Rec_StateCountdown"), "#EAB308"),
            _ => (Loc.S("Rec_StateIdle"), "#9CA3AF"),
        };
        MainButtonText = Loc.S(IsActive ? "Rec_Stop" : "Rec_Start");
        PauseButtonText = Loc.S(IsPaused ? "Rec_Resume" : "Rec_Pause");

        if (IsActive && _recorder.ActiveEncoder is { } enc)
        {
            var (w, h) = _recorder.OutputSize;
            ActiveInfo = Loc.F("Rec_ActiveInfo", w, h, S.Fps, EncoderLabel(enc))
                + (_recorder.UsedFallback ? " · " + Loc.S("Rec_FallbackUsed") : "");
        }
        else if (!IsActive) ActiveInfo = "";

        if (IsActive) _clock.Start(); else _clock.Stop();
        Tick();
        StartStopCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
    }

    private static string EncoderLabel(string encoder)
    {
        string vendor = RecordingCommandBuilder.VendorOf(encoder) switch
        {
            "NVENC" => "NVIDIA NVENC",
            "AMF" => "AMD AMF",
            "QSV" => "Intel Quick Sync",
            _ => "CPU",
        };
        string codec = encoder.StartsWith("hevc", StringComparison.Ordinal) || encoder == "libx265" ? "HEVC"
            : encoder.StartsWith("av1", StringComparison.Ordinal) || encoder == "libsvtav1" ? "AV1" : "H.264";
        return $"{codec} · {vendor}";
    }

    private void Tick()
    {
        var elapsed = _recorder.Elapsed;
        ElapsedText = RecordingIndicatorWindow.FormatElapsed(elapsed);
        _indicator?.Update(elapsed, IsPaused);
    }

    private bool CanStartStop() => !IsBusy || _countdownCts is not null;

    // Concorrente: durante a contagem regressiva, clicar de novo cancela (o comando ainda está rodando).
    [RelayCommand(CanExecute = nameof(CanStartStop), AllowConcurrentExecutions = true)]
    private async Task StartStopAsync()
    {
        if (_countdownCts is not null) { _countdownCts.Cancel(); return; }
        if (IsActive) await StopRecordingAsync();
        else await StartRecordingAsync();
    }

    private bool CanPauseResume() => IsActive;

    [RelayCommand(CanExecute = nameof(CanPauseResume))]
    private async Task PauseResumeAsync()
    {
        if (IsRecording)
        {
            try { await _recorder.PauseAsync(); Cue(660, 520); }
            catch (Exception ex) { ShowError(ex); }
        }
        else if (IsPaused) await ResumeAsync();
    }

    private async Task ResumeAsync()
    {
        try { Cue(520, 660); await _recorder.ResumeAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }

    public async Task StartRecordingAsync()
    {
        if (_recorder.State != RecorderState.Idle || _countdownCts is not null) return;
        _overlay?.Close(); // o menu sai da frente antes da contagem/gravação
        StatusText = "";
        StatusIsError = false;

        if (!FfmpegFound) await RefreshEngineAsync();
        if (!FfmpegFound || _ffmpeg.ExePath is null)
        {
            ShowStatus(Loc.S("Rec_ErrNoEngine"), error: true);
            OpenAppRequested?.Invoke();
            return;
        }

        RefreshMonitors();
        var monitor = DisplayEnumerator.Resolve(_monitorList, S.MonitorDeviceName);
        if (monitor is null) { ShowStatus(Loc.S("Rec_ErrNoMonitor"), error: true); return; }

        // Contagem regressiva (cancelável pelo atalho de terminar ou pelo próprio botão).
        if (S.CountdownSeconds > 0)
        {
            _countdownCts = new CancellationTokenSource();
            OnRecorderStateChanged();
            try
            {
                var countdown = new CountdownWindow(monitor);
                await countdown.RunAsync(S.CountdownSeconds, _countdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                _countdownCts = null;
                OnRecorderStateChanged();
                return;
            }
            _countdownCts = null;
        }

        try
        {
            IsBusy = true;
            await ProbeEncodersAsync(force: false);
            var plan = new RecordingPlan(_ffmpeg.ExePath, S, monitor, _ffmpeg.HasDdagrab, await ResolveEncodersAsync(monitor));
            Cue(880, 0);
            await _recorder.StartAsync(plan);
            if (S.ShowIndicator) OpenIndicator(monitor);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            OnRecorderStateChanged();
        }
    }

    /// <summary>Encoders na ordem a tentar, só os que abriram no teste desta máquina.</summary>
    private async Task<List<string>> ResolveEncodersAsync(CaptureMonitor monitor)
    {
        var order = S.Encoder == "Auto"
            ? RecordingCommandBuilder.AutoVendorOrder(monitor.VendorId).ToList()
            : new[] { S.Encoder }.Concat(RecordingCommandBuilder.AutoVendorOrder(monitor.VendorId)).Distinct().ToList();
        var list = new List<string>();
        foreach (var vendor in order)
        {
            string enc = RecordingCommandBuilder.EncoderName(S.Codec, vendor);
            if (await _ffmpeg.EncoderWorksAsync(enc)) list.Add(enc);
        }
        return list;
    }

    public async Task StopRecordingAsync()
    {
        if (!IsActive) return;
        try
        {
            HideIndicator();
            string path = await _recorder.StopAsync();
            Cue(660, 440);
            ShowStatus(Loc.F("Rec_SavedAs", System.IO.Path.GetFileName(path)), error: false);
            await RefreshLibraryAsync();
            if (S.OpenFolderWhenDone)
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            ShowError(ex);
            await RefreshLibraryAsync();
        }
    }

    private void ShowError(Exception ex)
    {
        string text = ex is RecordingException re
            ? Loc.S(re.MessageKey) + (string.IsNullOrWhiteSpace(re.Detail) ? "" : "\n" + re.Detail)
            : ex.Message;
        ShowStatus(text, error: true);
    }

    private void ShowStatus(string text, bool error)
    {
        StatusText = text;
        StatusIsError = error;
    }

    private void OpenIndicator(CaptureMonitor monitor)
    {
        HideIndicator();
        _indicator = new RecordingIndicatorWindow(monitor);
        _indicator.Show();
        Tick();
    }

    private void HideIndicator()
    {
        _indicator?.Close();
        _indicator = null;
    }

    /// <summary>Bipe curto (dois tons) no dispositivo padrão. Silencioso se o usuário desligou.</summary>
    private void Cue(int firstHz, int secondHz)
    {
        if (!S.SoundCues) return;
        _ = Task.Run(() =>
        {
            try
            {
                using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                ISampleProvider Tone(int hz) => new SignalGenerator(48000, 2) { Frequency = hz, Gain = 0.12, Type = SignalGeneratorType.Sin }
                    .Take(TimeSpan.FromMilliseconds(90));
                ISampleProvider tone = secondHz > 0
                    ? new ConcatenatingSampleProvider(new[] { Tone(firstHz), Tone(secondHz) })
                    : Tone(firstHz);
                using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
                output.Init(tone);
                output.Play();
                var until = DateTime.UtcNow.AddSeconds(1);
                while (output.PlaybackState == PlaybackState.Playing && DateTime.UtcNow < until) Thread.Sleep(20);
            }
            catch { }
        });
    }

    // =====================================================================================
    //  Menu flutuante
    // =====================================================================================

    [RelayCommand]
    public void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Close();
            return;
        }
        RefreshMonitors();
        var monitor = RecordingNative.MonitorUnderCursor(_monitorList)
                      ?? DisplayEnumerator.Resolve(_monitorList, S.MonitorDeviceName);
        if (monitor is null) return;
        _ = RefreshLibraryAsync();
        _overlay = new RecordingOverlayWindow(this, monitor);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        _overlay.Activate();
    }

    [RelayCommand]
    private void OpenApp()
    {
        _overlay?.Close();
        OpenAppRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenGameBar()
    {
        _overlay?.Close();
        try { Process.Start(new ProcessStartInfo("ms-gamebar:") { UseShellExecute = true }); } catch { }
    }

    // =====================================================================================
    //  Gravações recentes
    // =====================================================================================

    public ObservableCollection<RecordingItemViewModel> Recordings { get; } = new();
    public IEnumerable<RecordingItemViewModel> RecentThree => Recordings.Take(3);
    [ObservableProperty] private string _libraryInfo = "";
    [ObservableProperty] private bool _hasRecordings;

    private int _libraryVersion;

    [RelayCommand]
    public async Task RefreshLibraryAsync()
    {
        int version = ++_libraryVersion;
        string folder = S.EffectiveOutputFolder;
        var files = await Task.Run(() => RecordingLibrary.List(folder));
        if (version != _libraryVersion) return;

        // Mantém os itens já carregados (miniatura pronta) e só troca o que mudou.
        var existing = Recordings.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        Recordings.Clear();
        foreach (var f in files)
            Recordings.Add(existing.TryGetValue(f.Path, out var item) && item.File == f ? item : new RecordingItemViewModel(f));
        HasRecordings = Recordings.Count > 0;
        long total = files.Sum(f => f.SizeBytes);
        LibraryInfo = Recordings.Count == 0 ? "" : Loc.F("Rec_LibraryInfo", Recordings.Count, RecordingLibrary.FormatSize(total));
        OnPropertyChanged(nameof(RecentThree));

        if (_ffmpeg.ExePath is not { } exe) return;
        _ = Task.Run(() => RecordingLibrary.PruneCache(files));
        foreach (var item in Recordings.ToList())
        {
            if (version != _libraryVersion) return;
            if (item.Thumbnail is not null) continue;
            var (thumb, duration) = await RecordingLibrary.DescribeAsync(exe, item.File);
            if (duration is { } d) item.DurationText = RecordingIndicatorWindow.FormatElapsed(d);
            if (thumb is not null) item.Thumbnail = LoadImage(thumb);
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // não prende o arquivo
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    [RelayCommand]
    private void Play(RecordingItemViewModel? item)
    {
        if (item is null) return;
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch { ShowStatus(Loc.S("Rec_ErrOpen"), error: true); }
    }

    [RelayCommand]
    private void ShowInFolder(RecordingItemViewModel? item)
    {
        if (item is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private async Task DeleteAsync(RecordingItemViewModel? item)
    {
        if (item is null) return;
        var answer = MessageBox.Show(Application.Current.MainWindow!, Loc.F("Rec_DeleteConfirm", item.Name),
            Loc.S("Rec_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        if (!RecordingLibrary.Delete(item.Path)) ShowStatus(Loc.S("Rec_ErrDelete"), error: true);
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private async Task DeleteAllAsync()
    {
        if (Recordings.Count == 0) return;
        var answer = MessageBox.Show(Application.Current.MainWindow!, Loc.F("Rec_DeleteAllConfirm", Recordings.Count),
            Loc.S("Rec_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        int failed = 0;
        foreach (var item in Recordings.ToList())
            if (!RecordingLibrary.Delete(item.Path)) failed++;
        if (failed > 0) ShowStatus(Loc.S("Rec_ErrDelete"), error: true);
        await RefreshLibraryAsync();
    }

    // =====================================================================================
    //  Game Bar e Game DVR do Windows (as mesmas otimizações reversíveis da categoria Otimização)
    // =====================================================================================

    [ObservableProperty] private bool _gameDvrDisabled;
    [ObservableProperty] private bool _gameBarDisabled;
    [ObservableProperty] private bool _isApplyingGameBar;

    public string GameDvrButtonText => Loc.S(GameDvrDisabled ? "Rec_Revert" : "Rec_GameDvrDisable");
    public string GameBarButtonText => Loc.S(GameBarDisabled ? "Rec_Revert" : "Rec_GameBarDisable");
    public string GameDvrStateText => Loc.S(GameDvrDisabled ? "Rec_GameDvrOff" : "Rec_GameDvrOn");
    public string GameBarStateText => Loc.S(GameBarDisabled ? "Rec_GameBarOff" : "Rec_GameBarOn");

    partial void OnGameDvrDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(GameDvrButtonText));
        OnPropertyChanged(nameof(GameDvrStateText));
    }

    partial void OnGameBarDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(GameBarButtonText));
        OnPropertyChanged(nameof(GameBarStateText));
    }

    private AdvancedOptimization? Opt(string id) => _advanced.Optimizations.FirstOrDefault(o => o.Id == id);

    private async Task RefreshGameBarAsync()
    {
        try
        {
            if (Opt("game-dvr") is { } dvr) GameDvrDisabled = await dvr.IsAppliedAsync();
            if (Opt("xbox-gamebar") is { } bar) GameBarDisabled = await bar.IsAppliedAsync();
        }
        catch { }
        OnPropertyChanged(nameof(GameDvrButtonText));
        OnPropertyChanged(nameof(GameBarButtonText));
        OnPropertyChanged(nameof(GameDvrStateText));
        OnPropertyChanged(nameof(GameBarStateText));
    }

    [RelayCommand]
    private Task ToggleGameDvrAsync() => ToggleOptimizationAsync("game-dvr", GameDvrDisabled);

    [RelayCommand]
    private Task ToggleGameBarAsync() => ToggleOptimizationAsync("xbox-gamebar", GameBarDisabled);

    private async Task ToggleOptimizationAsync(string id, bool currentlyApplied)
    {
        if (IsApplyingGameBar || Opt(id) is not { } opt) return;
        IsApplyingGameBar = true;
        try
        {
            if (currentlyApplied) await _advanced.RevertOptimizationAsync(id);
            else await opt.ApplyAsync();
        }
        catch (Exception ex) { ShowStatus(ex.Message, error: true); }
        finally
        {
            IsApplyingGameBar = false;
            await RefreshGameBarAsync();
        }
    }

    [RelayCommand]
    private void OpenGameBarSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:gaming-gamebar") { UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void OpenCaptureSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:gaming-gamedvr") { UseShellExecute = true }); } catch { }
    }

    public void Dispose()
    {
        _clock.Stop();
        _countdownCts?.Cancel();
        _installCts?.Cancel();
        HideIndicator();
        _overlay?.Close();
        _hotkeys?.Dispose();

        // Fechar o Pulse1x no meio de uma gravação: termina e monta o vídeo antes de sair (juntar
        // os trechos é cópia, leva segundos). Fora da thread de UI para não travar no próprio await.
        if (_recorder.State is RecorderState.Recording or RecorderState.Paused)
        {
            try { Task.Run(() => _recorder.StopAsync()).Wait(TimeSpan.FromSeconds(60)); }
            catch { /* os trechos continuam na pasta oculta ao lado dos vídeos */ }
        }
        _recorder.Dispose();
    }
}
