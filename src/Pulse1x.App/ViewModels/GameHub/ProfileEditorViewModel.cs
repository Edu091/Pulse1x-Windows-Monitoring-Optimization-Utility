using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.Services.Profiles;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Uma das três opções de toda seção: não alterar, automático, personalizado.</summary>
public record ModeOption(SettingMode Mode, string Label);

/// <summary>Item com rótulo e valor, usado nos seletores de opção (boost, resfriamento, PCIe...).</summary>
public record ChoiceOption(int Value, string Label);

/// <summary>Uma etapa da sequência de inicialização, na lista reordenável.</summary>
public partial class StepItemViewModel : ObservableObject
{
    public ProfileStepKind Kind { get; }
    [ObservableProperty] private string label = "";
    [ObservableProperty] private int order;

    public StepItemViewModel(ProfileStepKind kind, int order)
    {
        Kind = kind;
        this.order = order;
        label = Loc.S($"GH_Step_{kind}");
    }
}

/// <summary>Um processo escolhido para fechar antes de jogar.</summary>
public partial class ProcessItemViewModel : ObservableObject
{
    [ObservableProperty] private string name = "";
    public ProcessItemViewModel(string name) => this.name = name;
}

/// <summary>Um aplicativo aberto junto com o jogo.</summary>
public partial class LaunchItemViewModel : ObservableObject
{
    public LaunchItem Model { get; }
    [ObservableProperty] private string path = "";
    [ObservableProperty] private string arguments = "";
    [ObservableProperty] private bool closeOnExit;

    public LaunchItemViewModel(LaunchItem model)
    {
        Model = model;
        path = model.Path;
        arguments = model.Arguments;
        closeOnExit = model.CloseOnExit;
    }

    public LaunchItem ToModel() => new() { Path = Path, Arguments = Arguments, CloseOnExit = CloseOnExit };
}

/// <summary>
/// Editor completo de um perfil. Traduz o modelo salvo em disco para controles simples — seletores,
/// interruptores e deslizantes — de modo que o usuário configure prioridade de processo, plano de
/// energia, modo do fabricante, áudio e tela sem precisar conhecer um único comando do Windows.
///
/// As listas de opções (planos de energia, modos do fabricante, dispositivos de áudio, monitores)
/// são lidas da máquina de verdade ao abrir o editor, então só aparece o que existe aqui.
/// </summary>
public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly ProfileStoreService _store;
    private readonly GameLibraryService _library;
    private readonly PowerPlanService _power;
    private readonly OemVendorService _oem;
    private readonly AudioService _audio;
    private readonly DisplayService _display;

    private readonly GameEntry _game;
    private GameProfile _profile;

    /// <summary>Fechar a janela com sucesso (true) ou cancelado (false).</summary>
    public event Action<bool>? CloseRequested;

    public string GameName => _game.Name;

    [ObservableProperty] private string profileName = "";

    // ---- Listas carregadas da máquina ----
    public ObservableCollection<PowerPlanInfo> PowerPlans { get; } = new();
    public ObservableCollection<OemMode> OemModes { get; } = new();
    public ObservableCollection<AudioDevice> OutputDevices { get; } = new();
    public ObservableCollection<AudioDevice> InputDevices { get; } = new();
    public ObservableCollection<DisplayInfo> Displays { get; } = new();
    public ObservableCollection<int> RefreshRates { get; } = new();
    public ObservableCollection<GameProfile> Presets { get; } = new();
    public ObservableCollection<StepItemViewModel> Steps { get; } = new();
    public ObservableCollection<ProcessItemViewModel> CloseProcesses { get; } = new();
    public ObservableCollection<LaunchItemViewModel> StartApps { get; } = new();
    public ObservableCollection<string> CommandsBefore { get; } = new();
    public ObservableCollection<string> CommandsAfter { get; } = new();

    public ModeOption[] Modes { get; private set; } = Array.Empty<ModeOption>();
    public ChoiceOption[] BoostModes { get; private set; } = Array.Empty<ChoiceOption>();
    public ChoiceOption[] CoolingPolicies { get; private set; } = Array.Empty<ChoiceOption>();
    public ChoiceOption[] PcieStates { get; private set; } = Array.Empty<ChoiceOption>();
    public ChoiceOption[] UsbSuspendStates { get; private set; } = Array.Empty<ChoiceOption>();
    public ChoiceOption[] Priorities { get; private set; } = Array.Empty<ChoiceOption>();
    public ChoiceOption[] NetworkProfiles { get; private set; } = Array.Empty<ChoiceOption>();

    // =====================================================================================
    //  Seções
    // =====================================================================================

    // ---- Energia ----
    [ObservableProperty] private ModeOption? powerMode;
    [ObservableProperty] private PowerPlanInfo? selectedPlan;
    [ObservableProperty] private bool useUltimate;
    [ObservableProperty] private bool cpuMinEnabled;
    [ObservableProperty] private int cpuMin = 100;
    [ObservableProperty] private bool cpuMaxEnabled;
    [ObservableProperty] private int cpuMax = 100;
    [ObservableProperty] private bool boostEnabled;
    [ObservableProperty] private ChoiceOption? boostMode;
    [ObservableProperty] private bool coolingEnabled;
    [ObservableProperty] private ChoiceOption? coolingPolicy;
    [ObservableProperty] private bool pcieEnabled;
    [ObservableProperty] private ChoiceOption? pcieState;
    [ObservableProperty] private bool usbSuspendEnabled;
    [ObservableProperty] private ChoiceOption? usbSuspendState;
    [ObservableProperty] private bool sleepEnabled;
    [ObservableProperty] private int sleepMinutes;
    [ObservableProperty] private bool displayOffEnabled;
    [ObservableProperty] private int displayOffMinutes;

    // ---- Fabricante ----
    [ObservableProperty] private OemMode? selectedOemMode;
    [ObservableProperty] private string oemVendorText = "";
    [ObservableProperty] private bool oemAvailable;

    // ---- CPU / processo ----
    [ObservableProperty] private ModeOption? cpuMode;
    [ObservableProperty] private ChoiceOption? priority;
    [ObservableProperty] private bool reserveFirstCore;
    [ObservableProperty] private bool customAffinity;
    public ObservableCollection<CoreToggleViewModel> Cores { get; } = new();

    // ---- Memória ----
    [ObservableProperty] private ModeOption? memoryMode;
    [ObservableProperty] private bool optimizeBefore = true;
    [ObservableProperty] private bool optimizeAfter;

    // ---- Latência ----
    [ObservableProperty] private ModeOption? latencyMode;
    [ObservableProperty] private bool timerEnabled;
    [ObservableProperty] private double timerMs = 0.5;
    [ObservableProperty] private bool disableUsbSuspend;
    [ObservableProperty] private bool disableWifiPowerSaving;
    [ObservableProperty] private bool lowLatencyProfile;

    // ---- Rede ----
    [ObservableProperty] private ModeOption? networkMode;
    [ObservableProperty] private ChoiceOption? networkProfile;
    [ObservableProperty] private bool flushDns;

    // ---- Áudio ----
    [ObservableProperty] private ModeOption? audioMode;
    [ObservableProperty] private bool volumeEnabled;
    [ObservableProperty] private int volume = 70;
    [ObservableProperty] private bool muteEnabled;
    [ObservableProperty] private bool muted;
    [ObservableProperty] private AudioDevice? outputDevice;
    [ObservableProperty] private AudioDevice? inputDevice;

    // ---- Tela ----
    [ObservableProperty] private ModeOption? displayMode;
    [ObservableProperty] private DisplayInfo? selectedDisplay;
    [ObservableProperty] private bool brightnessEnabled;
    [ObservableProperty] private int brightness = 80;
    [ObservableProperty] private bool refreshRateEnabled;
    [ObservableProperty] private int refreshRate;
    [ObservableProperty] private bool hdrEnabled;
    [ObservableProperty] private bool hdr;

    // ---- Apps ----
    [ObservableProperty] private ModeOption? appsMode;
    [ObservableProperty] private bool reopenClosed = true;
    [ObservableProperty] private string newProcessName = "";
    [ObservableProperty] private string newCommandBefore = "";
    [ObservableProperty] private string newCommandAfter = "";

    // ---- Geral ----
    [ObservableProperty] private bool restoreOnExit = true;
    [ObservableProperty] private bool gamingMode = true;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool isBusy;

    // =====================================================================================

    public ProfileEditorViewModel(
        GameEntry game,
        ProfileStoreService store,
        GameLibraryService library,
        PowerPlanService power,
        OemVendorService oem,
        AudioService audio,
        DisplayService display)
    {
        _game = game;
        _store = store;
        _library = library;
        _power = power;
        _oem = oem;
        _audio = audio;
        _display = display;

        BuildOptionLists();

        // Um jogo sem perfil começa a partir de uma cópia do preset Padrão, para o usuário já ver a
        // estrutura montada em vez de uma tela vazia.
        _profile = store.Find(game.ProfileId)?.Clone()
                   ?? store.Presets.FirstOrDefault(p => p.Preset == ProfilePreset.Default)?.Clone()
                   ?? new GameProfile();

        if (store.Find(game.ProfileId) is null)
        {
            _profile.Id = Guid.NewGuid().ToString("N");
            _profile.Name = game.Name;
            _profile.IsBuiltInPreset = false;
        }

        LoadFromProfile();
        _ = LoadMachineDataAsync();
    }

    private void BuildOptionLists()
    {
        Modes = new[]
        {
            new ModeOption(SettingMode.Unchanged, Loc.S("GH_ModeUnchanged")),
            new ModeOption(SettingMode.Auto, Loc.S("GH_ModeAuto")),
            new ModeOption(SettingMode.Custom, Loc.S("GH_ModeCustom")),
        };

        BoostModes = new[]
        {
            new ChoiceOption(0, Loc.S("GH_PwBoostOff")),
            new ChoiceOption(1, Loc.S("GH_PwBoostOn")),
            new ChoiceOption(2, Loc.S("GH_PwBoostAggressive")),
            new ChoiceOption(3, Loc.S("GH_PwBoostEfficient")),
            new ChoiceOption(4, Loc.S("GH_PwBoostEfficientAggressive")),
        };

        CoolingPolicies = new[]
        {
            new ChoiceOption(0, Loc.S("GH_PwCoolingPassive")),
            new ChoiceOption(1, Loc.S("GH_PwCoolingActive")),
        };

        PcieStates = new[]
        {
            new ChoiceOption(0, Loc.S("GH_PwPcieOff")),
            new ChoiceOption(1, Loc.S("GH_PwPcieModerate")),
            new ChoiceOption(2, Loc.S("GH_PwPcieMax")),
        };

        UsbSuspendStates = new[]
        {
            new ChoiceOption(0, Loc.S("GH_PwDisabled")),
            new ChoiceOption(1, Loc.S("GH_PwEnabled")),
        };

        Priorities = new[]
        {
            new ChoiceOption((int)ProcessPriority.Idle, Loc.S("GH_PrioIdle")),
            new ChoiceOption((int)ProcessPriority.BelowNormal, Loc.S("GH_PrioBelowNormal")),
            new ChoiceOption((int)ProcessPriority.Normal, Loc.S("GH_PrioNormal")),
            new ChoiceOption((int)ProcessPriority.AboveNormal, Loc.S("GH_PrioAboveNormal")),
            new ChoiceOption((int)ProcessPriority.High, Loc.S("GH_PrioHigh")),
            new ChoiceOption((int)ProcessPriority.Realtime, Loc.S("GH_PrioRealtime")),
        };

        NetworkProfiles = new[]
        {
            new ChoiceOption((int)NetworkProfileKind.Competitive, Loc.S("GH_NetCompetitive")),
            new ChoiceOption((int)NetworkProfileKind.Stability, Loc.S("GH_NetStability")),
            new ChoiceOption((int)NetworkProfileKind.AllSafe, Loc.S("GH_NetAllSafe")),
        };

        for (int i = 0; i < Environment.ProcessorCount; i++)
            Cores.Add(new CoreToggleViewModel(i));
    }

    /// <summary>Lê da máquina o que pode ser escolhido: planos, modos do fabricante, áudio e telas.</summary>
    private async Task LoadMachineDataAsync()
    {
        IsBusy = true;
        try
        {
            // Core Audio e EnumDisplaySettings podem demorar quando há drivers/dispositivos
            // desconectados. Iniciamos as leituras fora da UI para abrir o perfil pelo Start sem
            // travar a navegação do GameHub.
            var outputDevicesTask = Task.Run(() => _audio.ListDevices());
            var inputDevicesTask = Task.Run(() => _audio.ListDevices(input: true));
            var displaysTask = Task.Run(() => _display.ListDisplays());
            var plans = await _power.ListPlansAsync();
            PowerPlans.Clear();
            foreach (var plan in plans) PowerPlans.Add(plan);
            SelectedPlan = PowerPlans.FirstOrDefault(p => p.Guid == _profile.Power.PlanGuid)
                           ?? PowerPlans.FirstOrDefault(p => p.IsActive);

            await Task.Run(() =>
            {
                bool available = _oem.IsAvailable;
                var modes = _oem.AvailableModes.ToList();
                string vendor = _oem.VendorName ?? Loc.S("GH_OemNotDetected");

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    OemAvailable = available;
                    OemVendorText = available ? vendor : Loc.S("GH_OemNotDetected");
                    OemModes.Clear();
                    foreach (var mode in modes) OemModes.Add(mode);
                    SelectedOemMode = OemModes.FirstOrDefault(m => m.Id == _profile.Oem.ModeId)
                                      ?? OemModes.FirstOrDefault();
                });
            });

            foreach (var device in await outputDevicesTask) OutputDevices.Add(device);
            foreach (var device in await inputDevicesTask) InputDevices.Add(device);
            OutputDevice = OutputDevices.FirstOrDefault(d => d.Id == _profile.Audio.OutputDeviceId);
            InputDevice = InputDevices.FirstOrDefault(d => d.Id == _profile.Audio.InputDeviceId);

            foreach (var screen in await displaysTask) Displays.Add(screen);
            SelectedDisplay = Displays.FirstOrDefault(d => d.DeviceName == _profile.Display.TargetDevice)
                              ?? Displays.FirstOrDefault(d => d.IsPrimary)
                              ?? Displays.FirstOrDefault();

            Presets.Clear();
            foreach (var preset in _store.Presets) Presets.Add(preset);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedDisplayChanged(DisplayInfo? value)
    {
        RefreshRates.Clear();
        if (value is null) return;
        foreach (var rate in value.AvailableRefreshRates) RefreshRates.Add(rate);
        if (RefreshRate == 0 || !RefreshRates.Contains(RefreshRate))
            RefreshRate = value.RefreshRate;
    }

    // =====================================================================================
    //  Modelo <-> tela
    // =====================================================================================

    private void LoadFromProfile()
    {
        ProfileName = _profile.Name;

        var power = _profile.Power;
        PowerMode = ModeFor(power.Mode);
        UseUltimate = power.UseUltimatePerformance;
        CpuMinEnabled = power.CpuMinState is not null; CpuMin = power.CpuMinState ?? 100;
        CpuMaxEnabled = power.CpuMaxState is not null; CpuMax = power.CpuMaxState ?? 100;
        BoostEnabled = power.CpuBoostMode is not null;
        BoostMode = BoostModes.FirstOrDefault(o => o.Value == (power.CpuBoostMode ?? 2)) ?? BoostModes[2];
        CoolingEnabled = power.CoolingPolicy is not null;
        CoolingPolicy = CoolingPolicies.FirstOrDefault(o => o.Value == (power.CoolingPolicy ?? 1)) ?? CoolingPolicies[1];
        PcieEnabled = power.PciExpressLinkState is not null;
        PcieState = PcieStates.FirstOrDefault(o => o.Value == (power.PciExpressLinkState ?? 0)) ?? PcieStates[0];
        UsbSuspendEnabled = power.UsbSelectiveSuspend is not null;
        UsbSuspendState = UsbSuspendStates.FirstOrDefault(o => o.Value == (power.UsbSelectiveSuspend ?? 0)) ?? UsbSuspendStates[0];
        SleepEnabled = power.SleepTimeoutMinutes is not null; SleepMinutes = power.SleepTimeoutMinutes ?? 0;
        DisplayOffEnabled = power.DisplayTimeoutMinutes is not null; DisplayOffMinutes = power.DisplayTimeoutMinutes ?? 0;

        OemModeSelection = ModeFor(_profile.Oem.Mode);

        var cpu = _profile.Cpu;
        CpuMode = ModeFor(cpu.Mode);
        Priority = Priorities.FirstOrDefault(p => p.Value == (int)cpu.Priority) ?? Priorities[2];
        ReserveFirstCore = cpu.ReserveFirstCore;
        CustomAffinity = cpu.AffinityMask != 0;
        for (int i = 0; i < Cores.Count; i++)
            Cores[i].IsEnabled = cpu.AffinityMask == 0 || (cpu.AffinityMask & (1L << i)) != 0;

        MemoryMode = ModeFor(_profile.Memory.Mode);
        OptimizeBefore = _profile.Memory.OptimizeBeforeLaunch;
        OptimizeAfter = _profile.Memory.OptimizeAfterExit;

        var latency = _profile.Latency;
        LatencyMode = ModeFor(latency.Mode);
        TimerEnabled = latency.TimerResolutionMs is not null;
        TimerMs = latency.TimerResolutionMs ?? 0.5;
        DisableUsbSuspend = latency.DisableUsbSelectiveSuspend;
        DisableWifiPowerSaving = latency.DisableWifiPowerSaving;
        LowLatencyProfile = latency.LowLatencyProfile;

        NetworkMode = ModeFor(_profile.Network.Mode);
        NetworkProfile = NetworkProfiles.FirstOrDefault(n => n.Value == (int)_profile.Network.Kind) ?? NetworkProfiles[0];
        FlushDns = _profile.Network.FlushDnsBefore;

        var audio = _profile.Audio;
        AudioMode = ModeFor(audio.Mode);
        VolumeEnabled = audio.Volume is not null; Volume = audio.Volume ?? 70;
        MuteEnabled = audio.Muted is not null; Muted = audio.Muted ?? false;

        var display = _profile.Display;
        DisplayMode = ModeFor(display.Mode);
        BrightnessEnabled = display.Brightness is not null; Brightness = display.Brightness ?? 80;
        RefreshRateEnabled = display.RefreshRate is not null; RefreshRate = display.RefreshRate ?? 0;
        HdrEnabled = display.Hdr is not null; Hdr = display.Hdr ?? false;

        var apps = _profile.Apps;
        AppsMode = ModeFor(apps.Mode);
        ReopenClosed = apps.ReopenClosedOnExit;
        CloseProcesses.Clear();
        foreach (var name in apps.CloseProcesses) CloseProcesses.Add(new ProcessItemViewModel(name));
        StartApps.Clear();
        foreach (var app in apps.StartApps) StartApps.Add(new LaunchItemViewModel(app));
        CommandsBefore.Clear();
        foreach (var command in apps.CommandsBefore) CommandsBefore.Add(command);
        CommandsAfter.Clear();
        foreach (var command in apps.CommandsAfter) CommandsAfter.Add(command);

        RestoreOnExit = _profile.RestoreOnExit;
        GamingMode = _profile.GamingModeWhileRunning;

        Steps.Clear();
        int order = 1;
        foreach (var step in _profile.EffectiveStepOrder())
            Steps.Add(new StepItemViewModel(step, order++));
    }

    /// <summary>Modo da seção do fabricante (nome separado para não colidir com <see cref="OemMode"/>).</summary>
    [ObservableProperty] private ModeOption? oemModeSelection;

    private ModeOption ModeFor(SettingMode mode) => Modes.First(m => m.Mode == mode);

    private void SaveToProfile()
    {
        _profile.Name = string.IsNullOrWhiteSpace(ProfileName) ? _game.Name : ProfileName.Trim();

        var power = _profile.Power;
        power.Mode = PowerMode?.Mode ?? SettingMode.Unchanged;
        power.PlanGuid = SelectedPlan?.Guid;
        power.PlanName = SelectedPlan?.Name;
        power.UseUltimatePerformance = UseUltimate;
        power.CpuMinState = CpuMinEnabled ? CpuMin : null;
        power.CpuMaxState = CpuMaxEnabled ? CpuMax : null;
        power.CpuBoostMode = BoostEnabled ? BoostMode?.Value : null;
        power.CoolingPolicy = CoolingEnabled ? CoolingPolicy?.Value : null;
        power.PciExpressLinkState = PcieEnabled ? PcieState?.Value : null;
        power.UsbSelectiveSuspend = UsbSuspendEnabled ? UsbSuspendState?.Value : null;
        power.SleepTimeoutMinutes = SleepEnabled ? SleepMinutes : null;
        power.DisplayTimeoutMinutes = DisplayOffEnabled ? DisplayOffMinutes : null;

        _profile.Oem.Mode = OemModeSelection?.Mode ?? SettingMode.Unchanged;
        _profile.Oem.VendorId = _oem.VendorId;
        _profile.Oem.ModeId = SelectedOemMode?.Id;

        var cpu = _profile.Cpu;
        cpu.Mode = CpuMode?.Mode ?? SettingMode.Unchanged;
        cpu.Priority = (ProcessPriority)(Priority?.Value ?? (int)ProcessPriority.Normal);
        cpu.ReserveFirstCore = ReserveFirstCore;
        cpu.AffinityMask = 0;
        if (CustomAffinity)
        {
            long mask = 0;
            for (int i = 0; i < Cores.Count; i++)
                if (Cores[i].IsEnabled) mask |= 1L << i;
            // Uma máscara vazia travaria o processo; nesse caso voltamos para "automática".
            cpu.AffinityMask = mask;
        }

        _profile.Memory.Mode = MemoryMode?.Mode ?? SettingMode.Unchanged;
        _profile.Memory.OptimizeBeforeLaunch = OptimizeBefore;
        _profile.Memory.OptimizeAfterExit = OptimizeAfter;

        var latency = _profile.Latency;
        latency.Mode = LatencyMode?.Mode ?? SettingMode.Unchanged;
        latency.TimerResolutionMs = TimerEnabled ? TimerMs : null;
        latency.DisableUsbSelectiveSuspend = DisableUsbSuspend;
        latency.DisableWifiPowerSaving = DisableWifiPowerSaving;
        latency.LowLatencyProfile = LowLatencyProfile;

        _profile.Network.Mode = NetworkMode?.Mode ?? SettingMode.Unchanged;
        _profile.Network.Kind = (NetworkProfileKind)(NetworkProfile?.Value ?? 0);
        _profile.Network.FlushDnsBefore = FlushDns;

        var audio = _profile.Audio;
        audio.Mode = AudioMode?.Mode ?? SettingMode.Unchanged;
        audio.Volume = VolumeEnabled ? Volume : null;
        audio.Muted = MuteEnabled ? Muted : null;
        audio.OutputDeviceId = OutputDevice?.Id;
        audio.OutputDeviceName = OutputDevice?.Name;
        audio.InputDeviceId = InputDevice?.Id;
        audio.InputDeviceName = InputDevice?.Name;

        var display = _profile.Display;
        display.Mode = DisplayMode?.Mode ?? SettingMode.Unchanged;
        display.TargetDevice = SelectedDisplay?.DeviceName;
        display.Brightness = BrightnessEnabled ? Brightness : null;
        display.RefreshRate = RefreshRateEnabled ? RefreshRate : null;
        display.Hdr = HdrEnabled ? Hdr : null;

        var apps = _profile.Apps;
        apps.Mode = AppsMode?.Mode ?? SettingMode.Unchanged;
        apps.ReopenClosedOnExit = ReopenClosed;
        apps.CloseProcesses = CloseProcesses.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        apps.StartApps = StartApps.Select(a => a.ToModel()).Where(a => !string.IsNullOrWhiteSpace(a.Path)).ToList();
        apps.CommandsBefore = CommandsBefore.ToList();
        apps.CommandsAfter = CommandsAfter.ToList();

        _profile.RestoreOnExit = RestoreOnExit;
        _profile.GamingModeWhileRunning = GamingMode;
        _profile.StepOrder = Steps.Select(s => s.Kind).ToList();
    }

    // =====================================================================================
    //  Comandos
    // =====================================================================================

    [RelayCommand]
    private void Save()
    {
        SaveToProfile();
        _store.AddOrUpdate(_profile);

        _game.ProfileId = _profile.Id;
        _library.UpdateGame(_game);

        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Remove o perfil deste jogo (volta a "não alterar nada").</summary>
    [RelayCommand]
    private void RemoveProfile()
    {
        _game.ProfileId = null;
        _library.UpdateGame(_game);
        if (!_profile.IsBuiltInPreset) _store.Remove(_profile.Id);
        CloseRequested?.Invoke(true);
    }

    /// <summary>Substitui os valores atuais pelos de um preset, mantendo o nome do perfil.</summary>
    [RelayCommand]
    private void ApplyPreset(GameProfile? preset)
    {
        if (preset is null) return;

        string id = _profile.Id;
        string name = _profile.Name;
        _profile = preset.Clone();
        _profile.Id = id;
        _profile.Name = name;
        _profile.IsBuiltInPreset = false;

        LoadFromProfile();
        StatusMessage = Loc.F("GH_PresetApplied", preset.Name);
    }

    /// <summary>Salva a configuração atual como um novo preset reutilizável.</summary>
    [RelayCommand]
    private void SaveAsPreset()
    {
        SaveToProfile();
        string name = string.IsNullOrWhiteSpace(ProfileName) ? _game.Name : ProfileName.Trim();
        var preset = _store.SaveAsPreset(_profile, name);
        Presets.Add(preset);
        StatusMessage = Loc.F("GH_PresetSaved", preset.Name);
    }

    [RelayCommand]
    private void MoveStepUp(StepItemViewModel? step)
    {
        if (step is null) return;
        int index = Steps.IndexOf(step);
        if (index <= 0) return;
        Steps.Move(index, index - 1);
        RenumberSteps();
    }

    [RelayCommand]
    private void MoveStepDown(StepItemViewModel? step)
    {
        if (step is null) return;
        int index = Steps.IndexOf(step);
        if (index < 0 || index >= Steps.Count - 1) return;
        Steps.Move(index, index + 1);
        RenumberSteps();
    }

    [RelayCommand]
    private void ResetSteps()
    {
        Steps.Clear();
        int order = 1;
        foreach (var step in GameProfile.DefaultStepOrder)
            Steps.Add(new StepItemViewModel(step, order++));
    }

    private void RenumberSteps()
    {
        for (int i = 0; i < Steps.Count; i++) Steps[i].Order = i + 1;
    }

    [RelayCommand]
    private void AddCloseProcess()
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)) return;
        string name = System.IO.Path.GetFileNameWithoutExtension(NewProcessName.Trim());
        if (CloseProcesses.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        CloseProcesses.Add(new ProcessItemViewModel(name));
        NewProcessName = "";
    }

    [RelayCommand]
    private void RemoveCloseProcess(ProcessItemViewModel? item)
    {
        if (item is not null) CloseProcesses.Remove(item);
    }

    [RelayCommand]
    private void RemoveStartApp(LaunchItemViewModel? item)
    {
        if (item is not null) StartApps.Remove(item);
    }

    [RelayCommand]
    private void AddCommandBefore()
    {
        if (string.IsNullOrWhiteSpace(NewCommandBefore)) return;
        CommandsBefore.Add(NewCommandBefore.Trim());
        NewCommandBefore = "";
    }

    [RelayCommand]
    private void AddCommandAfter()
    {
        if (string.IsNullOrWhiteSpace(NewCommandAfter)) return;
        CommandsAfter.Add(NewCommandAfter.Trim());
        NewCommandAfter = "";
    }

    [RelayCommand]
    private void RemoveCommandBefore(string? command)
    {
        if (command is not null) CommandsBefore.Remove(command);
    }

    [RelayCommand]
    private void RemoveCommandAfter(string? command)
    {
        if (command is not null) CommandsAfter.Remove(command);
    }

    /// <summary>Adiciona um app à lista "abrir junto" (o caminho vem do seletor de arquivos).</summary>
    public void AddStartApp(string path) =>
        StartApps.Add(new LaunchItemViewModel(new LaunchItem { Path = path, CloseOnExit = true }));

    /// <summary>Processos com janela aberta agora — alimenta o seletor "fechar antes de jogar".</summary>
    public static IReadOnlyList<string> RunningProcesses() =>
        ProcessControlService.ListUserProcesses().Select(p => p.Name).ToList();

    /// <summary>
    /// Torna visíveis no Windows as configurações avançadas que ele esconde (CPU Boost e política de
    /// resfriamento). O perfil funciona sem isso; serve para quem quer conferir pelo Painel de
    /// Controle o que o Pulse1x aplicou.
    /// </summary>
    [RelayCommand]
    private async Task UnhideAdvancedAsync()
    {
        IsBusy = true;
        try
        {
            await _power.UnhideAdvancedSettingsAsync();
            StatusMessage = Loc.S("GH_PwUnhidden");
        }
        finally { IsBusy = false; }
    }

    /// <summary>Sonda de novo o software do fabricante (botão de diagnóstico da seção Notebook).</summary>
    [RelayCommand]
    private async Task DetectOemAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                _oem.Rescan();
                bool available = _oem.IsAvailable;
                var modes = _oem.AvailableModes.ToList();
                string vendor = _oem.VendorName ?? "";
                string? current = _oem.GetCurrentMode();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    OemAvailable = available;
                    OemVendorText = available ? vendor : Loc.S("GH_OemNotDetected");
                    OemModes.Clear();
                    foreach (var mode in modes) OemModes.Add(mode);
                    SelectedOemMode = OemModes.FirstOrDefault(m => m.Id == current) ?? OemModes.FirstOrDefault();
                    StatusMessage = available
                        ? Loc.F("GH_OemDetected", vendor, current ?? "?")
                        : Loc.S("GH_OemNotDetected");
                });
            });
        }
        finally { IsBusy = false; }
    }
}

/// <summary>Um núcleo lógico da CPU no seletor de afinidade.</summary>
public partial class CoreToggleViewModel : ObservableObject
{
    public int Index { get; }
    public string Label => $"CPU {Index}";
    [ObservableProperty] private bool isEnabled = true;

    public CoreToggleViewModel(int index) => Index = index;
}
