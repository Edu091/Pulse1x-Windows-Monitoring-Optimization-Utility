using System.Diagnostics;
using System.Windows.Threading;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>Resultado da aplicação de um alvo — alimenta o painel de status e o log.</summary>
public readonly record struct ApplyOutcome(string Target, bool Applied, UnavailableReason Reason, string? Error)
{
    public static ApplyOutcome Ok(string target) => new(target, true, UnavailableReason.None, null);
    public static ApplyOutcome Skipped(string target, UnavailableReason reason) => new(target, false, reason, null);
    public static ApplyOutcome Failed(string target, string error) => new(target, false, UnavailableReason.None, error);
}

/// <summary>Estado geral do subsistema, exibido na seção.</summary>
public enum CustomizationState
{
    /// <summary>Desligada pelo usuário.</summary>
    Stopped,
    /// <summary>Ligada e com um tema aplicado.</summary>
    Active,
    /// <summary>Ligada, mas pausada temporariamente.</summary>
    Paused,
    /// <summary>Incompatível com esta versão do Windows.</summary>
    Unsupported,
}

/// <summary>
/// O motor da Personalização do Windows: decide o que pode ser aplicado, aplica, observa o
/// Explorer e sabe desfazer tudo.
///
/// Princípio de projeto: <b>nada aqui é irreversível</b>. Toda a personalização sem injeção é
/// feita por atributos de composição no HWND, que existem apenas em memória — reiniciar o
/// Explorer já devolve o Windows ao padrão, e <see cref="RestoreAll"/> faz isso na hora, sem
/// depender de nenhum arquivo do sistema ter sido modificado. Nenhum binário do Windows é
/// tocado, nenhum recurso é substituído em disco.
/// </summary>
public class WinCustomizationEngine
{
    private readonly WinThemeStore _store;
    private readonly CapabilityMatrix _capabilities;
    private readonly CustomizationWatchdog _watchdog;
    private readonly Func<WinCustomSettings> _settings;
    private readonly Action _persist;

    /// <summary>Observa o Explorer para reaplicar quando ele reinicia.</summary>
    private DispatcherTimer? _explorerWatch;
    private int _lastExplorerPid;

    /// <summary>Disparado quando o estado muda, para a interface se atualizar.</summary>
    public event Action? StateChanged;

    /// <summary>Disparado a cada aplicação, com o que foi (ou não) aplicado.</summary>
    public event Action<IReadOnlyList<ApplyOutcome>>? Applied;

    public WinCustomizationEngine(
        WinThemeStore store,
        CapabilityMatrix capabilities,
        CustomizationWatchdog watchdog,
        Func<WinCustomSettings> settings,
        Action persist)
    {
        _store = store;
        _capabilities = capabilities;
        _watchdog = watchdog;
        _settings = settings;
        _persist = persist;
    }

    public CapabilityMatrix Capabilities => _capabilities;

    /// <summary>
    /// Barras de tarefas existentes e em que monitor estão — alimenta o seletor de monitores da
    /// seção. É consultado ao vivo, porque monitores podem ser conectados a qualquer momento.
    /// </summary>
    public IReadOnlyList<WindowComposition.TaskbarOnMonitor> Taskbars() =>
        WindowComposition.FindTaskbarsWithMonitors();

    /// <summary>
    /// Se ALGUMA barra de tarefas selecionada está oculta pela ocultação automática do Windows.
    /// Nesse caso o efeito é aplicado mas fica invisível, e a seção precisa dizer isso — do
    /// contrário parece que a personalização simplesmente não funcionou.
    /// </summary>
    public bool AnySelectedTaskbarHidden()
    {
        var settings = _settings();
        return WindowComposition.FindTaskbarsWithMonitors()
            .Where(t => settings.IncludesMonitor(t.DeviceName))
            .Any(t => WindowComposition.IsTaskbarHidden(t.Hwnd));
    }
    public CustomizationWatchdog Watchdog => _watchdog;
    public WinThemeStore Store => _store;

    /// <summary>Tema em vigor, ou null se nenhum foi escolhido.</summary>
    public WinTheme? ActiveTheme => _store.FindById(_settings().ActiveThemeId);

    /// <summary>Pausa temporária — não mexe nas preferências, só suspende a aplicação.</summary>
    public bool IsPaused { get; private set; }

    public CustomizationState State
    {
        get
        {
            if (!_capabilities.IsWindows11) return CustomizationState.Unsupported;
            if (!_settings().Enabled) return CustomizationState.Stopped;
            if (IsPaused) return CustomizationState.Paused;
            return ActiveTheme is null ? CustomizationState.Stopped : CustomizationState.Active;
        }
    }

    /// <summary>Último resultado de aplicação, para a tela de status.</summary>
    public IReadOnlyList<ApplyOutcome> LastOutcomes { get; private set; } = Array.Empty<ApplyOutcome>();

    // =====================================================================================
    //  Aplicação
    // =====================================================================================

    /// <summary>
    /// Aplica o tema ativo a todos os alvos possíveis. Cada alvo é tratado de forma
    /// independente: uma incompatibilidade (ou uma quarentena) em um deles nunca impede os
    /// demais de funcionarem — requisito explícito de compatibilidade.
    /// </summary>
    public IReadOnlyList<ApplyOutcome> ApplyActiveTheme()
    {
        var outcomes = new List<ApplyOutcome>();
        var settings = _settings();

        if (!_capabilities.IsWindows11)
        {
            LastOutcomes = new[] { ApplyOutcome.Skipped("Windows", UnavailableReason.UnsupportedBuild) };
            StateChanged?.Invoke();
            return LastOutcomes;
        }

        var theme = ActiveTheme;
        if (theme is null || !settings.Enabled || IsPaused)
        {
            LastOutcomes = outcomes;
            StateChanged?.Invoke();
            return outcomes;
        }

        _capabilities.InjectionActive = settings.EnableInjection && InjectionHost.IsAvailable;

        outcomes.Add(ApplyTaskbar(theme));
        outcomes.Add(ApplyExplorerWindows(theme));

        // Menu Iniciar, Configurações e as regiões internas do Explorer dependem do motor de
        // injeção. Quando ele está fora, registramos o motivo em vez de tentar à força.
        outcomes.AddRange(ApplyInjectionTargets(theme, settings));

        LastOutcomes = outcomes;
        Applied?.Invoke(outcomes);
        StateChanged?.Invoke();
        return outcomes;
    }

    private ApplyOutcome ApplyTaskbar(WinTheme theme)
    {
        const string target = "Taskbar";

        var appearance = WinTheme.Get(theme.Components, WinComponent.Taskbar);
        if (appearance.Kind == AppearanceKind.WindowsDefault)
        {
            // "Padrão" não é uma falha: limpamos o efeito e saímos sem contar tentativa.
            // A limpeza ignora o filtro de monitores de propósito — voltar ao padrão tem de
            // alcançar TODAS as barras, inclusive as que deixaram de estar selecionadas.
            foreach (var hwnd in WindowComposition.FindAllTaskbars())
                WindowComposition.Reset(hwnd);
            return ApplyOutcome.Ok(target);
        }

        var capability = _capabilities.ForTaskbar(appearance.Kind);
        if (!capability.Available) return ApplyOutcome.Skipped(target, capability.Reason);

        if (!_watchdog.BeginAttempt(target))
            return ApplyOutcome.Failed(target, "quarantined");

        var all = WindowComposition.FindTaskbarsWithMonitors();
        if (all.Count == 0)
        {
            _watchdog.RecordFailure(target, "taskbar not found");
            return ApplyOutcome.Failed(target, "taskbar not found");
        }

        var settings = _settings();

        bool any = false;
        foreach (var taskbar in all)
        {
            if (settings.IncludesMonitor(taskbar.DeviceName))
            {
                any |= WindowComposition.Apply(taskbar.Hwnd, appearance);
            }
            else
            {
                // Monitor fora da seleção volta ao padrão, para que desmarcar uma tela
                // realmente a limpe em vez de deixar o efeito anterior preso nela.
                WindowComposition.Reset(taskbar.Hwnd);
            }
        }

        if (!any)
        {
            _watchdog.RecordFailure(target, "composition rejected");
            return ApplyOutcome.Failed(target, "composition rejected");
        }

        _watchdog.CommitSuccess(target);
        return ApplyOutcome.Ok(target);
    }

    private ApplyOutcome ApplyExplorerWindows(WinTheme theme)
    {
        const string target = "Explorer.Window";

        var appearance = WinTheme.Get(theme.Explorer, ExplorerRegion.Window);
        if (appearance.Kind == AppearanceKind.WindowsDefault)
        {
            foreach (var hwnd in WindowComposition.FindExplorerWindows())
                WindowComposition.Reset(hwnd);
            return ApplyOutcome.Ok(target);
        }

        var capability = _capabilities.ForExplorer(ExplorerRegion.Window, appearance.Kind);
        if (!capability.Available) return ApplyOutcome.Skipped(target, capability.Reason);

        if (!_watchdog.BeginAttempt(target))
            return ApplyOutcome.Failed(target, "quarantined");

        // Nenhuma janela aberta não é falha — é só não haver o que pintar agora.
        var windows = WindowComposition.FindExplorerWindows();
        foreach (var hwnd in windows)
            WindowComposition.Apply(hwnd, appearance);

        _watchdog.CommitSuccess(target);
        return ApplyOutcome.Ok(target);
    }

    /// <summary>
    /// Alvos que só o motor de injeção alcança. Com a injeção desligada (padrão), devolve
    /// "indisponível" para cada um — a interface já mostra esses alvos como beta/indisponíveis,
    /// então isto apenas confirma o motivo, sem nunca tentar forçar.
    /// </summary>
    private IEnumerable<ApplyOutcome> ApplyInjectionTargets(WinTheme theme, WinCustomSettings settings)
    {
        var results = new List<ApplyOutcome>();

        bool injection = settings.EnableInjection && InjectionHost.IsAvailable;

        foreach (var region in Enum.GetValues<StartRegion>())
        {
            var appearance = WinTheme.Get(theme.Start, region);
            if (appearance.Kind == AppearanceKind.WindowsDefault) continue;
            results.Add(injection
                ? InjectionHost.Apply($"Start.{region}", appearance, _watchdog)
                : ApplyOutcome.Skipped($"Start.{region}", UnavailableReason.NeedsInjection));
        }

        foreach (var region in Enum.GetValues<SettingsRegion>())
        {
            var appearance = WinTheme.Get(theme.Settings, region);
            if (appearance.Kind == AppearanceKind.WindowsDefault) continue;
            results.Add(injection
                ? InjectionHost.Apply($"Settings.{region}", appearance, _watchdog)
                : ApplyOutcome.Skipped($"Settings.{region}", UnavailableReason.NeedsInjection));
        }

        foreach (var region in Enum.GetValues<ExplorerRegion>())
        {
            if (region == ExplorerRegion.Window) continue;   // já tratada sem injeção
            var appearance = WinTheme.Get(theme.Explorer, region);
            if (appearance.Kind == AppearanceKind.WindowsDefault) continue;
            results.Add(injection
                ? InjectionHost.Apply($"Explorer.{region}", appearance, _watchdog)
                : ApplyOutcome.Skipped($"Explorer.{region}", UnavailableReason.NeedsInjection));
        }

        return results;
    }

    // =====================================================================================
    //  Controle
    // =====================================================================================

    /// <summary>Liga a personalização e aplica o tema ativo.</summary>
    public void Start()
    {
        var settings = _settings();
        settings.Enabled = true;
        IsPaused = false;
        _persist();
        ApplyActiveTheme();
        StartExplorerWatch();
    }

    /// <summary>
    /// Para temporariamente: restaura o visual padrão mas MANTÉM o tema escolhido, para
    /// "Reativar" devolver tudo como estava.
    /// </summary>
    public void Pause()
    {
        IsPaused = true;
        RestoreAll(clearTheme: false);
        StateChanged?.Invoke();
    }

    /// <summary>Retoma depois de uma pausa.</summary>
    public void Resume()
    {
        IsPaused = false;
        ApplyActiveTheme();
        StateChanged?.Invoke();
    }

    /// <summary>Reinicia o subsistema — restaura e aplica de novo, do zero.</summary>
    public void Restart()
    {
        RestoreAll(clearTheme: false);
        IsPaused = false;
        ApplyActiveTheme();
        StartExplorerWatch();
    }

    /// <summary>Escolhe o tema ativo e aplica na hora.</summary>
    public void SetActiveTheme(WinTheme theme)
    {
        var settings = _settings();
        settings.ActiveThemeId = theme.Id;
        settings.Enabled = true;
        IsPaused = false;
        _persist();
        ApplyActiveTheme();
    }

    // =====================================================================================
    //  Reversibilidade
    // =====================================================================================

    /// <summary>Restaura a barra de tarefas ao padrão do Windows.</summary>
    public void RestoreTaskbar()
    {
        foreach (var hwnd in WindowComposition.FindAllTaskbars())
            WindowComposition.Reset(hwnd);
        _watchdog.Release("Taskbar");
        StateChanged?.Invoke();
    }

    /// <summary>Restaura o Explorador de Arquivos (janelas + regiões injetadas).</summary>
    public void RestoreExplorer()
    {
        foreach (var hwnd in WindowComposition.FindExplorerWindows())
            WindowComposition.Reset(hwnd);

        InjectionHost.RestoreTarget("Explorer");
        _watchdog.Release("Explorer.Window");
        foreach (var region in Enum.GetValues<ExplorerRegion>())
            _watchdog.Release($"Explorer.{region}");

        StateChanged?.Invoke();
    }

    /// <summary>Restaura o Menu Iniciar.</summary>
    public void RestoreStartMenu()
    {
        InjectionHost.RestoreTarget("Start");
        foreach (var region in Enum.GetValues<StartRegion>())
            _watchdog.Release($"Start.{region}");
        StateChanged?.Invoke();
    }

    /// <summary>Restaura as Configurações do Windows.</summary>
    public void RestoreSettings()
    {
        InjectionHost.RestoreTarget("Settings");
        foreach (var region in Enum.GetValues<SettingsRegion>())
            _watchdog.Release($"Settings.{region}");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// RESTAURAR TODA A PERSONALIZAÇÃO DO WINDOWS. Devolve cada componente ao visual padrão,
    /// desliga o motor de injeção e limpa a quarentena. Com <paramref name="clearTheme"/>,
    /// também esquece o tema ativo e desliga o subsistema — é o "voltar ao estado de fábrica".
    /// </summary>
    public void RestoreAll(bool clearTheme = true)
    {
        RestoreTaskbar();
        RestoreExplorer();
        RestoreStartMenu();
        RestoreSettings();

        InjectionHost.Shutdown();

        if (clearTheme)
        {
            var settings = _settings();
            settings.Enabled = false;
            settings.ActiveThemeId = "";
            settings.EnableInjection = false;
            _persist();
            _watchdog.ReleaseAll();
            StopExplorerWatch();
        }

        StateChanged?.Invoke();
    }

    // =====================================================================================
    //  Reaplicação quando o Explorer reinicia
    // =====================================================================================

    /// <summary>
    /// Vigia o processo do Explorer. Quando ele reinicia (crash, "Reiniciar o Explorer", término
    /// de sessão), as janelas são recriadas do zero e perdem os atributos de composição — por
    /// isso reaplicamos. É a garantia de persistência pedida, e funciona igualmente para o
    /// reinício provocado pelo próprio usuário.
    /// </summary>
    public void StartExplorerWatch()
    {
        if (!_settings().ReapplyOnExplorerRestart) return;
        if (_explorerWatch is not null) return;

        _lastExplorerPid = CurrentExplorerPid();

        _explorerWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _explorerWatch.Tick += (_, _) =>
        {
            int pid = CurrentExplorerPid();
            if (pid == 0 || pid == _lastExplorerPid) return;

            // PID novo = Explorer reiniciado. Damos um instante para o Shell montar as janelas
            // antes de pintar; aplicar cedo demais simplesmente não pega.
            _lastExplorerPid = pid;

            // As janelas antigas morreram junto com o processo: o que sabíamos sobre elas não
            // vale mais, e sem limpar isso a guarda de idempotência acharia que as novas já
            // estão pintadas.
            WindowComposition.ForgetAll();
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                if (_settings().Enabled && !IsPaused) ApplyActiveTheme();
            };
            delay.Start();
        };
        _explorerWatch.Start();
    }

    public void StopExplorerWatch()
    {
        _explorerWatch?.Stop();
        _explorerWatch = null;
    }

    private static int CurrentExplorerPid()
    {
        try
        {
            var processes = Process.GetProcessesByName("explorer");
            // O Explorer do Shell é o mais antigo; janelas de pasta podem ter processos próprios.
            var shell = processes.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } })
                                 .FirstOrDefault();
            int pid = shell?.Id ?? 0;
            foreach (var p in processes) p.Dispose();
            return pid;
        }
        catch { return 0; }
    }
}
