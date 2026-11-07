using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Sound;

namespace Pulse1x.App.ViewModels;

/// <summary>Uma banda do equalizador gráfico.</summary>
public partial class EqBandViewModel : ObservableObject
{
    private readonly Action _changed;
    public EqBandViewModel(int frequency, double value, Action changed)
    {
        Frequency = frequency;
        _value = value;
        _changed = changed;
    }

    public int Frequency { get; }
    public string Label => Frequency >= 1000 ? $"{Frequency / 1000}k" : Frequency.ToString();

    [ObservableProperty] private double _value;
    public string ValueText => $"{(Value > 0 ? "+" : "")}{Value:0.#}";

    internal bool Silent;
    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(ValueText));
        if (!Silent) _changed();
    }

    internal void SetSilently(double value)
    {
        Silent = true;
        Value = value;
        Silent = false;
    }
}

/// <summary>Opção de um seletor (perfil de som ou efeito de voz).</summary>
public sealed class SoundOption
{
    public SoundOption(string key, string name, string description, string icon)
    {
        Key = key; Name = name; Description = description; Icon = icon;
    }
    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
}

/// <summary>
/// Categoria Som: dispositivos, volumes, microfone acima de 100%, amplificação de hardware,
/// perfis de som, equalizador, graves, som 3D, modificador de voz, testes ao vivo e inicialização
/// com o Windows. Toda mudança é aplicada na hora (o motor de áudio recarrega sozinho).
/// </summary>
public partial class SoundViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly SoundEngine _engine;
    private readonly AudioTestService _tester = new();
    private readonly DispatcherTimer _applyDebounce;
    private readonly DispatcherTimer _meterTimer;
    private readonly Timer _lockTimer;
    private SoundDeviceService.LevelMeter? _outMeter;
    private SoundDeviceService.LevelMeter? _inMeter;
    private CancellationTokenSource? _testCts;
    private bool _loading;

    private SoundSettings S => _settings.Current.Sound;
    private SoundDeviceService Devices => _engine.Devices;

    public SoundViewModel(SettingsService settings, SoundEngine engine)
    {
        _settings = settings;
        _engine = engine;

        _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _applyDebounce.Tick += (_, _) => { _applyDebounce.Stop(); _ = ApplyEffectsNowAsync(); };

        _meterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _meterTimer.Tick += (_, _) => UpdateMeters();

        // Trava do volume do microfone: vale enquanto o Pulse1x estiver aberto (mesmo na bandeja).
        _lockTimer = new Timer(_ => { try { _engine.EnforceMicLevel(S); } catch { } }, null, 3000, 2000);

        Bands = new ObservableCollection<EqBandViewModel>(
            SoundPresets.EqFrequencies.Select((f, i) => new EqBandViewModel(f, S.Eq[i], OnBandChanged)));

        Loc.Instance.LanguageChanged += BuildOptions;
        BuildOptions();
        Refresh();
    }

    // =====================================================================================
    //  Estado do motor de áudio
    // =====================================================================================

    [ObservableProperty] private bool _apoInstalled;
    [ObservableProperty] private bool _apoReady;
    [ObservableProperty] private string _apoStatus = "";
    [ObservableProperty] private string _apoStatusColor = "#F97316";
    [ObservableProperty] private string _statusText = "";

    private void RefreshApo()
    {
        ApoInstalled = _engine.Apo.IsInstalled;
        if (!ApoInstalled)
        {
            ApoReady = false;
            ApoStatus = Loc.S("Snd_ApoMissing");
            ApoStatusColor = "#F97316";
            return;
        }

        bool? outOk = _engine.Apo.IsActiveOn(SelectedOutput?.Id);
        bool? inOk = _engine.Apo.IsActiveOn(SelectedInput?.Id);
        var missing = new List<string>();
        if (outOk == false && SelectedOutput is not null) missing.Add(SelectedOutput.Name);
        if (inOk == false && SelectedInput is not null) missing.Add(SelectedInput.Name);

        ApoReady = true;
        if (missing.Count == 0)
        {
            ApoStatus = Loc.S("Snd_ApoActive");
            ApoStatusColor = "#22C55E";
        }
        else
        {
            ApoStatus = Loc.F("Snd_ApoNotOnDevice", string.Join(" · ", missing));
            ApoStatusColor = "#F97316";
        }
    }

    [RelayCommand]
    private void DownloadApo() => EqualizerApoService.OpenDownloadPage();

    [RelayCommand]
    private void OpenDeviceSelector()
    {
        if (!_engine.Apo.OpenDeviceSelector()) StatusText = Loc.S("Snd_ApoSelectorFailed");
    }

    [RelayCommand]
    private void Refresh()
    {
        _loading = true;
        try
        {
            Outputs = Devices.List(input: false);
            Inputs = Devices.List(input: true);
            SelectedOutput = Outputs.FirstOrDefault(d => d.Id == S.OutputDeviceId) ?? Outputs.FirstOrDefault(d => d.IsDefault) ?? Outputs.FirstOrDefault();
            SelectedInput = Inputs.FirstOrDefault(d => d.Id == S.InputDeviceId) ?? Inputs.FirstOrDefault(d => d.IsDefault) ?? Inputs.FirstOrDefault();
            LoadFromSettings();
            ReadDeviceState();
        }
        finally { _loading = false; }
        RefreshApo();
        ReopenMeters();
    }

    // =====================================================================================
    //  Dispositivos e volumes
    // =====================================================================================

    [ObservableProperty] private IReadOnlyList<SoundDevice> _outputs = Array.Empty<SoundDevice>();
    [ObservableProperty] private IReadOnlyList<SoundDevice> _inputs = Array.Empty<SoundDevice>();
    [ObservableProperty] private SoundDevice? _selectedOutput;
    [ObservableProperty] private SoundDevice? _selectedInput;
    [ObservableProperty] private double _outputVolume;
    [ObservableProperty] private bool _outputMuted;
    [ObservableProperty] private bool _inputMuted;
    [ObservableProperty] private double _outputPeak;
    [ObservableProperty] private double _inputPeak;

    partial void OnSelectedOutputChanged(SoundDevice? value)
    {
        if (_loading || value is null) return;
        S.OutputDeviceId = value.Id;
        Save();
        ReadDeviceState();
        RefreshApo();
        ReopenMeters();
        ScheduleApply();
    }

    partial void OnSelectedInputChanged(SoundDevice? value)
    {
        if (_loading || value is null) return;
        S.InputDeviceId = value.Id;
        Save();
        StopTests();
        ReadDeviceState();
        RefreshApo();
        ReopenMeters();
        _engine.ApplyMicLevel(S);
        ScheduleApply();
    }

    private void ReadDeviceState()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            OutputVolume = Devices.GetVolume(SelectedOutput?.Id) ?? 0;
            OutputMuted = Devices.GetMute(SelectedOutput?.Id) ?? false;
            InputMuted = Devices.GetMute(SelectedInput?.Id) ?? false;

            // Sem um valor salvo, o slider mostra o volume atual do Windows (até 100%).
            MicLevel = S.MicLevelPercent ?? Devices.GetVolume(SelectedInput?.Id) ?? 100;

            var boost = Devices.GetHardwareBoost(SelectedInput?.Id);
            HardwareBoostSteps = boost?.Steps.Select(v => new BoostStep(v)).ToList() ?? new List<BoostStep>();
            HasHardwareBoost = HardwareBoostSteps.Count > 1;
            SelectedBoost = boost is null ? null
                : HardwareBoostSteps.OrderBy(s => Math.Abs(s.Db - boost.CurrentDb)).FirstOrDefault();
        }
        finally { _loading = was; }
        OnPropertyChanged(nameof(MicLevelText));
        OnPropertyChanged(nameof(MicLevelDetail));
        OnPropertyChanged(nameof(MicNeedsEngine));
    }

    partial void OnOutputVolumeChanged(double value)
    {
        if (_loading) return;
        Devices.SetVolume(SelectedOutput?.Id, (int)Math.Round(value));
    }

    partial void OnOutputMutedChanged(bool value)
    {
        if (_loading) return;
        Devices.SetMute(SelectedOutput?.Id, value);
    }

    partial void OnInputMutedChanged(bool value)
    {
        if (_loading) return;
        Devices.SetMute(SelectedInput?.Id, value);
    }

    /// <summary>Texto do botão: "Tornar padrão" ou, se o dispositivo já é o padrão, "Remover padrão".</summary>
    public string OutputDefaultText => Loc.S(SelectedOutput?.IsDefault == true ? "Snd_RemoveDefault" : "Snd_MakeDefault");
    public string InputDefaultText => Loc.S(SelectedInput?.IsDefault == true ? "Snd_RemoveDefault" : "Snd_MakeDefault");
    public string OutputDefaultTip => Loc.S(SelectedOutput?.IsDefault == true ? "Snd_RemoveDefaultTip" : "Snd_MakeDefaultTip");
    public string InputDefaultTip => Loc.S(SelectedInput?.IsDefault == true ? "Snd_RemoveDefaultTip" : "Snd_MakeDefaultTip");

    [RelayCommand]
    private void ToggleOutputDefault() => ToggleDefault(SelectedOutput, input: false);

    [RelayCommand]
    private void ToggleInputDefault() => ToggleDefault(SelectedInput, input: true);

    private void ToggleDefault(SoundDevice? device, bool input)
    {
        if (device is null) return;

        // A página continua mostrando o mesmo aparelho depois da troca.
        if (input) S.InputDeviceId = device.Id; else S.OutputDeviceId = device.Id;

        if (device.IsDefault)
        {
            string? preferred = input ? S.PreviousDefaultInputId : S.PreviousDefaultOutputId;
            string? next = Devices.RemoveDefault(device.Id, input, preferred);
            var nextName = (input ? Inputs : Outputs).FirstOrDefault(d => d.Id == next)?.Name;
            StatusText = nextName is not null
                ? Loc.F("Snd_DefaultRemoved", device.Name, nextName)
                : Loc.S("Snd_DefaultRemoveFailed");
        }
        else
        {
            string? previous = Devices.GetDefaultId(input);
            if (Devices.SetDefault(device.Id))
            {
                if (input) S.PreviousDefaultInputId = previous; else S.PreviousDefaultOutputId = previous;
                StatusText = Loc.F("Snd_DefaultSet", device.Name);
            }
            else StatusText = Loc.S("Snd_DefaultFailed");
        }
        Save();
        Refresh();
    }

    partial void OnSelectedOutputChanged(SoundDevice? oldValue, SoundDevice? newValue)
    {
        OnPropertyChanged(nameof(OutputDefaultText));
        OnPropertyChanged(nameof(OutputDefaultTip));
    }

    partial void OnSelectedInputChanged(SoundDevice? oldValue, SoundDevice? newValue)
    {
        OnPropertyChanged(nameof(InputDefaultText));
        OnPropertyChanged(nameof(InputDefaultTip));
    }

    // =====================================================================================
    //  Voltar ao padrão
    // =====================================================================================

    /// <summary>
    /// Desfaz tudo o que a categoria Som mudou: microfone de volta a 100% sem ganho extra,
    /// amplificação de hardware no mínimo, equalizador/graves/3D/voz desligados, sem tarefa de
    /// inicialização nem trava. Os dispositivos escolhidos na página continuam os mesmos.
    /// </summary>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        var answer = System.Windows.MessageBox.Show(Loc.S("Snd_RestoreConfirm"), Loc.S("Snd_RestoreTitle"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        StopTests();
        string? mic = SelectedInput?.Id;
        if (S.ApplyOnStartup)
        {
            try { SoundEngine.SetStartupTask(false); } catch { }
        }

        var fresh = new SoundSettings
        {
            OutputDeviceId = S.OutputDeviceId,
            InputDeviceId = S.InputDeviceId,
            PreviousDefaultOutputId = S.PreviousDefaultOutputId,
            PreviousDefaultInputId = S.PreviousDefaultInputId,
        };
        _settings.Current.Sound = fresh;

        if (mic is not null)
        {
            Devices.SetVolume(mic, 100);
            var boost = Devices.GetHardwareBoost(mic);
            if (boost is not null) Devices.SetHardwareBoost(mic, boost.MinDb);
        }

        Save();
        Refresh();
        await ApplyEffectsNowAsync();
        StatusText = Loc.S("Snd_Restored");
    }

    // =====================================================================================
    //  Microfone: volume até 400% e amplificação de hardware
    // =====================================================================================

    public sealed record BoostStep(float Db)
    {
        public string Display => Db <= 0.05f ? "0 dB" : $"+{Db:0.#} dB";
    }

    [ObservableProperty] private double _micLevel = 100;
    [ObservableProperty] private bool _hasHardwareBoost;
    [ObservableProperty] private IReadOnlyList<BoostStep> _hardwareBoostSteps = Array.Empty<BoostStep>();
    [ObservableProperty] private BoostStep? _selectedBoost;

    public string MicLevelText => $"{Math.Round(MicLevel)}%";

    public string MicLevelDetail
    {
        get
        {
            var (windows, extra) = SoundPresets.SplitMicLevel((int)Math.Round(MicLevel));
            string text = extra > 0
                ? Loc.F("Snd_MicLevelSplit", windows, extra.ToString("0.0"))
                : Loc.F("Snd_MicLevelWindows", windows);
            return MicLevel > 400.5 ? text + Loc.S("Snd_MicLoudWarn") : text;
        }
    }

    /// <summary>Acima de 100% sem o motor de áudio pronto: o excedente não tem efeito.</summary>
    public bool MicNeedsEngine => MicLevel > 100.5 && !ApoInstalled;

    partial void OnMicLevelChanged(double value)
    {
        OnPropertyChanged(nameof(MicLevelText));
        OnPropertyChanged(nameof(MicLevelDetail));
        OnPropertyChanged(nameof(MicNeedsEngine));
        if (_loading) return;
        S.MicLevelPercent = (int)Math.Round(value);
        _engine.ApplyMicLevel(S);
        ScheduleApply();
    }

    partial void OnApoInstalledChanged(bool value) => OnPropertyChanged(nameof(MicNeedsEngine));

    partial void OnSelectedBoostChanged(BoostStep? value)
    {
        if (_loading || value is null) return;
        S.MicHardwareBoostDb = value.Db;
        Save();
        Devices.SetHardwareBoost(SelectedInput?.Id, value.Db);
    }

    [ObservableProperty] private bool _micLowCut;
    [ObservableProperty] private bool _micClarity;
    [ObservableProperty] private bool _micNoiseGate;

    partial void OnMicNoiseGateChanged(bool value) { if (!_loading) { S.MicNoiseGate = value; ScheduleApply(); } }

    partial void OnMicLowCutChanged(bool value) { if (!_loading) { S.MicLowCut = value; ScheduleApply(); } }
    partial void OnMicClarityChanged(bool value) { if (!_loading) { S.MicClarity = value; ScheduleApply(); } }

    // =====================================================================================
    //  Perfis, equalizador, graves e som 3D
    // =====================================================================================

    public ObservableCollection<EqBandViewModel> Bands { get; }
    [ObservableProperty] private IReadOnlyList<SoundOption> _profiles = Array.Empty<SoundOption>();
    [ObservableProperty] private SoundOption? _selectedProfile;
    [ObservableProperty] private bool _outputEffectsEnabled;
    [ObservableProperty] private double _bassBoost;
    [ObservableProperty] private double _surround;
    [ObservableProperty] private double _outputGain;

    public string BassBoostText => BassBoost < 0.05 ? Loc.S("Snd_Off") : $"+{BassBoost:0.#} dB";
    public string SurroundText => Surround < 0.01 ? Loc.S("Snd_Off") : $"{Math.Round(Surround * 100)}%";
    public string OutputGainText => $"{(OutputGain > 0 ? "+" : "")}{OutputGain:0.#} dB";

    partial void OnOutputEffectsEnabledChanged(bool value)
    {
        if (_loading) return;
        S.OutputEffectsEnabled = value;
        ScheduleApply();
    }

    partial void OnSelectedProfileChanged(SoundOption? value)
    {
        if (_loading || value is null || value.Key == "Custom") return;
        var profile = SoundPresets.FindProfile(value.Key);
        if (profile is null) return;

        _loading = true;
        try
        {
            for (int i = 0; i < Bands.Count; i++) Bands[i].SetSilently(profile.Eq[i]);
            BassBoost = profile.BassBoostDb;
            Surround = profile.Surround;
            OutputEffectsEnabled = true;
        }
        finally { _loading = false; }

        S.OutputProfile = value.Key;
        S.OutputEffectsEnabled = true;
        CopyOutputToSettings();
        ScheduleApply();
    }

    private void OnBandChanged()
    {
        if (_loading) return;
        MarkCustom();
        CopyOutputToSettings();
        ScheduleApply();
    }

    partial void OnBassBoostChanged(double value)
    {
        OnPropertyChanged(nameof(BassBoostText));
        if (_loading) return;
        MarkCustom();
        CopyOutputToSettings();
        ScheduleApply();
    }

    partial void OnSurroundChanged(double value)
    {
        OnPropertyChanged(nameof(SurroundText));
        if (_loading) return;
        MarkCustom();
        CopyOutputToSettings();
        ScheduleApply();
    }

    partial void OnOutputGainChanged(double value)
    {
        OnPropertyChanged(nameof(OutputGainText));
        if (_loading) return;
        S.OutputGainDb = value;
        // Mexer no volume extra liga o processamento da saída (senão o slider não faria nada).
        if (!OutputEffectsEnabled)
        {
            _loading = true;
            OutputEffectsEnabled = true;
            _loading = false;
            S.OutputEffectsEnabled = true;
        }
        ScheduleApply();
    }

    private void MarkCustom()
    {
        if (S.OutputProfile == "Custom") return;
        S.OutputProfile = "Custom";
        bool was = _loading;
        _loading = true;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Key == "Custom");
        _loading = was;
        if (!OutputEffectsEnabled)
        {
            _loading = true;
            OutputEffectsEnabled = true;
            _loading = was;
            S.OutputEffectsEnabled = true;
        }
    }

    private void CopyOutputToSettings()
    {
        S.Eq = Bands.Select(b => Math.Round(b.Value, 1)).ToArray();
        S.BassBoostDb = Math.Round(BassBoost, 1);
        S.Surround = Math.Round(Surround, 2);
    }

    [RelayCommand]
    private void ResetEqualizer()
    {
        SelectedProfile = Profiles.FirstOrDefault(p => p.Key == "Flat");
        OutputGain = 0;
    }

    // =====================================================================================
    //  Modificador de voz
    // =====================================================================================

    [ObservableProperty] private IReadOnlyList<SoundOption> _voices = Array.Empty<SoundOption>();
    [ObservableProperty] private SoundOption? _selectedVoice;

    partial void OnSelectedVoiceChanged(SoundOption? value)
    {
        if (_loading || value is null) return;
        S.VoiceEffect = value.Key;
        ScheduleApply();
        StatusText = value.Key == "None" ? Loc.S("Snd_VoiceOff") : Loc.F("Snd_VoiceOn", value.Name);
    }

    // =====================================================================================
    //  Inicialização
    // =====================================================================================

    [ObservableProperty] private bool _applyOnStartup;
    [ObservableProperty] private bool _lockMicLevel;

    partial void OnApplyOnStartupChanged(bool value)
    {
        if (_loading) return;
        try
        {
            SoundEngine.SetStartupTask(value);
            S.ApplyOnStartup = value;
            // Sem um volume salvo não haveria o que reaplicar no microfone: fixa o atual.
            S.MicLevelPercent ??= (int)Math.Round(MicLevel);
            Save();
            StatusText = Loc.S(value ? "Snd_StartupOn" : "Snd_StartupOff");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Snd_StartupFailed", ex.Message);
            _loading = true;
            ApplyOnStartup = !value;
            _loading = false;
        }
    }

    partial void OnLockMicLevelChanged(bool value)
    {
        if (_loading) return;
        S.LockMicLevel = value;
        S.MicLevelPercent ??= (int)Math.Round(MicLevel);
        Save();
    }

    // =====================================================================================
    //  Testes ao vivo
    // =====================================================================================

    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPlayingTest;
    [ObservableProperty] private string _testStatus = "";

    [RelayCommand]
    private void ToggleMicTest()
    {
        if (IsMicTesting) { StopTests(); return; }
        if (SelectedInput is null) return;
        try
        {
            _tester.StartMeter(SelectedInput.Id);
            IsMicTesting = true;
            TestStatus = Loc.S("Snd_TestSpeak");
        }
        catch (Exception ex) { TestStatus = Loc.F("Snd_TestFailed", ex.Message); }
    }

    [RelayCommand]
    private void ToggleMonitor()
    {
        if (IsMonitoring)
        {
            _tester.StopMonitor();
            IsMonitoring = false;
            TestStatus = "";
            return;
        }
        if (SelectedInput is null || SelectedOutput is null) return;
        try
        {
            _tester.StartMonitor(SelectedInput.Id, SelectedOutput.Id);
            IsMicTesting = true;
            IsMonitoring = true;
            TestStatus = Loc.S("Snd_MonitorOn");
        }
        catch (Exception ex) { TestStatus = Loc.F("Snd_TestFailed", ex.Message); }
    }

    [RelayCommand]
    private async Task RecordAndPlayAsync()
    {
        if (IsRecording || SelectedInput is null || SelectedOutput is null) return;
        _tester.StopMonitor();
        IsMonitoring = false;
        _testCts?.Cancel();
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        IsRecording = true;
        IsMicTesting = true;
        try
        {
            TestStatus = Loc.S("Snd_Recording");
            var recording = _tester.RecordAndPlayAsync(SelectedInput.Id, SelectedOutput.Id, 5, ct);
            // A gravação dura 5 s; depois disso o que toca é a reprodução.
            _ = Task.Delay(5000, ct).ContinueWith(_ =>
                App.Current?.Dispatcher.Invoke(() => { if (IsRecording) TestStatus = Loc.S("Snd_PlayingBack"); }),
                TaskScheduler.Default);
            await recording;
            TestStatus = Loc.S("Snd_PlaybackDone");
        }
        catch (OperationCanceledException) { TestStatus = ""; }
        catch (Exception ex) { TestStatus = Loc.F("Snd_TestFailed", ex.Message); }
        finally { IsRecording = false; }
    }

    [RelayCommand]
    private async Task PlayToneAsync(string? tone)
    {
        if (SelectedOutput is null || !Enum.TryParse<TestTone>(tone, out var kind)) return;
        _testCts?.Cancel();
        _testCts = new CancellationTokenSource();
        IsPlayingTest = true;
        try
        {
            TestStatus = Loc.S("Snd_Tone" + kind);
            await _tester.PlayToneAsync(SelectedOutput.Id, kind, _testCts.Token);
            TestStatus = "";
        }
        catch (OperationCanceledException) { TestStatus = ""; }
        catch (Exception ex) { TestStatus = Loc.F("Snd_TestFailed", ex.Message); }
        finally { IsPlayingTest = false; }
    }

    [RelayCommand]
    private void StopTests()
    {
        _testCts?.Cancel();
        _tester.StopPlayback();
        _tester.StopMeter();
        IsMicTesting = false;
        IsMonitoring = false;
        TestStatus = "";
    }

    // =====================================================================================
    //  Ciclo de vida da página
    // =====================================================================================

    public void OnShown()
    {
        Refresh();
        _meterTimer.Start();
    }

    public void OnHidden()
    {
        _meterTimer.Stop();
        StopTests();
        OutputPeak = InputPeak = 0;
    }

    private void ReopenMeters()
    {
        if (_outMeter?.Id != SelectedOutput?.Id) { _outMeter?.Dispose(); _outMeter = Devices.OpenMeter(SelectedOutput?.Id); }
        if (_inMeter?.Id != SelectedInput?.Id) { _inMeter?.Dispose(); _inMeter = Devices.OpenMeter(SelectedInput?.Id); }
    }

    private void UpdateMeters()
    {
        // Queda suave, como nos medidores do Windows.
        double outNow = ToMeter(_outMeter?.Peak ?? 0);
        double inNow = ToMeter(Math.Max(_inMeter?.Peak ?? 0, IsMicTesting ? _tester.MicPeak : 0));
        OutputPeak = Math.Max(outNow, OutputPeak - 0.04);
        InputPeak = Math.Max(inNow, InputPeak - 0.04);
    }

    /// <summary>Escala em dB (-60 a 0 dB → 0 a 1): numa barra linear, a voz normal mal apareceria.</summary>
    internal static double ToMeter(double peak) =>
        peak <= 0.001 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    // =====================================================================================
    //  Persistência e aplicação
    // =====================================================================================

    private void LoadFromSettings()
    {
        for (int i = 0; i < Bands.Count; i++) Bands[i].SetSilently(S.Eq[i]);
        BassBoost = S.BassBoostDb;
        Surround = S.Surround;
        OutputGain = S.OutputGainDb;
        OutputEffectsEnabled = S.OutputEffectsEnabled;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Key == S.OutputProfile) ?? Profiles.FirstOrDefault(p => p.Key == "Custom");
        SelectedVoice = Voices.FirstOrDefault(v => v.Key == S.VoiceEffect) ?? Voices.FirstOrDefault();
        MicLowCut = S.MicLowCut;
        MicClarity = S.MicClarity;
        MicNoiseGate = S.MicNoiseGate;
        ApplyOnStartup = S.ApplyOnStartup;
        LockMicLevel = S.LockMicLevel;
    }

    private void BuildOptions()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            Profiles = SoundPresets.Profiles.Select(p => new SoundOption(p.Key,
                    Loc.S($"Snd_Profile_{p.Key}"), Loc.S($"Snd_ProfileDesc_{p.Key}"), ""))
                .Append(new SoundOption("Custom", Loc.S("Snd_Profile_Custom"), Loc.S("Snd_ProfileDesc_Custom"), ""))
                .ToList();
            Voices = SoundPresets.VoiceEffects.Select(v => new SoundOption(v.Key,
                    Loc.S($"Snd_Voice_{v.Key}"), Loc.S($"Snd_VoiceDesc_{v.Key}"), VoiceIcon(v.Key)))
                .ToList();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Key == S.OutputProfile) ?? Profiles.Last();
            SelectedVoice = Voices.FirstOrDefault(v => v.Key == S.VoiceEffect) ?? Voices.First();
        }
        finally { _loading = was; }
        OnPropertyChanged(nameof(BassBoostText));
        OnPropertyChanged(nameof(SurroundText));
        OnPropertyChanged(nameof(MicLevelDetail));
        OnPropertyChanged(nameof(OutputDefaultText));
        OnPropertyChanged(nameof(InputDefaultText));
    }

    private static string VoiceIcon(string key) => key switch
    {
        "None" => "🎙️",
        "Deep" => "🐻",
        "Monster" => "👹",
        "Thin" => "🐭",
        "Chipmunk" => "🐿️",
        "Cyborg" => "🦾",
        "Retro" => "👾",
        "Whisper" => "🤫",
        "Ghost" => "👻",
        "Drunk" => "🥴",
        "Radio" => "📻",
        "Phone" => "📞",
        "Megaphone" => "📢",
        "Robot" => "🤖",
        "Muffled" => "🧱",
        "Alien" => "👽",
        "Cave" => "🦇",
        "Echo" => "🏔️",
        _ => "🎵",
    };

    private void ScheduleApply()
    {
        Save();
        _applyDebounce.Stop();
        _applyDebounce.Start();
    }

    private async Task ApplyEffectsNowAsync()
    {
        var snapshot = S;
        var result = await Task.Run(() => _engine.ApplyEffects(snapshot));
        string? problem = result.ApoError is not null ? Loc.F("Snd_ApplyFailed", result.ApoError)
            : !result.ApoWritten && NeedsEngine() ? Loc.S("Snd_NeedsEngine")
            : null;
        if (problem is not null) StatusText = problem;
        else if (_lastApplyProblem is not null && StatusText == _lastApplyProblem) StatusText = "";
        _lastApplyProblem = problem;
    }

    private string? _lastApplyProblem;

    private bool NeedsEngine() =>
        S.OutputEffectsEnabled || S.VoiceEffect != "None" || S.MicLowCut || S.MicClarity || S.MicNoiseGate || (S.MicLevelPercent ?? 0) > 100;

    private void Save()
    {
        try { _settings.Save(); } catch { }
    }

    public void Dispose()
    {
        _lockTimer.Dispose();
        _meterTimer.Stop();
        _outMeter?.Dispose();
        _inMeter?.Dispose();
        _tester.Dispose();
    }
}
