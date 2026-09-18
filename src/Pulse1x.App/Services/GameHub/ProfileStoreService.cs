using System.IO;
using System.Text.Json;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.Profiles;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Guarda os perfis do GameHub em <c>%APPDATA%\Pulse1x\gamehub-profiles.json</c> e mantém os
/// presets prontos (Padrão, Competitivo, Máximo Desempenho, Balanceado, Silencioso, Economia).
///
/// Os presets são criados na primeira execução como perfis normais e editáveis: quem quiser mudar
/// o que "Competitivo" significa, muda. Ao aplicar um preset a um jogo, o que vai para o jogo é uma
/// CÓPIA — editar o perfil de um jogo nunca altera o preset nem os outros jogos.
/// </summary>
public class ProfileStoreService
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private ProfileLibraryData _data = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action? Changed;

    public ProfileStoreService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gamehub-profiles.json");
        Load();
        EnsurePresets();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<ProfileLibraryData>(File.ReadAllText(_filePath));
                if (data is not null) { _data = data; return; }
            }
        }
        catch { }
        _data = new ProfileLibraryData();
    }

    public void Save()
    {
        lock (_gate)
        {
            try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, JsonOptions)); }
            catch { }
        }
        Changed?.Invoke();
    }

    // =====================================================================================
    //  Consulta e edição
    // =====================================================================================

    public IReadOnlyList<GameProfile> Profiles
    {
        get { lock (_gate) return _data.Profiles.ToList(); }
    }

    /// <summary>Somente os presets (modelos), para o seletor "começar a partir de...".</summary>
    public IReadOnlyList<GameProfile> Presets
    {
        get { lock (_gate) return _data.Profiles.Where(p => p.IsBuiltInPreset).ToList(); }
    }

    /// <summary>Perfis criados pelo usuário para jogos específicos.</summary>
    public IReadOnlyList<GameProfile> UserProfiles
    {
        get { lock (_gate) return _data.Profiles.Where(p => !p.IsBuiltInPreset).ToList(); }
    }

    public GameProfile? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_gate) return _data.Profiles.FirstOrDefault(p => p.Id == id);
    }

    public void AddOrUpdate(GameProfile profile)
    {
        profile.UpdatedAt = DateTime.Now;
        lock (_gate)
        {
            int index = _data.Profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) _data.Profiles[index] = profile;
            else _data.Profiles.Add(profile);
        }
        Save();
    }

    public void Remove(string id)
    {
        lock (_gate) _data.Profiles.RemoveAll(p => p.Id == id);
        Save();
    }

    /// <summary>Salva um perfil já montado como um novo preset reutilizável.</summary>
    public GameProfile SaveAsPreset(GameProfile source, string name)
    {
        var preset = source.Clone();
        preset.Id = Guid.NewGuid().ToString("N");
        preset.Name = name;
        preset.IsBuiltInPreset = true;
        preset.Preset = ProfilePreset.Custom;
        AddOrUpdate(preset);
        return preset;
    }

    /// <summary>Cria, a partir de um preset, um perfil próprio para um jogo.</summary>
    public GameProfile CreateFromPreset(GameProfile preset, string gameName)
    {
        var profile = preset.Clone();
        profile.Id = Guid.NewGuid().ToString("N");
        profile.Name = gameName;
        profile.IsBuiltInPreset = false;
        AddOrUpdate(profile);
        return profile;
    }

    // =====================================================================================
    //  Presets de fábrica
    // =====================================================================================

    /// <summary>Cria os presets que ainda não existem (não recria os que o usuário apagou de propósito
    /// nem sobrescreve os que ele editou — a comparação é pelo tipo de preset).</summary>
    private void EnsurePresets()
    {
        bool added = false;
        lock (_gate)
        {
            // Só semeia na primeira execução: com a lista já populada, respeitamos as escolhas do
            // usuário (inclusive ter apagado um preset).
            if (_data.Profiles.Count > 0) return;

            foreach (var preset in BuildDefaultPresets())
            {
                _data.Profiles.Add(preset);
                added = true;
            }
        }
        if (added) Save();
    }

    /// <summary>Recria os presets de fábrica que estiverem faltando (botão "restaurar presets").</summary>
    public void RestoreDefaultPresets()
    {
        lock (_gate)
        {
            foreach (var preset in BuildDefaultPresets())
            {
                if (_data.Profiles.Any(p => p.IsBuiltInPreset && p.Preset == preset.Preset)) continue;
                _data.Profiles.Add(preset);
            }
        }
        Save();
    }

    private static IEnumerable<GameProfile> BuildDefaultPresets()
    {
        // ---- Padrão: não mexe em nada. Serve para jogos que só querem ser iniciados. ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetDefault"),
            Preset = ProfilePreset.Default,
            IsBuiltInPreset = true,
        };

        // ---- Competitivo: latência acima de tudo. ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetCompetitive"),
            Preset = ProfilePreset.Competitive,
            IsBuiltInPreset = true,
            Power = new PowerProfileSection
            {
                Mode = SettingMode.Custom,
                UseUltimatePerformance = true,
                CpuMinState = 100,
                CpuMaxState = 100,
                CpuBoostMode = 2,          // agressivo
                CoolingPolicy = 1,         // ativo (ventoinha antes de reduzir o clock)
                PciExpressLinkState = 0,   // sem economia de energia no barramento
                UsbSelectiveSuspend = 0,   // periféricos nunca "dormem" (mouse/teclado)
                SleepTimeoutMinutes = 0,
                DisplayTimeoutMinutes = 0,
            },
            Oem = new OemProfileSection { Mode = SettingMode.Auto, ModeId = "performance" },
            Cpu = new CpuProfileSection { Mode = SettingMode.Custom, Priority = ProcessPriority.High, ReserveFirstCore = false },
            Memory = new MemoryProfileSection { Mode = SettingMode.Custom, OptimizeBeforeLaunch = true },
            Latency = new LatencyProfileSection
            {
                Mode = SettingMode.Custom,
                TimerResolutionMs = 0.5,
                DisableUsbSelectiveSuspend = true,
                DisableWifiPowerSaving = true,
                LowLatencyProfile = true,
            },
            Network = new NetworkProfileSection { Mode = SettingMode.Custom, Kind = NetworkProfileKind.Competitive, FlushDnsBefore = true },
        };

        // ---- Máximo Desempenho: para jogos pesados de single-player. ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetMaxPerformance"),
            Preset = ProfilePreset.MaxPerformance,
            IsBuiltInPreset = true,
            Power = new PowerProfileSection
            {
                Mode = SettingMode.Custom,
                UseUltimatePerformance = true,
                CpuMinState = 100,
                CpuMaxState = 100,
                CpuBoostMode = 2,
                CoolingPolicy = 1,
                SleepTimeoutMinutes = 0,
                DisplayTimeoutMinutes = 0,
            },
            Oem = new OemProfileSection { Mode = SettingMode.Auto, ModeId = "turbo" },
            Cpu = new CpuProfileSection { Mode = SettingMode.Custom, Priority = ProcessPriority.AboveNormal },
            Memory = new MemoryProfileSection { Mode = SettingMode.Custom, OptimizeBeforeLaunch = true },
            Latency = new LatencyProfileSection { Mode = SettingMode.Custom, TimerResolutionMs = 0.5 },
        };

        // ---- Balanceado: o meio-termo do dia a dia. ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetBalanced"),
            Preset = ProfilePreset.Balanced,
            IsBuiltInPreset = true,
            Power = new PowerProfileSection
            {
                Mode = SettingMode.Custom,
                PlanGuid = PowerPlanService.BalancedGuid,
                PlanName = Localization.Loc.S("GH_PlanBalanced"),
                CpuMinState = 5,
                CpuMaxState = 100,
                CpuBoostMode = 1,
            },
            Oem = new OemProfileSection { Mode = SettingMode.Auto, ModeId = "balanced" },
            Memory = new MemoryProfileSection { Mode = SettingMode.Custom, OptimizeBeforeLaunch = true },
        };

        // ---- Silencioso: prioriza o barulho baixo (jogos leves, emuladores, vídeo). ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetSilent"),
            Preset = ProfilePreset.Silent,
            IsBuiltInPreset = true,
            Power = new PowerProfileSection
            {
                Mode = SettingMode.Custom,
                PlanGuid = PowerPlanService.BalancedGuid,
                PlanName = Localization.Loc.S("GH_PlanBalanced"),
                CpuMinState = 5,
                CpuMaxState = 80,          // segura o clock e, com isso, a ventoinha
                CpuBoostMode = 0,
                CoolingPolicy = 0,         // passivo: reduz o clock antes de acelerar a ventoinha
            },
            Oem = new OemProfileSection { Mode = SettingMode.Auto, ModeId = "quiet" },
        };

        // ---- Economia: para usar na bateria. ----
        yield return new GameProfile
        {
            Name = Localization.Loc.S("GH_PresetPowerSaving"),
            Preset = ProfilePreset.PowerSaving,
            IsBuiltInPreset = true,
            Power = new PowerProfileSection
            {
                Mode = SettingMode.Custom,
                PlanGuid = PowerPlanService.PowerSaverGuid,
                PlanName = Localization.Loc.S("GH_PlanPowerSaver"),
                CpuMinState = 5,
                CpuMaxState = 50,
                CpuBoostMode = 0,
                CoolingPolicy = 0,
            },
            Oem = new OemProfileSection { Mode = SettingMode.Auto, ModeId = "eco" },
            Display = new DisplayProfileSection { Mode = SettingMode.Custom, Brightness = 40 },
        };
    }
}
