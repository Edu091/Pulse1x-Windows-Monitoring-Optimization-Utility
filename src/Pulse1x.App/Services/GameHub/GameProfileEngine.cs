using System.IO;
using System.Diagnostics;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.Profiles;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Andamento de uma etapa da sequência, para a interface mostrar o que está acontecendo.</summary>
public record ProfileStepProgress(ProfileStepKind Step, string LabelKey, bool Success, string? Detail = null);

/// <summary>
/// Executa a sequência de um perfil: salva o estado atual, aplica cada ajuste na ordem escolhida
/// pelo usuário, inicia o jogo e, ao final, devolve tudo ao que era antes.
///
/// Dois princípios guiam toda a classe:
///
///  • Nada é alterado sem antes ser lido. Cada etapa grava o valor original no snapshot (que já
///    está em disco) antes de escrever o novo. É isso que torna a restauração confiável.
///
///  • Uma etapa que falha não interrompe a sequência. Se a máquina não tem HDR, se o software do
///    fabricante não responde ou se um app não abriu, isso vira um aviso e o jogo abre do mesmo
///    jeito — o usuário nunca fica sem jogar por causa de um ajuste secundário.
///
/// Os módulos que o Pulse1x já tinha são reaproveitados como estão: a otimização de RAM é a do
/// MemoryOptimizationService e os perfis de rede são os do NetworkOptimizationService, com o mesmo
/// registro de reversão da categoria Latência.
/// </summary>
public class GameProfileEngine
{
    private readonly PowerPlanService _power;
    private readonly OemVendorService _oem;
    private readonly AudioService _audio;
    private readonly DisplayService _display;
    private readonly TimerResolutionService _timer;
    private readonly ProcessControlService _processes;
    private readonly MemoryOptimizationService _memory;
    private readonly NetworkOptimizationService _network;
    private readonly SnapshotService _snapshots;

    public GameProfileEngine(
        PowerPlanService power,
        OemVendorService oem,
        AudioService audio,
        DisplayService display,
        TimerResolutionService timer,
        ProcessControlService processes,
        MemoryOptimizationService memory,
        NetworkOptimizationService network,
        SnapshotService snapshots)
    {
        _power = power;
        _oem = oem;
        _audio = audio;
        _display = display;
        _timer = timer;
        _processes = processes;
        _memory = memory;
        _network = network;
        _snapshots = snapshots;
    }

    public ProcessControlService Processes => _processes;
    public OemVendorService Oem => _oem;
    public PowerPlanService Power => _power;
    public AudioService Audio => _audio;
    public DisplayService Display => _display;
    public TimerResolutionService Timer => _timer;

    // =====================================================================================
    //  Aplicar
    // =====================================================================================

    /// <summary>
    /// Roda a sequência do perfil. Devolve o snapshot (para a restauração) e o processo iniciado,
    /// quando houver. <paramref name="progress"/> recebe cada etapa concluída.
    /// </summary>
    public async Task<(SystemSnapshot Snapshot, Process? Launched)> ApplyAsync(
        GameEntry game, GameProfile? profile, IProgress<ProfileStepProgress>? progress = null)
    {
        var snapshot = new SystemSnapshot
        {
            GameId = game.Id,
            GameName = game.Name,
            ProfileId = profile?.Id,
        };

        Process? launched = null;

        // Sem perfil, o GameHub é só uma biblioteca: inicia o jogo e não toca em nada do sistema.
        if (profile is null)
        {
            launched = LaunchGame(game);
            Report(progress, ProfileStepKind.LaunchGame, "GH_StepLaunch", launched is not null);
            return (snapshot, launched);
        }

        foreach (var step in profile.EffectiveStepOrder())
        {
            try
            {
                switch (step)
                {
                    case ProfileStepKind.SaveSnapshot:
                        // Só grava o arquivo; o conteúdo vai sendo preenchido pelas etapas seguintes,
                        // e cada uma regrava antes de alterar o sistema.
                        _snapshots.Persist(snapshot);
                        Report(progress, step, "GH_StepSnapshot", true);
                        break;

                    case ProfileStepKind.PowerPlan:
                        await ApplyPowerPlanAsync(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.PowerAdvanced:
                        await ApplyPowerAdvancedAsync(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.OemMode:
                        ApplyOemMode(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.Audio:
                        ApplyAudio(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.Display:
                        ApplyDisplay(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.TimerResolution:
                        ApplyTimer(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.LatencyTweaks:
                        await ApplyLatencyAsync(profile, progress);
                        break;

                    case ProfileStepKind.MemoryOptimize:
                        await ApplyMemoryAsync(profile, progress);
                        break;

                    case ProfileStepKind.NetworkProfile:
                        await ApplyNetworkAsync(profile, progress);
                        break;

                    case ProfileStepKind.CloseProcesses:
                        await ApplyCloseProcessesAsync(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.RunCommandsBefore:
                        await RunCommandsAsync(profile.Apps, before: true, progress);
                        break;

                    case ProfileStepKind.StartApps:
                        ApplyStartApps(profile, snapshot, progress);
                        break;

                    case ProfileStepKind.LaunchGame:
                        launched = LaunchGame(game);
                        Report(progress, step, "GH_StepLaunch", launched is not null);
                        break;

                    case ProfileStepKind.ProcessTuning:
                        // Precisa do jogo já rodando; é tratado por AttachToProcess quando o
                        // processo principal for detectado.
                        break;
                }
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add($"{step}: {ex.Message}");
                Report(progress, step, "GH_StepFailed", false, ex.Message);
            }
        }

        _snapshots.Persist(snapshot);
        return (snapshot, launched);
    }

    /// <summary>
    /// Ajusta prioridade e afinidade depois que o processo principal do jogo foi identificado.
    /// É a última etapa da sequência, porque só aí existe um processo para ajustar.
    /// </summary>
    public void AttachToProcess(GameProfile? profile, Process process, IProgress<ProfileStepProgress>? progress = null)
    {
        if (profile?.Cpu.IsActive != true) return;

        bool priority = _processes.SetPriority(process, profile.Cpu.Mode == SettingMode.Auto
            ? Models.GameHub.ProcessPriority.High
            : profile.Cpu.Priority);

        bool affinity = profile.Cpu.Mode == SettingMode.Auto
            || _processes.SetAffinity(process, profile.Cpu.AffinityMask, profile.Cpu.ReserveFirstCore);

        Report(progress, ProfileStepKind.ProcessTuning, "GH_StepProcessTuning", priority && affinity);
    }

    // ---- Etapas ----

    private async Task ApplyPowerPlanAsync(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Power.IsActive) return;

        var current = await _power.GetActivePlanAsync();
        if (current is not null)
        {
            snapshot.PowerPlanGuid = current.Guid;
            snapshot.PowerPlanName = current.Name;
            _snapshots.Persist(snapshot);
        }

        string? target = profile.Power.PlanGuid;

        if (profile.Power.UseUltimatePerformance || profile.Power.Mode == SettingMode.Auto)
            target = await _power.EnsureUltimatePlanAsync() ?? PowerPlanService.HighPerformanceGuid;

        if (string.IsNullOrEmpty(target)) return;

        bool ok = await _power.SetActivePlanAsync(target);
        Report(progress, ProfileStepKind.PowerPlan, "GH_StepPowerPlan", ok, profile.Power.PlanName);
    }

    private async Task ApplyPowerAdvancedAsync(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Power.IsActive) return;

        var wanted = CollectPowerSettings(profile.Power);
        if (wanted.Count == 0) return;

        // Lê os valores atuais ANTES de escrever e grava o snapshot em disco.
        snapshot.PowerSettings = await _power.CaptureAsync(
            wanted.Select(w => (w.SubGroupGuid, w.SettingGuid, w.Label)));
        _snapshots.Persist(snapshot);

        bool all = true;
        foreach (var setting in wanted)
        {
            if (setting.AcValue is null) continue;
            all &= await _power.WriteSettingAsync(
                setting.SubGroupGuid, setting.SettingGuid, setting.AcValue.Value, setting.DcValue);
        }

        Report(progress, ProfileStepKind.PowerAdvanced, "GH_StepPowerAdvanced", all, $"{wanted.Count}");
    }

    /// <summary>Junta os atalhos amigáveis e os extras num único conjunto de configurações a gravar.</summary>
    private static List<PowerSettingValue> CollectPowerSettings(PowerProfileSection power)
    {
        var list = new List<PowerSettingValue>();

        void Add(int? value, string sub, string setting, string label)
        {
            if (value is null) return;
            list.Add(new PowerSettingValue { SubGroupGuid = sub, SettingGuid = setting, Label = label, AcValue = value });
        }

        Add(power.CpuMinState, PowerPlanService.SubProcessor, PowerPlanService.SetCpuMin, "CpuMin");
        Add(power.CpuMaxState, PowerPlanService.SubProcessor, PowerPlanService.SetCpuMax, "CpuMax");
        Add(power.CpuBoostMode, PowerPlanService.SubProcessor, PowerPlanService.SetBoostMode, "Boost");
        Add(power.CoolingPolicy, PowerPlanService.SubProcessor, PowerPlanService.SetCoolingPolicy, "Cooling");
        Add(power.PciExpressLinkState, PowerPlanService.SubPciExpress, PowerPlanService.SetPciAspm, "Pcie");
        Add(power.UsbSelectiveSuspend, PowerPlanService.SubUsb, PowerPlanService.SetUsbSuspend, "UsbSuspend");
        Add(power.SleepTimeoutMinutes, PowerPlanService.SubSleep, PowerPlanService.SetStandbyIdle, "Sleep");
        Add(power.DisplayTimeoutMinutes, PowerPlanService.SubVideo, PowerPlanService.SetVideoIdle, "Video");
        Add(power.HardDiskTimeoutMinutes, PowerPlanService.SubDisk, PowerPlanService.SetDiskIdle, "Disk");

        foreach (var extra in power.Extra)
            if (extra.AcValue is not null)
                list.Add(extra);

        return list;
    }

    private void ApplyOemMode(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Oem.IsActive) return;
        if (!_oem.IsAvailable)
        {
            Report(progress, ProfileStepKind.OemMode, "GH_StepOemUnavailable", false);
            return;
        }

        snapshot.OemVendorId = _oem.VendorId;
        snapshot.OemModeId = _oem.GetCurrentMode();
        _snapshots.Persist(snapshot);

        string? target = profile.Oem.ModeId;
        if (target is null) return;

        // O modo pedido pode não existir neste modelo (um notebook sem "turbo", por exemplo):
        // caímos para o mais próximo disponível em vez de simplesmente não fazer nada.
        if (_oem.AvailableModes.All(m => m.Id != target))
            target = NearestMode(target, _oem.AvailableModes);

        bool ok = target is not null && _oem.SetMode(target);
        Report(progress, ProfileStepKind.OemMode, "GH_StepOem", ok, target);
    }

    /// <summary>Escolhe o modo disponível mais próximo, na escala eco → quiet → balanced → performance → turbo.</summary>
    private static string? NearestMode(string wanted, IReadOnlyList<OemMode> available)
    {
        string[] scale = { "eco", "quiet", "balanced", "performance", "turbo" };
        int target = Array.IndexOf(scale, wanted);
        if (target < 0) return available.FirstOrDefault()?.Id;

        return available
            .Where(m => Array.IndexOf(scale, m.Id) >= 0)
            .OrderBy(m => Math.Abs(Array.IndexOf(scale, m.Id) - target))
            .FirstOrDefault()?.Id;
    }

    private void ApplyAudio(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Audio.IsActive) return;

        snapshot.Volume = _audio.GetVolume();
        snapshot.Muted = _audio.GetMuted();
        if (profile.Audio.OutputDeviceId is not null) snapshot.DefaultOutputDeviceId = _audio.GetDefaultDeviceId();
        if (profile.Audio.InputDeviceId is not null) snapshot.DefaultInputDeviceId = _audio.GetDefaultDeviceId(input: true);
        _snapshots.Persist(snapshot);

        bool ok = true;
        // A troca de dispositivo vem antes do volume: o volume pertence ao dispositivo ativo, então
        // trocar depois ajustaria o volume do aparelho errado.
        if (profile.Audio.OutputDeviceId is { Length: > 0 } output) ok &= _audio.SetDefaultDevice(output);
        if (profile.Audio.InputDeviceId is { Length: > 0 } input) ok &= _audio.SetDefaultDevice(input);
        if (profile.Audio.Volume is int volume) ok &= _audio.SetVolume(volume);
        if (profile.Audio.Muted is bool muted) ok &= _audio.SetMuted(muted);

        Report(progress, ProfileStepKind.Audio, "GH_StepAudio", ok,
            profile.Audio.Volume is int v ? $"{v}%" : null);
    }

    private void ApplyDisplay(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Display.IsActive) return;

        string? device = profile.Display.TargetDevice;
        snapshot.DisplayDevice = device;
        if (profile.Display.Brightness is not null) snapshot.Brightness = _display.GetBrightness();
        if (profile.Display.RefreshRate is not null) snapshot.RefreshRate = _display.GetRefreshRate(device);
        if (profile.Display.Hdr is not null) snapshot.Hdr = _display.GetHdrEnabled();
        _snapshots.Persist(snapshot);

        bool ok = true;
        if (profile.Display.Brightness is int brightness) ok &= _display.SetBrightness(brightness);
        if (profile.Display.RefreshRate is int hz) ok &= _display.SetRefreshRate(hz, device);
        if (profile.Display.Hdr is bool hdr) ok &= _display.SetHdrEnabled(hdr);

        Report(progress, ProfileStepKind.Display, "GH_StepDisplay", ok);
    }

    private void ApplyTimer(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Latency.IsActive) return;

        double? target = profile.Latency.Mode == SettingMode.Auto ? 0.5 : profile.Latency.TimerResolutionMs;
        if (target is null) return;

        snapshot.TimerResolutionMs = _timer.CurrentMs;
        _snapshots.Persist(snapshot);

        bool ok = _timer.Apply(target.Value);
        Report(progress, ProfileStepKind.TimerResolution, "GH_StepTimer", ok, $"{target.Value:0.##} ms");
    }

    private async Task ApplyLatencyAsync(GameProfile profile, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Latency.IsActive) return;

        bool auto = profile.Latency.Mode == SettingMode.Auto;
        bool any = false;

        // Estas ferramentas são as mesmas da categoria Latência e já se registram no log de
        // reversão network-changes.json — "Desfazer Todas as Alterações" continua desfazendo.
        if (auto || profile.Latency.DisableUsbSelectiveSuspend)
        {
            await _network.DisableSelectiveSuspendAsync();
            any = true;
        }
        if (auto || profile.Latency.DisableWifiPowerSaving)
        {
            await _network.DisableWifiPowerSavingAsync();
            any = true;
        }
        if (profile.Latency.LowLatencyProfile)
        {
            await _network.ApplyCompetitiveProfileAsync();
            any = true;
        }

        if (any) Report(progress, ProfileStepKind.LatencyTweaks, "GH_StepLatency", true);
    }

    private async Task ApplyMemoryAsync(GameProfile profile, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Memory.IsActive) return;
        if (profile.Memory.Mode == SettingMode.Custom && !profile.Memory.OptimizeBeforeLaunch) return;

        var result = await _memory.OptimizeAsync();
        Report(progress, ProfileStepKind.MemoryOptimize, "GH_StepMemory", result.Success, $"{result.FreedMb:0} MB");
    }

    private async Task ApplyNetworkAsync(GameProfile profile, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Network.IsActive) return;

        if (profile.Network.FlushDnsBefore) await _network.FlushDnsAsync();

        var kind = profile.Network.Mode == SettingMode.Auto ? NetworkProfileKind.Competitive : profile.Network.Kind;
        var result = kind switch
        {
            NetworkProfileKind.Competitive => await _network.ApplyCompetitiveProfileAsync(),
            NetworkProfileKind.Stability => await _network.ApplyStabilityProfileAsync(),
            _ => await _network.ApplyAllAsync(),
        };

        Report(progress, ProfileStepKind.NetworkProfile, "GH_StepNetwork", result.Success, kind.ToString());
    }

    private async Task ApplyCloseProcessesAsync(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Apps.IsActive || profile.Apps.CloseProcesses.Count == 0) return;

        var closed = await _processes.CloseProcessesAsync(profile.Apps.CloseProcesses);
        snapshot.ClosedProcesses = closed;
        _snapshots.Persist(snapshot);

        Report(progress, ProfileStepKind.CloseProcesses, "GH_StepCloseApps", true, $"{closed.Count}");
    }

    private void ApplyStartApps(GameProfile profile, SystemSnapshot snapshot, IProgress<ProfileStepProgress>? progress)
    {
        if (!profile.Apps.IsActive || profile.Apps.StartApps.Count == 0) return;

        snapshot.StartedProcessIds = _processes.StartApps(profile.Apps.StartApps);
        _snapshots.Persist(snapshot);

        Report(progress, ProfileStepKind.StartApps, "GH_StepStartApps", true, $"{profile.Apps.StartApps.Count}");
    }

    private async Task RunCommandsAsync(AppsProfileSection apps, bool before, IProgress<ProfileStepProgress>? progress)
    {
        var commands = before ? apps.CommandsBefore : apps.CommandsAfter;
        if (!apps.IsActive || commands.Count == 0) return;

        foreach (var command in commands)
            await _processes.RunCommandAsync(command);

        Report(progress, ProfileStepKind.RunCommandsBefore, "GH_StepCommands", true, $"{commands.Count}");
    }

    // =====================================================================================
    //  Iniciar o jogo
    // =====================================================================================

    /// <summary>
    /// Inicia o item. Endereços de protocolo (steam://, com.epicgames.launcher://) e executáveis
    /// comuns usam o mesmo caminho do Explorer (ShellExecute), que é o que faz o launcher assumir
    /// a partir dali.
    /// </summary>
    public static Process? LaunchGame(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.Executable)) return null;

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = game.Executable,
                Arguments = game.Arguments,
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(game.WorkingDirectory) && Directory.Exists(game.WorkingDirectory))
                info.WorkingDirectory = game.WorkingDirectory;

            return Process.Start(info);
        }
        catch { return null; }
    }

    // =====================================================================================
    //  Restaurar
    // =====================================================================================

    /// <summary>
    /// Devolve o sistema ao estado do snapshot. É idempotente e tolerante: cada item é restaurado
    /// isoladamente, então um erro em um deles não impede os outros de voltarem. Ao terminar, o
    /// arquivo pendente é apagado.
    /// </summary>
    public async Task RestoreAsync(SystemSnapshot snapshot, GameProfile? profile = null,
        IProgress<ProfileStepProgress>? progress = null)
    {
        // Comandos "depois de finalizar" rodam primeiro, para poderem contar com o sistema ainda
        // no estado do jogo (por exemplo, salvar algo antes de a configuração voltar).
        if (profile is not null)
            await RunCommandsAsync(profile.Apps, before: false, progress);

        Try(() => _timer.Restore());

        Try(() =>
        {
            if (snapshot.Volume is int volume) _audio.SetVolume(volume);
            if (snapshot.Muted is bool muted) _audio.SetMuted(muted);
            if (snapshot.DefaultOutputDeviceId is { Length: > 0 } output) _audio.SetDefaultDevice(output);
            if (snapshot.DefaultInputDeviceId is { Length: > 0 } input) _audio.SetDefaultDevice(input);
        });

        Try(() =>
        {
            if (snapshot.Brightness is int brightness) _display.SetBrightness(brightness);
            if (snapshot.RefreshRate is int hz) _display.SetRefreshRate(hz, snapshot.DisplayDevice);
            if (snapshot.Hdr is bool hdr) _display.SetHdrEnabled(hdr);
        });

        Try(() =>
        {
            if (snapshot.OemModeId is { Length: > 0 } mode) _oem.SetMode(mode);
        });

        // As configurações avançadas voltam ANTES do plano: elas foram gravadas no plano que estava
        // ativo na hora da captura, que é justamente o que vamos reativar em seguida.
        if (snapshot.PowerSettings.Count > 0)
            await TryAsync(() => _power.RestoreAsync(snapshot.PowerSettings, snapshot.PowerPlanGuid));

        if (snapshot.PowerPlanGuid is { Length: > 0 } plan)
            await TryAsync(() => _power.SetActivePlanAsync(plan));

        Try(() =>
        {
            if (snapshot.StartedProcessIds.Count > 0) _processes.CloseStartedApps(snapshot.StartedProcessIds);
        });

        Try(() =>
        {
            bool reopen = profile?.Apps.ReopenClosedOnExit ?? true;
            if (reopen && snapshot.ClosedProcesses.Count > 0) _processes.ReopenProcesses(snapshot.ClosedProcesses);
        });

        if (profile?.Memory.OptimizeAfterExit == true)
            await TryAsync(async () => await _memory.OptimizeAsync());

        snapshot.Restored = true;
        _snapshots.Clear();

        Report(progress, ProfileStepKind.SaveSnapshot, "GH_StepRestored", true);
    }

    // =====================================================================================

    private static void Try(Action action)
    {
        try { action(); } catch { /* restaurar é "melhor esforço": nunca lança */ }
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); } catch { }
    }

    private static void Report(IProgress<ProfileStepProgress>? progress, ProfileStepKind step,
        string labelKey, bool success, string? detail = null) =>
        progress?.Report(new ProfileStepProgress(step, labelKey, success, detail));
}
