using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Services;

/// <summary>
/// Perfil de ajustes visuais aplicado pela otimização "Ajustes Visuais".
/// </summary>
public enum VisualProfile
{
    BestAppearance, // mantém todos os efeitos
    Balanced,       // o Windows decide (padrão)
    BestPerformance // desativa a maioria dos efeitos
}

/// <summary>
/// Descreve uma otimização avançada de forma declarativa. Cada otimização sabe:
/// verificar seu estado atual (<see cref="IsAppliedAsync"/>), aplicar-se
/// (<see cref="ApplyAsync"/>) e descrever-se. A reversão é genérica: o serviço desfaz as
/// alterações registradas no <see cref="OptimizationChangeLog"/>.
/// </summary>
public class AdvancedOptimization
{
    public required string Id { get; init; }
    public required string Icon { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }

    /// <summary>Aviso opcional exibido no card (ex.: ganho pequeno em SSDs).</summary>
    public string? Warning { get; init; }

    /// <summary>Verifica se a otimização já está ativa no sistema (estado real, não do log).</summary>
    public required Func<Task<bool>> IsAppliedAsync { get; init; }

    /// <summary>Aplica a otimização. Deve registrar todas as alterações no log.</summary>
    public required Func<Task> ApplyAsync { get; init; }

    /// <summary>Indica se a otimização é aplicável nesta máquina/versão do Windows.</summary>
    public Func<Task<bool>>? IsAvailableAsync { get; init; }

    /// <summary>Texto adicional de estado (ex.: espaço liberado, serviços afetados).</summary>
    public Func<Task<string?>>? StateDetailAsync { get; init; }
}

/// <summary>
/// Motor das Otimizações Avançadas. Reúne otimizações seguras e reversíveis do Windows
/// (privacidade, desempenho e aparência) usando apenas métodos oficialmente suportados:
/// Registro do Windows, Políticas Locais, serviços, tarefas agendadas e powercfg.
///
/// Toda alteração passa pelos helpers <c>SetDword</c>/<c>SetString</c>/<c>DisableTaskAsync</c>/etc.,
/// que gravam no <see cref="OptimizationChangeLog"/> o valor anterior — permitindo a reversão
/// individual ou total. Nenhum componente é removido; apenas configurações são alteradas.
/// </summary>
public class AdvancedOptimizationService
{
    private readonly OptimizationChangeLog _log;
    private readonly HardwareOptimizationService _hardware;
    public IReadOnlyList<AdvancedOptimization> Optimizations { get; private set; }

    /// <summary>Disparado quando o idioma muda e a lista de otimizações é reconstruída com os novos textos.</summary>
    public event Action? OptimizationsChanged;

    public OptimizationChangeLog ChangeLog => _log;

    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>Políticas de IA do Windows (Recall e afins), conforme a documentação da Microsoft.</summary>
    private const string WindowsAiPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI";

    public AdvancedOptimizationService(OptimizationChangeLog log)
    {
        _log = log;
        _hardware = new HardwareOptimizationService(log);
        Optimizations = BuildOptimizations();
        Loc.Instance.LanguageChanged += () =>
        {
            Optimizations = BuildOptimizations();
            OptimizationsChanged?.Invoke();
        };
    }

    // ===================== Definição das otimizações =====================

    private List<AdvancedOptimization> BuildOptimizations()
    {
        var list = new List<AdvancedOptimization>();

        // ---------- PRIVACIDADE ----------

        list.Add(new AdvancedOptimization
        {
            Id = "copilot",
            Icon = "🤖",
            Title = Loc.S("AdvOpt_CopilotTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_CopilotDesc"),
            IsAvailableAsync = () => Task.FromResult(Environment.OSVersion.Version.Build >= 22000),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") == 1),
            ApplyAsync = () =>
            {
                SetDword("copilot", RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                SetDword("copilot", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "cortana",
            Icon = "🎙️",
            Title = Loc.S("AdvOpt_CortanaTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_CortanaDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") == 0),
            ApplyAsync = () =>
            {
                SetDword("cortana", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "telemetry",
            Icon = "📡",
            Title = Loc.S("AdvOpt_TelemetryTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_TelemetryDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0),
            ApplyAsync = () =>
            {
                SetDword("telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                SetDword("telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "telemetry-tasks",
            Icon = "🗓️",
            Title = Loc.S("AdvOpt_TelemetryTasksTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_TelemetryTasksDesc"),
            StateDetailAsync = async () =>
            {
                var states = await GetExistingTaskStatesAsync(TelemetryTasks);
                if (states.Count == 0) return Loc.S("AdvOpt_NoKnownTasksOnThisWindows");
                return Loc.S("AdvOpt_TasksFound") + " " + string.Join(", ", states.Select(s =>
                    $"{Path.GetFileName(s.path)} ({(s.disabled ? Loc.S("AdvOpt_Disabled") : Loc.S("AdvOpt_Enabled"))})")) + ".";
            },
            // Algumas tarefas (ex.: Compatibility Appraiser) não existem em todas as versões do
            // Windows — por isso a verificação considera só as que existem nesta máquina, e a
            // otimização é "aplicada" quando todas elas (as existentes) estão desabilitadas.
            IsAvailableAsync = async () => (await GetExistingTaskStatesAsync(TelemetryTasks)).Count > 0,
            IsAppliedAsync = async () =>
            {
                var states = await GetExistingTaskStatesAsync(TelemetryTasks);
                return states.Count > 0 && states.All(s => s.disabled);
            },
            ApplyAsync = async () =>
            {
                foreach (var task in TelemetryTasks)
                    await DisableTaskAsync("telemetry-tasks", task);
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "nvidia-telemetry",
            Icon = "🟩",
            Title = Loc.S("AdvOpt_NvidiaTelemetryTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_NvidiaTelemetryDesc"),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("NvTelemetryContainer")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("NvTelemetryContainer") == 4),
            ApplyAsync = async () => await DisableServiceAsync("nvidia-telemetry", "NvTelemetryContainer"),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "suggestions",
            Icon = "💡",
            Title = Loc.S("AdvOpt_SuggestionsTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_SuggestionsDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled") == 0),
            ApplyAsync = () =>
            {
                const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                SetDword("suggestions", RegistryHive.CurrentUser, cdm, "SystemPaneSuggestionsEnabled", 0);
                SetDword("suggestions", RegistryHive.CurrentUser, cdm, "SilentInstalledAppsEnabled", 0);
                SetDword("suggestions", RegistryHive.CurrentUser, cdm, "SubscribedContent-338388Enabled", 0);
                SetDword("suggestions", RegistryHive.CurrentUser, cdm, "SubscribedContent-338389Enabled", 0);
                SetDword("suggestions", RegistryHive.CurrentUser, cdm, "SubscribedContent-353698Enabled", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "browser-telemetry",
            Icon = "🌐",
            Title = Loc.S("AdvOpt_BrowserTelemetryTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_BrowserTelemetryDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "MetricsReportingEnabled") == 0),
            ApplyAsync = () =>
            {
                SetDword("browser-telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "MetricsReportingEnabled", 0);
                SetDword("browser-telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled", 0);
                SetDword("browser-telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData", 0);
                SetDword("browser-telemetry", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "notifications",
            Icon = "🔔",
            Title = Loc.S("AdvOpt_NotificationsTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_NotificationsDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") == 0),
            ApplyAsync = () =>
            {
                SetDword("notifications", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0);
                return Task.CompletedTask;
            },
        });

        // O Recall é o recurso que mais assusta quem se preocupa com privacidade: ele grava o que
        // passa na tela. Por isso as duas políticas são aplicadas juntas — uma impede salvar novas
        // capturas, a outra remove o componente do sistema.
        list.Add(new AdvancedOptimization
        {
            Id = "recall",
            Icon = "📸",
            Title = Loc.S("AdvOpt_RecallTitle"),
            Category = "Privacidade",
            Warning = Loc.S("AdvOpt_RecallWarning"),
            Description = Loc.S("AdvOpt_RecallDesc"),
            // Só existe a partir do Windows 11 24H2 (build 26100).
            IsAvailableAsync = () => Task.FromResult(Environment.OSVersion.Version.Build >= 26100),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, WindowsAiPolicy, "DisableAIDataAnalysis") == 1),
            ApplyAsync = () =>
            {
                SetDword("recall", RegistryHive.LocalMachine, WindowsAiPolicy, "DisableAIDataAnalysis", 1);
                SetDword("recall", RegistryHive.CurrentUser, WindowsAiPolicy, "DisableAIDataAnalysis", 1);
                SetDword("recall", RegistryHive.LocalMachine, WindowsAiPolicy, "AllowRecallEnablement", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "advertising-id",
            Icon = "🎯",
            Title = Loc.S("AdvOpt_AdvertisingIdTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_AdvertisingIdDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") == 0),
            ApplyAsync = () =>
            {
                SetDword("advertising-id", RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, fallbackOldValue: 1);
                SetDword("advertising-id", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "tailored-experiences",
            Icon = "🧩",
            Title = Loc.S("AdvOpt_TailoredTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_TailoredDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled") == 0),
            ApplyAsync = () =>
            {
                SetDword("tailored-experiences", RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, fallbackOldValue: 1);
                SetDword("tailored-experiences", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "diagtrack-service",
            Icon = "📶",
            Title = Loc.S("AdvOpt_DiagTrackTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_DiagTrackDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(
                Loc.S("AdvOpt_AffectedServices") + " DiagTrack, dmwappushservice."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("DiagTrack")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("DiagTrack") == 4),
            ApplyAsync = async () =>
            {
                await DisableServiceAsync("diagtrack-service", "DiagTrack", defaultStartType: 2);
                if (ServiceExists("dmwappushservice"))
                    await DisableServiceAsync("diagtrack-service", "dmwappushservice", defaultStartType: 3);
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "start-web-search",
            Icon = "🔎",
            Title = Loc.S("AdvOpt_WebSearchTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_WebSearchDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch") == 1),
            ApplyAsync = () =>
            {
                SetDword("start-web-search", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1);
                SetDword("start-web-search", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
                SetDword("start-web-search", RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, fallbackOldValue: 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "lockscreen-ads",
            Icon = "🖼️",
            Title = Loc.S("AdvOpt_LockScreenAdsTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_LockScreenAdsDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled") == 0),
            ApplyAsync = () =>
            {
                const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "RotatingLockScreenOverlayEnabled", 0, fallbackOldValue: 1);
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "SubscribedContent-338387Enabled", 0, fallbackOldValue: 1);
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "SubscribedContent-310093Enabled", 0, fallbackOldValue: 1);
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "SubscribedContent-338393Enabled", 0, fallbackOldValue: 1);
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "SubscribedContent-353694Enabled", 0, fallbackOldValue: 1);
                SetDword("lockscreen-ads", RegistryHive.CurrentUser, cdm, "SubscribedContent-353696Enabled", 0, fallbackOldValue: 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "feedback-requests",
            Icon = "💬",
            Title = Loc.S("AdvOpt_FeedbackTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_FeedbackDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod") == 0),
            ApplyAsync = () =>
            {
                SetDword("feedback-requests", RegistryHive.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
                SetDword("feedback-requests", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "activity-history",
            Icon = "🕒",
            Title = Loc.S("AdvOpt_ActivityHistoryTitle"),
            Category = "Privacidade",
            Description = Loc.S("AdvOpt_ActivityHistoryDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities") == 0),
            ApplyAsync = () =>
            {
                const string system = @"SOFTWARE\Policies\Microsoft\Windows\System";
                SetDword("activity-history", RegistryHive.LocalMachine, system, "PublishUserActivities", 0, fallbackOldValue: 1);
                SetDword("activity-history", RegistryHive.LocalMachine, system, "UploadUserActivities", 0, fallbackOldValue: 1);
                SetDword("activity-history", RegistryHive.LocalMachine, system, "EnableActivityFeed", 0, fallbackOldValue: 1);
                return Task.CompletedTask;
            },
        });

        // ---------- DESEMPENHO ----------

        list.Add(new AdvancedOptimization
        {
            Id = "xbox-gamebar",
            Icon = "🎮",
            Title = Loc.S("AdvOpt_XboxGameBarTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_XboxGameBarDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") == 0),
            ApplyAsync = () =>
            {
                SetDword("xbox-gamebar", RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                SetDword("xbox-gamebar", RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                SetDword("xbox-gamebar", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "xbox-services",
            Icon = "🕹️",
            Title = Loc.S("AdvOpt_XboxServicesTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_XboxServicesDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " " + string.Join(", ", XboxServices) + "."),
            IsAvailableAsync = () => Task.FromResult(XboxServices.Any(ServiceExists)),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("XblAuthManager") == 4),
            ApplyAsync = async () =>
            {
                foreach (var svc in XboxServices)
                    if (ServiceExists(svc))
                        await DisableServiceAsync("xbox-services", svc);
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "fax-service",
            Icon = "📠",
            Title = Loc.S("AdvOpt_FaxServiceTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_FaxServiceDesc"),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("Fax")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("Fax") == 4),
            ApplyAsync = async () => await DisableServiceAsync("fax-service", "Fax"),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "windows-update-auto",
            Icon = "🔄",
            Title = Loc.S("AdvOpt_WuAutoTitle"),
            Category = "Desempenho",
            Warning = Loc.S("AdvOpt_WuAutoWarning"),
            Description = Loc.S("AdvOpt_WuAutoDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate") == 1),
            ApplyAsync = () =>
            {
                SetDword("windows-update-auto", RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "windows-search",
            Icon = "🔍",
            Title = Loc.S("AdvOpt_WindowsSearchTitle"),
            Category = "Desempenho",
            Warning = Loc.S("AdvOpt_WindowsSearchWarning"),
            Description = Loc.S("AdvOpt_WindowsSearchDesc"),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("WSearch")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("WSearch") == 4),
            ApplyAsync = async () => await DisableServiceAsync("windows-search", "WSearch", defaultStartType: 2), // Automático é o padrão de fábrica
        });

        list.Add(new AdvancedOptimization
        {
            Id = "sysmain",
            Icon = "🧩",
            Title = Loc.S("AdvOpt_SysMainTitle"),
            Category = "Desempenho",
            Warning = Loc.S("AdvOpt_SysMainWarning"),
            Description = Loc.S("AdvOpt_SysMainDesc"),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("SysMain")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("SysMain") == 4),
            ApplyAsync = async () => await DisableServiceAsync("sysmain", "SysMain", defaultStartType: 2), // Automático é o padrão de fábrica
        });

        // Serviços que quase nenhum PC doméstico usa. Cada um diz exatamente o que desliga e o que
        // deixa de funcionar — desativar às cegas é o que transforma "otimizar" em suporte técnico.
        list.Add(new AdvancedOptimization
        {
            Id = "print-spooler",
            Icon = "🖨️",
            Title = Loc.S("AdvOpt_SpoolerTitle"),
            Category = "Desempenho",
            Warning = Loc.S("AdvOpt_SpoolerWarning"),
            Description = Loc.S("AdvOpt_SpoolerDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " Spooler."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("Spooler")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("Spooler") == 4),
            ApplyAsync = async () => await DisableServiceAsync("print-spooler", "Spooler", defaultStartType: 2),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "remote-registry",
            Icon = "🔐",
            Title = Loc.S("AdvOpt_RemoteRegistryTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_RemoteRegistryDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " RemoteRegistry."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("RemoteRegistry")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("RemoteRegistry") == 4),
            ApplyAsync = async () => await DisableServiceAsync("remote-registry", "RemoteRegistry"),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "remote-access",
            Icon = "🌐",
            Title = Loc.S("AdvOpt_RemoteAccessTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_RemoteAccessDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(
                Loc.S("AdvOpt_AffectedServices") + " RemoteAccess, SessionEnv, TermService."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("RemoteAccess")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("RemoteAccess") == 4),
            ApplyAsync = async () =>
            {
                await DisableServiceAsync("remote-access", "RemoteAccess");
                foreach (var svc in new[] { "SessionEnv", "TermService" })
                    if (ServiceExists(svc)) await DisableServiceAsync("remote-access", svc, defaultStartType: 3);
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "retail-demo",
            Icon = "🏬",
            Title = Loc.S("AdvOpt_RetailDemoTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_RetailDemoDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " RetailDemo."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("RetailDemo")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("RetailDemo") == 4),
            ApplyAsync = async () => await DisableServiceAsync("retail-demo", "RetailDemo"),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "map-broker",
            Icon = "🗺️",
            Title = Loc.S("AdvOpt_MapBrokerTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_MapBrokerDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " MapsBroker."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("MapsBroker")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("MapsBroker") == 4),
            ApplyAsync = async () => await DisableServiceAsync("map-broker", "MapsBroker", defaultStartType: 2),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "biometrics",
            Icon = "👆",
            Title = Loc.S("AdvOpt_BiometricsTitle"),
            Category = "Desempenho",
            Warning = Loc.S("AdvOpt_BiometricsWarning"),
            Description = Loc.S("AdvOpt_BiometricsDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(Loc.S("AdvOpt_AffectedServices") + " WbioSrvc."),
            IsAvailableAsync = () => Task.FromResult(ServiceExists("WbioSrvc")),
            IsAppliedAsync = () => Task.FromResult(GetServiceStartType("WbioSrvc") == 4),
            ApplyAsync = async () => await DisableServiceAsync("biometrics", "WbioSrvc"),
        });

        list.Add(new AdvancedOptimization
        {
            Id = "delivery-optimization",
            Icon = "📦",
            Title = Loc.S("AdvOpt_DeliveryOptTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_DeliveryOptDesc"),
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode") == 0),
            ApplyAsync = () =>
            {
                SetDword("delivery-optimization", RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "hibernation",
            Icon = "🛌",
            Title = Loc.S("AdvOpt_HibernationTitle"),
            Category = "Desempenho",
            Description = Loc.S("AdvOpt_HibernationDesc"),
            StateDetailAsync = () =>
            {
                double gb = GetHiberfilSizeGb();
                return Task.FromResult<string?>(gb > 0 ? Loc.F("AdvOpt_HiberfilWillBeFreed", $"{gb:0.0}") : null);
            },
            IsAppliedAsync = () => Task.FromResult(
                GetDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0),
            ApplyAsync = async () =>
            {
                _log.Record(new OptimizationChange
                {
                    OptimizationId = "hibernation",
                    OptimizationTitle = Loc.S("AdvOpt_HibernationTitle"),
                    Kind = ChangeKind.PowerCfg,
                    KeyPath = Loc.S("AdvOpt_HibernationKeyPath"),
                    ValueName = "Hibernate",
                    OldValue = "on",
                    NewValue = "off",
                });
                await RunAsync("powercfg", "/hibernate off");
            },
        });

        AddHardwareOptimizations(list);

        return list;
    }

    // ---------- HARDWARE (CPU, GPU e RAM) ----------

    /// <summary>
    /// A categoria Hardware ajusta como o Windows distribui trabalho à CPU e à GPU. Nenhuma delas
    /// faz overclock, mexe em voltagem ou em limite térmico: o que muda é a política do sistema
    /// operacional, e toda mudança volta atrás pelo mesmo log das demais otimizações.
    /// </summary>
    private void AddHardwareOptimizations(List<AdvancedOptimization> list)
    {
        list.Add(new AdvancedOptimization
        {
            Id = "hw-gpu-scheduling",
            Icon = "🎬",
            Title = Loc.S("AdvOpt_HagsTitle"),
            Category = "Hardware",
            Warning = Loc.S("AdvOpt_RequiresRestart"),
            Description = Loc.S("AdvOpt_HagsDesc"),
            IsAvailableAsync = () => Task.FromResult(_hardware.IsHagsAvailable()),
            IsAppliedAsync = () => Task.FromResult(_hardware.IsHagsEnabled()),
            ApplyAsync = () =>
            {
                _hardware.EnableHags();
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "core-parking",
            Icon = "🧠",
            Title = Loc.S("AdvOpt_CoreParkingTitle"),
            Category = "Hardware",
            Description = Loc.S("AdvOpt_CoreParkingDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(
                Loc.F("AdvOpt_CoreParkingDetail", Environment.ProcessorCount.ToString())),
            IsAppliedAsync = () => _hardware.IsCoreParkingDisabledAsync(),
            ApplyAsync = async () =>
            {
                await _hardware.UnhideCoreParkingAsync();
                await _hardware.DisableCoreParkingAsync();
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "power-throttling",
            Icon = "⚡",
            Title = Loc.S("AdvOpt_PowerThrottlingTitle"),
            Category = "Hardware",
            Warning = Loc.S("AdvOpt_PowerThrottlingWarning"),
            Description = Loc.S("AdvOpt_PowerThrottlingDesc"),
            IsAppliedAsync = () => Task.FromResult(_hardware.IsPowerThrottlingDisabled()),
            ApplyAsync = () =>
            {
                _hardware.DisablePowerThrottling();
                return Task.CompletedTask;
            },
        });

        // O MSI depende do dispositivo: só aparece quando existe uma GPU física cujo driver já
        // publica a chave de propriedades de interrupção.
        var gpu = _hardware.FindPrimaryGpu();
        list.Add(new AdvancedOptimization
        {
            Id = "gpu-msi",
            Icon = "🖥️",
            Title = Loc.S("AdvOpt_GpuMsiTitle"),
            Category = "Hardware",
            Warning = Loc.S("AdvOpt_RequiresRestart"),
            Description = Loc.S("AdvOpt_GpuMsiDesc"),
            StateDetailAsync = () => Task.FromResult<string?>(gpu is null ? null : Loc.F("AdvOpt_GpuDetected", gpu.Name)),
            IsAvailableAsync = () => Task.FromResult(gpu is not null && _hardware.IsMsiAvailable(gpu)),
            IsAppliedAsync = () => Task.FromResult(gpu is not null && _hardware.IsMsiEnabled(gpu)),
            ApplyAsync = () =>
            {
                if (gpu is not null) _hardware.EnableMsi(gpu);
                return Task.CompletedTask;
            },
        });

        list.Add(new AdvancedOptimization
        {
            Id = "game-priority",
            Icon = "🎯",
            Title = Loc.S("AdvOpt_GamePriorityTitle"),
            Category = "Hardware",
            Description = Loc.S("AdvOpt_GamePriorityDesc"),
            IsAppliedAsync = () => Task.FromResult(_hardware.IsGamePriorityApplied()),
            ApplyAsync = () =>
            {
                _hardware.ApplyGamePriority();
                return Task.CompletedTask;
            },
        });
    }

    private static readonly string[] XboxServices = { "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" };

    private static readonly string[] TelemetryTasks =
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
    };

    // ===================== Ajustes Visuais (perfis) =====================

    public bool IsVisualProfileApplied(VisualProfile profile)
    {
        int? current = GetDword(RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
        return current == VisualFxValue(profile);
    }

    public void ApplyVisualProfile(VisualProfile profile)
    {
        SetDword("visual-profile", RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", VisualFxValue(profile));
    }

    private static int VisualFxValue(VisualProfile p) => p switch
    {
        VisualProfile.BestAppearance => 1,
        VisualProfile.BestPerformance => 2,
        _ => 0, // Balanced / "deixar o Windows decidir"
    };

    // ===================== Reversão genérica =====================

    /// <summary>Desfaz todas as alterações ativas de uma otimização, restaurando os valores originais.</summary>
    public async Task RevertOptimizationAsync(string optimizationId)
    {
        // Desfaz na ordem inversa à de aplicação (restauração exata pelo log).
        foreach (var change in _log.GetActive(optimizationId).Reverse())
            await RevertChangeAsync(change);

        // Rede de segurança: se a otimização AINDA aparece como aplicada, significa que o estado
        // veio de fora do Pulse1x (padrão do Windows, política de grupo ou outro programa) e o log
        // não tinha o que desfazer. Sem isto o interruptor "voltava sozinho" para ON. Restauramos
        // então o padrão do Windows para essas chaves/serviços — operação igualmente reversível
        // (religar pelo interruptor regrava os valores e registra no log).
        var opt = Optimizations.FirstOrDefault(o => o.Id == optimizationId);
        if (opt is not null)
        {
            bool stillApplied;
            try { stillApplied = await opt.IsAppliedAsync(); }
            catch { stillApplied = false; }

            if (stillApplied)
                await RestoreOptimizationDefaultAsync(optimizationId);
        }
    }

    // Restaura o padrão do Windows para uma otimização cujo estado "ligado" não foi aplicado pelo
    // Pulse1x (logo, não há registro no log para desfazer). Usa apenas os mesmos alvos do ApplyAsync,
    // no sentido inverso — limpando políticas (volta ao comportamento padrão) ou devolvendo o serviço
    // ao tipo de inicialização de fábrica. Não grava no log: o ApplyAsync registra de novo se religado.
    private async Task RestoreOptimizationDefaultAsync(string id)
    {
        switch (id)
        {
            case "copilot":
                DeleteValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
                SetDwordRaw(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 1);
                break;
            case "cortana":
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana");
                break;
            case "telemetry":
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry");
                break;
            case "telemetry-tasks":
                foreach (var task in TelemetryTasks)
                    await EnableTaskAsync(task);
                break;
            case "nvidia-telemetry":
                await RestoreServiceDefaultAsync("NvTelemetryContainer", 2); // padrão de fábrica: Automático
                break;
            case "suggestions":
                {
                    const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    foreach (var v in new[]
                    {
                        "SystemPaneSuggestionsEnabled", "SilentInstalledAppsEnabled",
                        "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353698Enabled",
                    })
                        SetDwordRaw(RegistryHive.CurrentUser, cdm, v, 1); // padrão: sugestões habilitadas
                }
                break;
            case "browser-telemetry":
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "MetricsReportingEnabled");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "PersonalizationReportingEnabled");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "DiagnosticData");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled");
                break;
            case "notifications":
                DeleteValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled");
                break;
            case "xbox-gamebar":
                SetDwordRaw(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                SetDwordRaw(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                break;
            case "xbox-services":
                foreach (var svc in XboxServices)
                    await RestoreServiceDefaultAsync(svc, 3); // padrão de fábrica: Manual
                break;
            case "fax-service":
                await RestoreServiceDefaultAsync("Fax", 3); // padrão de fábrica: Manual
                break;
            case "windows-update-auto":
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
                break;
            case "windows-search":
                await RestoreServiceDefaultAsync("WSearch", 2); // padrão de fábrica: Automático
                break;
            case "sysmain":
                await RestoreServiceDefaultAsync("SysMain", 2); // padrão de fábrica: Automático
                break;
            case "hibernation":
                await RunAsync("powercfg", "/hibernate on");
                break;

            // ---- Privacidade (novas) ----
            case "recall":
                DeleteValue(RegistryHive.LocalMachine, WindowsAiPolicy, "DisableAIDataAnalysis");
                DeleteValue(RegistryHive.CurrentUser, WindowsAiPolicy, "DisableAIDataAnalysis");
                DeleteValue(RegistryHive.LocalMachine, WindowsAiPolicy, "AllowRecallEnablement");
                break;
            case "advertising-id":
                SetDwordRaw(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1);
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy");
                break;
            case "tailored-experiences":
                SetDwordRaw(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy",
                    "TailoredExperiencesWithDiagnosticDataEnabled", 1);
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent",
                    "DisableTailoredExperiencesWithDiagnosticData");
                break;
            case "diagtrack-service":
                await RestoreServiceDefaultAsync("DiagTrack", 2);
                await RestoreServiceDefaultAsync("dmwappushservice", 3);
                break;
            case "start-web-search":
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb");
                SetDwordRaw(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1);
                break;
            case "lockscreen-ads":
                {
                    const string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    foreach (var v in new[]
                    {
                        "RotatingLockScreenOverlayEnabled", "SubscribedContent-338387Enabled",
                        "SubscribedContent-310093Enabled", "SubscribedContent-338393Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled",
                    })
                        SetDwordRaw(RegistryHive.CurrentUser, cdm, v, 1);
                }
                break;
            case "feedback-requests":
                DeleteValue(RegistryHive.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod");
                DeleteValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "DoNotShowFeedbackNotifications");
                break;
            case "activity-history":
                {
                    const string system = @"SOFTWARE\Policies\Microsoft\Windows\System";
                    foreach (var v in new[] { "PublishUserActivities", "UploadUserActivities", "EnableActivityFeed" })
                        SetDwordRaw(RegistryHive.LocalMachine, system, v, 1);
                }
                break;

            // ---- Serviços (novos) ----
            case "print-spooler":
                await RestoreServiceDefaultAsync("Spooler", 2);
                break;
            case "remote-registry":
                await RestoreServiceDefaultAsync("RemoteRegistry", 3);
                break;
            case "remote-access":
                await RestoreServiceDefaultAsync("RemoteAccess", 3);
                await RestoreServiceDefaultAsync("SessionEnv", 3);
                await RestoreServiceDefaultAsync("TermService", 3);
                break;
            case "retail-demo":
                await RestoreServiceDefaultAsync("RetailDemo", 3);
                break;
            case "map-broker":
                await RestoreServiceDefaultAsync("MapsBroker", 2);
                break;
            case "biometrics":
                await RestoreServiceDefaultAsync("WbioSrvc", 3);
                break;
            case "delivery-optimization":
                DeleteValue(RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
                break;
            case "hw-gpu-scheduling":
            case "core-parking":
            case "power-throttling":
            case "gpu-msi":
            case "game-priority":
                await _hardware.RestoreDefaultAsync(id);
                break;
        }
    }

    /// <summary>Desfaz uma única alteração registrada.</summary>
    public async Task RevertChangeAsync(OptimizationChange change)
    {
        switch (change.Kind)
        {
            case ChangeKind.Registry:
                RevertRegistry(change);
                // Se a alteração revertida for o "Start" de um serviço, restaurar o valor no
                // Registro não basta: o serviço só voltaria a rodar no próximo boot. Iniciamos
                // explicitamente para que o toggle reflita o estado real imediatamente.
                if (change.ValueName == "Start" && change.KeyPath.StartsWith(ServicesPath, StringComparison.OrdinalIgnoreCase)
                    && change.OldValue != "4" && change.OldValue != "3")
                {
                    var serviceName = change.KeyPath[(ServicesPath.Length + 1)..];
                    await RunAsync("sc", $"start {serviceName}");
                }
                break;
            case ChangeKind.Task:
                {
                    var result = await RunAsync("schtasks", $"/Change /TN \"{change.KeyPath}\" /Enable");
                    // O comando pode falhar silenciosamente (ex.: "Acesso negado" quando o app não
                    // está elevado) — sem checar o código de saída e o estado real, o log marcaria
                    // a tarefa como revertida mesmo continuando desabilitada, e o interruptor
                    // voltaria a "ativado" sozinho na próxima verificação de estado.
                    if (result.code != 0 || await GetTaskStateAsync(change.KeyPath) == "disabled")
                        throw new InvalidOperationException(
                            Loc.F("AdvOpt_CouldNotReenableTask", Path.GetFileName(change.KeyPath)));
                }
                break;
            case ChangeKind.PowerCfg:
                if (change.ValueName == "Hibernate")
                    await RunAsync("powercfg", "/hibernate on");
                else if (change.ValueName == "PowerPlan" && !string.IsNullOrEmpty(change.OldValue))
                    await RunAsync("powercfg", $"/setactive {change.OldValue}");
                else if (change.ValueName == "ValueIndex" && !string.IsNullOrEmpty(change.OldValue))
                {
                    // KeyPath guarda "SUBGRUPO CONFIGURAÇÃO"; OldValue, os índices originais de
                    // tomada e bateria ("ac|dc"), que é como CaptureIndex os gravou.
                    var guids = change.KeyPath.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var values = change.OldValue.Split('|');
                    if (guids.Length == 2 && values.Length == 2)
                    {
                        await RunAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT {guids[0]} {guids[1]} {values[0]}");
                        await RunAsync("powercfg", $"/setdcvalueindex SCHEME_CURRENT {guids[0]} {guids[1]} {values[1]}");
                        await RunAsync("powercfg", "/setactive SCHEME_CURRENT");
                    }
                }
                break;
        }

        _log.MarkReverted(change);
    }

    private void RevertRegistry(OptimizationChange c)
    {
        var root = c.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

        if (c.OldValue is null)
        {
            // O valor não existia antes: removê-lo restaura o estado original.
            using var key = root.OpenSubKey(c.KeyPath, writable: true);
            if (key?.GetValue(c.ValueName) is not null)
                key.DeleteValue(c.ValueName, throwOnMissingValue: false);
            return;
        }

        using var wk = root.CreateSubKey(c.KeyPath);
        if (c.ValueKind == "DWord" && int.TryParse(c.OldValue, out int dword))
            wk.SetValue(c.ValueName, dword, RegistryValueKind.DWord);
        else
            wk.SetValue(c.ValueName, c.OldValue, RegistryValueKind.String);
    }

    // ===================== Helpers de Registro =====================

    // Quando o valor já está no estado desejado (ex.: um serviço que o Windows já traz
    // desativado/manual por padrão em certas edições), nada precisa ser escrito no Registro —
    // mas se ninguém registrar uma alteração, não há nada para a reversão desfazer: o
    // interruptor mostraria "desativado" e, na próxima verificação de estado real, voltaria
    // a "ativado" sozinho. Por isso, quando há um valor de fallback, ainda assim registramos
    // a alteração (usando o fallback como "valor original") para que reverter sempre funcione.
    private void SetDword(string optId, RegistryHive hive, string subKey, string name, int value, int? fallbackOldValue = null)
    {
        int? old = GetDword(hive, subKey, name);
        bool alreadyAtTarget = old == value;
        if (alreadyAtTarget && fallbackOldValue is null) return;

        var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
        using (var wk = root.CreateSubKey(subKey))
            wk.SetValue(name, value, RegistryValueKind.DWord);

        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = TitleFor(optId),
            Kind = ChangeKind.Registry,
            Hive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
            KeyPath = subKey,
            ValueName = name,
            ValueKind = "DWord",
            OldValue = (alreadyAtTarget ? fallbackOldValue : old)?.ToString(),
            NewValue = value.ToString(),
        });
    }

    private int? GetDword(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(name) is int i ? i : (int?)null;
        }
        catch { return null; }
    }

    // Grava um DWORD diretamente, sem registrar no log — usado na restauração ao padrão do Windows,
    // que apenas devolve uma chave de política ao seu valor de fábrica (o ApplyAsync volta a registrar
    // se o usuário religar a otimização).
    private void SetDwordRaw(RegistryHive hive, string subKey, string name, int value)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var wk = root.CreateSubKey(subKey);
            wk.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { /* sem permissão: o estado real relido depois refletirá a falha no interruptor */ }
    }

    // Remove um valor (restaura o padrão do Windows, que é a ausência da política).
    private void DeleteValue(RegistryHive hive, string subKey, string name)
    {
        try
        {
            var root = hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey, writable: true);
            if (key?.GetValue(name) is not null)
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    private string TitleFor(string optId) =>
        Optimizations?.FirstOrDefault(o => o.Id == optId)?.Title
        ?? optId switch { "visual-profile" => Loc.S("AdvOpt_VisualAdjustments"), _ => optId };

    // ===================== Helpers de Serviços =====================

    private bool ServiceExists(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesPath}\{name}");
            return key is not null;
        }
        catch { return false; }
    }

    private int? GetServiceStartType(string name) =>
        GetDword(RegistryHive.LocalMachine, $@"{ServicesPath}\{name}", "Start");

    // Desativa um serviço gravando Start=4 no Registro (alteração reversível pelo log) e o interrompe.
    // defaultStartType é usado como "valor original" quando o serviço já estava desativado por
    // padrão (algumas edições do Windows trazem Fax/Xbox/etc. já em Manual ou Desativado) — sem
    // isso a reversão não teria nada para restaurar e o interruptor voltaria a "ativado" sozinho.
    private async Task DisableServiceAsync(string optId, string name, int defaultStartType = 3)
    {
        SetDword(optId, RegistryHive.LocalMachine, $@"{ServicesPath}\{name}", "Start", 4, fallbackOldValue: defaultStartType);
        await RunAsync("sc", $"stop {name}"); // melhor esforço; ignora falha se já parado
    }

    // Devolve um serviço ao tipo de inicialização de fábrica (sem log) — usado na restauração ao
    // padrão quando o serviço foi desativado fora do Pulse1x. Serviços automáticos são religados
    // para que o interruptor reflita o estado real imediatamente.
    private async Task RestoreServiceDefaultAsync(string name, int defaultStartType)
    {
        if (!ServiceExists(name)) return;
        SetDwordRaw(RegistryHive.LocalMachine, $@"{ServicesPath}\{name}", "Start", defaultStartType);
        if (defaultStartType == 2) // Automático: religa agora (Manual sobe sob demanda)
            await RunAsync("sc", $"start {name}");
    }

    // ===================== Helpers de Tarefas Agendadas =====================

    private async Task DisableTaskAsync(string optId, string taskPath)
    {
        string state = await GetTaskStateAsync(taskPath);
        if (state == "missing") return; // não existe nesta máquina: nada a fazer

        if (state == "enabled")
        {
            var result = await RunAsync("schtasks", $"/Change /TN \"{taskPath}\" /Disable");
            if (result.code != 0) return;
        }

        // Mesmo quando a tarefa já estava desabilitada (por padrão do Windows ou por outra
        // ferramenta), registramos a alteração: sem isso a reversão não teria nada para
        // restaurar e, como a otimização só conta como "desativada" quando TODAS as tarefas
        // existentes estão desabilitadas, essa única tarefa não revertida faria o interruptor
        // voltar a "ativado" sozinho.
        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = TitleFor(optId),
            Kind = ChangeKind.Task,
            KeyPath = taskPath,
            OldValue = Loc.S("AdvOpt_Enabled"),
            NewValue = Loc.S("AdvOpt_Disabled"),
        });
    }

    // Reabilita uma tarefa agendada (padrão do Windows) — usado na restauração quando a tarefa foi
    // desabilitada fora do Pulse1x e não há registro no log para reativar.
    private async Task EnableTaskAsync(string taskPath)
    {
        if (await GetTaskStateAsync(taskPath) == "missing") return;
        await RunAsync("schtasks", $"/Change /TN \"{taskPath}\" /Enable");
    }

    private async Task<bool> IsTaskDisabledAsync(string taskPath) =>
        await GetTaskStateAsync(taskPath) == "disabled";

    // Consulta só as tarefas que de fato existem nesta máquina (algumas variam por versão do
    // Windows) e retorna seu estado — usado para decidir corretamente se a otimização está
    // aplicada, sem depender de uma única tarefa que pode não existir em todo sistema.
    private async Task<List<(string path, bool disabled)>> GetExistingTaskStatesAsync(IEnumerable<string> taskPaths)
    {
        var result = new List<(string path, bool disabled)>();
        foreach (var path in taskPaths)
        {
            string state = await GetTaskStateAsync(path);
            if (state != "missing")
                result.Add((path, state == "disabled"));
        }
        return result;
    }

    private async Task<string> GetTaskStateAsync(string taskPath)
    {
        var result = await RunAsync("schtasks", $"/Query /TN \"{taskPath}\" /FO LIST");
        if (result.code != 0) return "missing";

        foreach (var line in result.output.Split('\n'))
        {
            if (line.TrimStart().StartsWith("Status", StringComparison.OrdinalIgnoreCase) ||
                line.TrimStart().StartsWith("Estado", StringComparison.OrdinalIgnoreCase))
            {
                string v = line.Split(':', 2).Last().Trim();
                if (v.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase) ||
                    v.StartsWith("Desabilitad", StringComparison.OrdinalIgnoreCase))
                    return "disabled";
                return "enabled";
            }
        }
        return "enabled";
    }

    // ===================== Util =====================

    private static double GetHiberfilSizeGb()
    {
        try
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System); // C:\Windows\System32
            string root = Path.GetPathRoot(sys) ?? "C:\\";
            string hiberfil = Path.Combine(root, "hiberfil.sys");
            if (File.Exists(hiberfil))
                return new FileInfo(hiberfil).Length / 1073741824d;
        }
        catch { }
        return 0;
    }

    private static async Task<(int code, string output)> RunAsync(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, sb.ToString());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
