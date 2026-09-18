using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.GameHub;

/// <summary>
/// Modo de cada seção do perfil, conforme a regra geral do GameHub: toda configuração pode ser
/// deixada como está, decidida pelo Pulse1x ou definida pelo usuário.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingMode
{
    /// <summary>Não alterar — o Pulse1x não toca nesta área.</summary>
    Unchanged,
    /// <summary>Automático — o Pulse1x escolhe um valor recomendado para o tipo de item.</summary>
    Auto,
    /// <summary>Personalizado — usa exatamente os valores definidos pelo usuário.</summary>
    Custom,
}

/// <summary>Presets prontos de perfil (editáveis; o usuário também pode salvar os seus).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfilePreset { Default, Competitive, MaxPerformance, Balanced, Silent, PowerSaving, Custom }

/// <summary>Prioridade do processo principal do jogo/app.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessPriority { Idle, BelowNormal, Normal, AboveNormal, High, Realtime }

/// <summary>Perfil de rede aplicado a partir da categoria Latência já existente.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkProfileKind { Competitive, Stability, AllSafe }

/// <summary>Cada etapa da sequência de inicialização — a ordem é definida pelo usuário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileStepKind
{
    SaveSnapshot,
    PowerPlan,
    PowerAdvanced,
    OemMode,
    Audio,
    Display,
    TimerResolution,
    LatencyTweaks,
    MemoryOptimize,
    NetworkProfile,
    CloseProcesses,
    RunCommandsBefore,
    StartApps,
    LaunchGame,
    ProcessTuning,
}

// =====================================================================================
//  Seções do perfil
// =====================================================================================

/// <summary>Base de toda seção: o modo (não alterar / automático / personalizado).</summary>
public abstract class ProfileSection
{
    public SettingMode Mode { get; set; } = SettingMode.Unchanged;

    [JsonIgnore] public bool IsActive => Mode != SettingMode.Unchanged;
}

/// <summary>
/// Valor de uma configuração avançada de plano de energia (o mesmo par subgrupo/configuração que o
/// powercfg usa). AcValue/DcValue são os índices na tomada e na bateria.
/// </summary>
public class PowerSettingValue
{
    public string SubGroupGuid { get; set; } = "";
    public string SettingGuid { get; set; } = "";
    /// <summary>Rótulo amigável, só para exibição/diagnóstico.</summary>
    public string Label { get; set; } = "";
    public int? AcValue { get; set; }
    public int? DcValue { get; set; }
}

/// <summary>Plano de energia do Windows + edição avançada (no espírito do Quick CPU).</summary>
public class PowerProfileSection : ProfileSection
{
    /// <summary>GUID do plano a ativar. Vazio com Auto = Alto Desempenho.</summary>
    public string? PlanGuid { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Cria/ativa o plano "Ultimate Performance" mesmo que ele não exista na máquina.</summary>
    public bool UseUltimatePerformance { get; set; }

    // ---- Atalhos para as configurações avançadas mais usadas (null = não alterar) ----
    public int? CpuMinState { get; set; }          // 0..100 (%)
    public int? CpuMaxState { get; set; }          // 0..100 (%)
    public int? CpuBoostMode { get; set; }         // 0=Desativado 1=Ativado 2=Agressivo 3=Eficiente ...
    public int? CoolingPolicy { get; set; }        // 0=Passivo 1=Ativo
    public int? PciExpressLinkState { get; set; }  // 0=Desligado 1=Moderado 2=Máximo
    public int? UsbSelectiveSuspend { get; set; }  // 0=Desabilitado 1=Habilitado
    public int? SleepTimeoutMinutes { get; set; }  // 0 = nunca
    public int? DisplayTimeoutMinutes { get; set; }
    public int? HardDiskTimeoutMinutes { get; set; }

    /// <summary>Quaisquer outras configurações avançadas escolhidas pelo usuário no editor.</summary>
    public List<PowerSettingValue> Extra { get; set; } = new();
}

/// <summary>Modo do software do fabricante (NitroSense, Armoury Crate, Vantage, MSI Center, AWCC).</summary>
public class OemProfileSection : ProfileSection
{
    /// <summary>Id do fornecedor detectado ("acer", "asus", "lenovo", "msi", "dell").</summary>
    public string? VendorId { get; set; }
    /// <summary>Id do modo dentro do fornecedor ("eco", "balanced", "performance", "turbo").</summary>
    public string? ModeId { get; set; }
}

/// <summary>Prioridade e afinidade do processo principal do jogo.</summary>
public class CpuProfileSection : ProfileSection
{
    public ProcessPriority Priority { get; set; } = ProcessPriority.Normal;

    /// <summary>Máscara de afinidade; 0 = automática (todos os núcleos).</summary>
    public long AffinityMask { get; set; }

    /// <summary>Reserva o núcleo 0 para o sistema (afinidade automática "sem o primeiro núcleo").</summary>
    public bool ReserveFirstCore { get; set; }
}

/// <summary>Otimização de RAM reaproveitando o módulo de memória já existente no Pulse1x.</summary>
public class MemoryProfileSection : ProfileSection
{
    public bool OptimizeBeforeLaunch { get; set; } = true;
    public bool OptimizeAfterExit { get; set; }
}

/// <summary>Ferramentas da categoria Latência aplicadas junto ao jogo.</summary>
public class LatencyProfileSection : ProfileSection
{
    /// <summary>Resolução do timer do sistema em ms (0.5 = mínimo usual). null = não alterar.</summary>
    public double? TimerResolutionMs { get; set; }
    public bool DisableUsbSelectiveSuspend { get; set; }
    public bool DisableWifiPowerSaving { get; set; }
    /// <summary>Aplica o conjunto de responsividade/agendador multimídia do perfil Competitivo.</summary>
    public bool LowLatencyProfile { get; set; }
}

/// <summary>Perfil de rede da categoria Latência.</summary>
public class NetworkProfileSection : ProfileSection
{
    public NetworkProfileKind Kind { get; set; } = NetworkProfileKind.Competitive;
    public bool FlushDnsBefore { get; set; }
}

/// <summary>Volume, dispositivos de áudio e mudo.</summary>
public class AudioProfileSection : ProfileSection
{
    public int? Volume { get; set; }              // 0..100
    public bool? Muted { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? OutputDeviceName { get; set; }
    public string? InputDeviceId { get; set; }
    public string? InputDeviceName { get; set; }
}

/// <summary>Brilho, taxa de atualização e HDR.</summary>
public class DisplayProfileSection : ProfileSection
{
    public int? Brightness { get; set; }          // 0..100
    public int? RefreshRate { get; set; }         // Hz
    public bool? Hdr { get; set; }
    /// <summary>Nome do dispositivo de vídeo alvo (null = monitor principal).</summary>
    public string? TargetDevice { get; set; }
}

/// <summary>Um app iniciado junto ao jogo.</summary>
public class LaunchItem
{
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool CloseOnExit { get; set; } = true;
    [JsonIgnore] public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>Processos fechados antes e aplicativos abertos junto ao jogo, com o caminho de volta.</summary>
public class AppsProfileSection : ProfileSection
{
    /// <summary>Nomes de processo (sem .exe) fechados antes de iniciar.</summary>
    public List<string> CloseProcesses { get; set; } = new();

    /// <summary>Reabre, ao sair, os processos que foram fechados (quando o caminho é conhecido).</summary>
    public bool ReopenClosedOnExit { get; set; } = true;

    public List<LaunchItem> StartApps { get; set; } = new();

    /// <summary>Comandos/scripts personalizados executados antes de iniciar o jogo.</summary>
    public List<string> CommandsBefore { get; set; } = new();
    /// <summary>Comandos/scripts personalizados executados ao finalizar.</summary>
    public List<string> CommandsAfter { get; set; } = new();
}

// =====================================================================================
//  O perfil
// =====================================================================================

/// <summary>
/// Perfil individual de um jogo ou aplicativo: como o computador deve se comportar antes, durante e
/// depois dele. Cada seção pode ser "não alterar", "automático" ou "personalizado", e a ordem das
/// etapas de inicialização é definida pelo usuário.
/// </summary>
public class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Preset de origem. Perfis marcados como IsBuiltInPreset aparecem como modelos e são
    /// copiados ao serem aplicados a um jogo (o modelo em si nunca é alterado).</summary>
    public ProfilePreset Preset { get; set; } = ProfilePreset.Custom;
    public bool IsBuiltInPreset { get; set; }

    public PowerProfileSection Power { get; set; } = new();
    public OemProfileSection Oem { get; set; } = new();
    public CpuProfileSection Cpu { get; set; } = new();
    public MemoryProfileSection Memory { get; set; } = new();
    public LatencyProfileSection Latency { get; set; } = new();
    public NetworkProfileSection Network { get; set; } = new();
    public AudioProfileSection Audio { get; set; } = new();
    public DisplayProfileSection Display { get; set; } = new();
    public AppsProfileSection Apps { get; set; } = new();

    /// <summary>Ordem das etapas na sequência de inicialização. Etapas ausentes usam a ordem padrão.</summary>
    public List<ProfileStepKind> StepOrder { get; set; } = new(DefaultStepOrder);

    /// <summary>Restaura tudo automaticamente quando o jogo fecha.</summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>Reduz a atividade do próprio Pulse1x enquanto o jogo estiver aberto.</summary>
    public bool GamingModeWhileRunning { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>Sequência padrão: salva o estado, ajusta o sistema, prepara o ambiente e só então inicia.</summary>
    public static readonly ProfileStepKind[] DefaultStepOrder =
    {
        ProfileStepKind.SaveSnapshot,
        ProfileStepKind.PowerPlan,
        ProfileStepKind.PowerAdvanced,
        ProfileStepKind.OemMode,
        ProfileStepKind.Audio,
        ProfileStepKind.Display,
        ProfileStepKind.TimerResolution,
        ProfileStepKind.LatencyTweaks,
        ProfileStepKind.MemoryOptimize,
        ProfileStepKind.NetworkProfile,
        ProfileStepKind.CloseProcesses,
        ProfileStepKind.RunCommandsBefore,
        ProfileStepKind.StartApps,
        ProfileStepKind.LaunchGame,
        ProfileStepKind.ProcessTuning,
    };

    /// <summary>Ordem efetiva: a do usuário, completada com as etapas que faltarem (e sem duplicatas).</summary>
    public IEnumerable<ProfileStepKind> EffectiveStepOrder()
    {
        var seen = new HashSet<ProfileStepKind>();
        foreach (var step in StepOrder)
            if (seen.Add(step)) yield return step;
        foreach (var step in DefaultStepOrder)
            if (seen.Add(step)) yield return step;
    }

    /// <summary>Cópia profunda (via JSON) — usada ao aplicar um preset a um jogo e ao editar sem salvar.</summary>
    public GameProfile Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<GameProfile>(
            System.Text.Json.JsonSerializer.Serialize(this))!;

    /// <summary>Resumo curto do que o perfil altera, exibido no cartão do jogo.</summary>
    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (Power.IsActive) parts.Add(Localization.Loc.S("GH_SecPower"));
            if (Oem.IsActive) parts.Add(Localization.Loc.S("GH_SecOem"));
            if (Cpu.IsActive) parts.Add("CPU");
            if (Memory.IsActive) parts.Add(Localization.Loc.S("GH_SecMemory"));
            if (Latency.IsActive) parts.Add(Localization.Loc.S("GH_SecLatency"));
            if (Network.IsActive) parts.Add(Localization.Loc.S("GH_SecNetwork"));
            if (Audio.IsActive) parts.Add(Localization.Loc.S("GH_SecAudio"));
            if (Display.IsActive) parts.Add(Localization.Loc.S("GH_SecDisplay"));
            if (Apps.IsActive) parts.Add(Localization.Loc.S("GH_SecApps"));
            return parts.Count == 0 ? Localization.Loc.S("GH_ProfileNoChanges") : string.Join(" · ", parts);
        }
    }
}

/// <summary>Raiz persistida dos perfis (gamehub-profiles.json).</summary>
public class ProfileLibraryData
{
    public List<GameProfile> Profiles { get; set; } = new();
    /// <summary>Perfil aplicado a itens sem perfil próprio (null = nenhum).</summary>
    public string? DefaultProfileId { get; set; }
}
