using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.Services.Profiles;
using Pulse1x.App.ViewModels;
using Pulse1x.App.ViewModels.GameHub;
using Pulse1x.App.Views;
using Wpf.Ui.Appearance;

namespace Pulse1x.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Pulse1x_SingleInstance_Mutex";
    private const string ShowWindowSignalName = "Pulse1x_ShowWindow_Event";

    /// <summary>
    /// O MESMO nome do mutex, mas no espaço "Global\" — visível por TODAS as sessões do Windows.
    ///
    /// Existe por causa do instalador: o Inno Setup roda elevado e usa o AppMutex para descobrir
    /// que o Pulse1x está aberto, fechá-lo e reabri-lo depois (é o que permite atualizar sem
    /// desinstalar). Um mutex comum vive apenas na sessão de quem o criou, então o instalador
    /// elevado podia não enxergá-lo, não fechar o app e então falhar ao substituir o .exe em uso.
    ///
    /// É um mutex ADICIONAL: o local continua sendo o que controla a instância única, para que a
    /// detecção siga funcionando contra versões anteriores já instaladas.
    /// </summary>
    private const string GlobalInstanceMutexName = @"Global\Pulse1x_SingleInstance_Mutex";

    private Mutex? _globalInstanceMutex;

    // Identidade estável do app perante o Shell do Windows (Menu Iniciar/busca/barra de tarefas),
    // independente do caminho ou do conteúdo binário do .exe. Sem isso, como o Pulse1x.App.exe é
    // substituído NO MESMO CAMINHO a cada atualização (tanto pelo instalador Inno Setup quanto pelo
    // SelfUpdateService), o Windows às vezes trata a identidade do app como "incerta" entre uma
    // atualização e outra e demora para re-cachear o ícone nos resultados de busca. Precisa ser
    // chamado bem no início, antes de qualquer janela ser criada.
    private const string AppUserModelId = "Pulse1x.App";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    // Cor de marca do Pulse1x: vermelho, usado em todos os controles do Wpf.Ui
    // (botões, toggles, sliders, barra de progresso etc.) em vez do azul padrão.
    private static readonly Color BrandAccentColor = Color.FromRgb(0xDC, 0x26, 0x26);

    private HardwareMonitorService? _hardwareMonitorService;
    private TrayIconService? _trayIconService;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowSignal;
    private RegisteredWaitHandle? _showWindowWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
        catch { /* best-effort — só melhora o cache de ícone do Shell, nunca deve impedir o app de abrir */ }

        // Rede de segurança: registra qualquer exceção não tratada num arquivo de log (e mostra
        // uma mensagem) em vez de o app "abrir e fechar" sem deixar pista. Para erros na thread de
        // UI, marca como tratado para tentar manter o app vivo — um erro secundário (ex.: ícone,
        // binding) não deve derrubar o programa inteiro.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        // Trava de instância única: se o Pulse1x já estiver rodando (inclusive
        // minimizado na bandeja), sinaliza a instância existente para aparecer e
        // encerra esta — evita empilhar vários processos do app.
        var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Esta instância NÃO é dona do mutex; descarta sem liberar (ReleaseMutex
            // num mutex não possuído lançaria exceção).
            mutex.Dispose();

            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowWindowSignalName, out var existingSignal))
                {
                    existingSignal.Set();
                    existingSignal.Dispose();
                }
            }
            catch { /* se não der para sinalizar, apenas encerra esta instância */ }

            Shutdown();
            return;
        }

        _singleInstanceMutex = mutex;

        // Marca a presença do app para o instalador elevado (ver GlobalInstanceMutexName).
        // Nunca pode impedir o app de abrir: se o sistema negar a criação no espaço global,
        // seguimos com o mutex local e o instalador apenas pedirá para fechar o app à mão.
        try { _globalInstanceMutex = new Mutex(initiallyOwned: false, GlobalInstanceMutexName, out _); }
        catch { _globalInstanceMutex = null; }

        if (SelfUpdateService.TryApplyPendingUpdate())
        {
            // Uma versao mais nova foi encontrada: um novo processo ja foi iniciado
            // no lugar deste, entao este aqui apenas finaliza sem abrir nada.
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        AnimationSettings.Enabled = settingsService.Current.AnimationsEnabled;
        Localization.Loc.Instance.SetLanguage(Localization.Loc.FromCode(settingsService.Current.Language));
        var appTheme = settingsService.Current.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(appTheme);
        ApplicationAccentColorManager.Apply(BrandAccentColor, appTheme);

        // Personalização visual: espalha as cores, a intensidade das animações e o plano de fundo
        // escolhidos pelo usuário por todo o app, antes de qualquer tela ser criada.
        var themeService = new ThemeService(settingsService);
        themeService.Apply();

        _hardwareMonitorService = new HardwareMonitorService();
        var systemMetricsService = new SystemMetricsService();
        var systemInfoService = new SystemInfoService();
        var hardwareDetailsService = new HardwareDetailsService(_hardwareMonitorService);

        var dashboardViewModel = new DashboardViewModel(_hardwareMonitorService, systemMetricsService, systemInfoService, settingsService);

        var dashboardPage = new DashboardPage(dashboardViewModel, hardwareDetailsService);
        var memoryOptimizationService = new MemoryOptimizationService(systemMetricsService);
        var diskCleanupService = new DiskCleanupService();
        var specialCommandsService = new SpecialCommandsService();
        var advancedOptimizationService = new AdvancedOptimizationService(new OptimizationChangeLog());
        var bloatwareDetectorService = new BloatwareDetectorService(advancedOptimizationService);
        var optimizationPage = new OptimizationPage(new OptimizationViewModel(memoryOptimizationService, systemMetricsService, diskCleanupService, specialCommandsService, advancedOptimizationService, bloatwareDetectorService));
        var updateService = new GitHubUpdateService();
        var settingsViewModel = new SettingsViewModel(
            settingsService,
            themeService,
            updateService,
            onIntervalChanged: dashboardViewModel.UpdateInterval,
            onMinimizeToTrayChanged: _ => { });
        var settingsPage = new SettingsPage(settingsViewModel);
        var aboutPage = new AboutPage();
        var donatePage = new DonatePage();

        // A categoria Saúde reaproveita os mesmos serviços de hardware/sistema (somente leitura)
        // e as ferramentas seguras já existentes (limpeza, SFC/DISM) para as ações.
        // O callback de "Abrir Otimizações" captura a janela, que é criada logo abaixo.
        MainWindow mainWindow = null!;
        var healthDiagnosticsService = new HealthDiagnosticsService(_hardwareMonitorService, systemMetricsService, systemInfoService);
        var healthViewModel = new HealthViewModel(
            healthDiagnosticsService, specialCommandsService, diskCleanupService,
            openOptimizations: () => mainWindow.NavigateToOptimization());
        var healthPage = new HealthPage(healthViewModel);

        // Categoria Latência: leitura de rede/Wi-Fi (somente leitura) + otimizações reversíveis,
        // com log de reversão próprio (network-changes.json) para "Desfazer Todas as Alterações".
        var networkLatencyService = new NetworkLatencyService();
        var networkOptimizationService = new NetworkOptimizationService(new OptimizationChangeLog("network-changes.json"));
        var serverStatusService = new ServerStatusService();
        var latencyViewModel = new LatencyViewModel(networkLatencyService, networkOptimizationService, systemMetricsService, serverStatusService);
        var latencyPage = new LatencyPage(latencyViewModel);

        // Categoria Utilidade: Central Pós-Formatação — instala apps/componentes de fonte oficial
        // (winget) e aplica configurações recomendadas, com detecção inteligente do hardware.
        var appInstallService = new AppInstallService();
        var postFormatTweaksService = new PostFormatTweaksService(specialCommandsService);
        var utilityPage = new UtilityPage(new PostFormatViewModel(appInstallService, postFormatTweaksService));

        // ---------------------------------------------------------------------------------
        //  GameHub — biblioteca unificada de jogos/apps + perfis individuais de sistema.
        //  Os módulos que o Pulse1x já tinha são reaproveitados como estão: a otimização de RAM
        //  (MemoryOptimizationService) e os perfis/ferramentas de rede e latência
        //  (NetworkOptimizationService, com o mesmo log de reversão da categoria Latência).
        // ---------------------------------------------------------------------------------
        var libraryService = new GameLibraryService();
        var profileStore = new ProfileStoreService();
        var artService = new GameArtService();
        var snapshotService = new SnapshotService();
        var gamingModeService = new GamingModeService();

        var powerPlanService = new PowerPlanService();
        var oemVendorService = new OemVendorService(settingsService.Current.OemCommands);
        var audioService = new AudioService();
        var displayService = new DisplayService();
        var timerResolutionService = new TimerResolutionService();
        var processControlService = new ProcessControlService();

        var profileEngine = new GameProfileEngine(
            powerPlanService, oemVendorService, audioService, displayService, timerResolutionService,
            processControlService, memoryOptimizationService, networkOptimizationService, snapshotService);

        var gamepadService = new GamepadService();
        var hubStatusService = new HubStatusService(_hardwareMonitorService);
        var playMetricsService = new PlayMetricsService();
        var fpsMonitorService = new FpsMonitorService();

        var sessionManager = new GameSessionManager(
            profileEngine, libraryService, profileStore, snapshotService, gamingModeService,
            playMetricsService, fpsMonitorService);

        // Limpeza única: o mesmo jogo detectado por duas lojas (por exemplo, um título comprado na
        // Steam que também registra entrada da EA) aparecia duplicado na biblioteca.
        libraryService.RemoveDuplicates();

        // Sons de navegação do hub: tons curtos sintetizados pelo próprio Pulse1x, com volume e
        // arquivos personalizáveis nas Configurações.
        var hubSettings = settingsService.Current.GameHub;
        var soundService = new GameHubSoundService
        {
            Enabled = hubSettings.SoundEnabled,
            Volume = hubSettings.SoundVolume,
            CustomSounds = hubSettings.CustomSounds,
        };

        // Busca de capas na internet pelo nome do jogo (catálogo público da Steam).
        artService.Online = new OnlineArtService();
        artService.OnlineEnabled = hubSettings.OnlineArtEnabled;

        // As Configurações mexem no som ao vivo (volume, arquivos, restaurar padrão).
        settingsViewModel.Sounds = soundService;
        settingsViewModel.Metrics = playMetricsService;

        playMetricsService.Enabled = settingsService.Current.GameHub.MetricsEnabled;

        var gameHubViewModel = new GameHubViewModel(
            libraryService, profileStore, artService, sessionManager, gamingModeService,
            themeService, gamepadService, soundService, hubStatusService);

        var gameHubPage = new Views.GameHub.GameHubPage(
            gameHubViewModel, libraryService, profileStore, artService,
            powerPlanService, oemVendorService, audioService, displayService, settingsService, playMetricsService);

        // Modo Gaming: enquanto um jogo estiver aberto, os painéis em tempo real do Pulse1x param
        // de atualizar. É um único sinal, consumido por quem gasta recursos.
        gamingModeService.StateChanged += active =>
        {
            dashboardViewModel.SetActive(!active);
            latencyViewModel.SetGamingMode(active);
        };

        mainWindow = new MainWindow(settingsService, themeService, dashboardPage, optimizationPage, healthPage,
            latencyPage, gameHubPage, utilityPage, new Views.WinCustomPage(), settingsPage, aboutPage, donatePage);

        _trayIconService = new TrayIconService(mainWindow, onExitRequested: () =>
        {
            mainWindow.ExitApplication();
        });
        _trayIconService.Initialize();
        mainWindow.TrayIconService = _trayIconService;

        // "Sair do GameHub": o botão discreto dentro do hub devolve o Pulse1x normal.
        gameHubPage.ExitRequested += () => mainWindow.ExitGameHub();
        gameHubPage.SettingsRequested += () => mainWindow.NavigateToSettings();

        // Navegação por controle em todo o app (fora do hub, o direcional move o foco).
        mainWindow.AttachGamepad(gamepadService);

        MainWindow = mainWindow;
        mainWindow.Show();

        // Abrir direto no GameHub, quando o usuário preferir (Configurações ▸ GameHub).
        if (settingsService.Current.GameHub.StartInGameHub)
            mainWindow.EnterGameHub();


        // Checagem de atualização em segundo plano ao abrir o app: nunca bloqueia a abertura
        // (roda depois da janela já visível) e, se falhar (sem internet, GitHub fora do ar),
        // não incomoda o usuário — apenas não pergunta nada. Só quando há mesmo uma versão
        // mais nova é que aparece o diálogo perguntando se quer atualizar agora.
        _ = PromptForUpdateOnStartupAsync(updateService, mainWindow);

        // Recuperação do GameHub: se uma sessão de jogo não foi encerrada normalmente (o jogo
        // travou, o Pulse1x foi fechado, a máquina desligou), o snapshot ficou em disco. Desfaz o
        // que tinha sido alterado e avisa o usuário do que foi restaurado.
        _ = RecoverPendingSessionAsync(sessionManager, mainWindow);

        // Aguarda (em segundo plano) o sinal de outra instância recém-aberta para
        // trazer esta janela à frente em vez de criar um novo processo.
        _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowSignalName);
        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            _showWindowSignal,
            (_, _) => Dispatcher.Invoke(() =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    // Consulta a última Release do GitHub e, se houver uma versão mais nova que a instalada,
    // pergunta ao usuário se quer atualizar agora. Ao confirmar, o instalador é baixado e
    // executado silenciosamente — o Inno Setup fecha o Pulse1x, substitui os arquivos e o
    // reabre sozinho (ver GitHubUpdateService).
    private static async Task PromptForUpdateOnStartupAsync(GitHubUpdateService updateService, MainWindow owner)
    {
        var result = await updateService.CheckAsync();
        if (result.Status != UpdateCheckStatus.UpdateAvailable ||
            result.DownloadUrl is null || result.AssetName is null)
            return;

        var choice = MessageBox.Show(
            owner,
            Localization.Loc.F("Update_PromptBody", result.LatestVersion ?? "?"),
            Localization.Loc.S("Update_PromptTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (choice != MessageBoxResult.Yes) return;

        var installResult = await updateService.DownloadAndInstallAsync(result.DownloadUrl, result.AssetName);
        if (!installResult.Started)
        {
            MessageBox.Show(
                owner,
                Localization.Loc.F("Update_InstallFailedBody", installResult.ErrorMessage ?? "?"),
                Localization.Loc.S("Update_PromptTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Desfaz, na abertura, as alterações de uma sessão de jogo que não chegou ao fim. Roda em
    // segundo plano para não atrasar a janela e nunca incomoda quando não há nada pendente.
    private static async Task RecoverPendingSessionAsync(GameSessionManager sessions, MainWindow owner)
    {
        try
        {
            string? gameName = await sessions.RecoverPendingAsync();
            if (gameName is null) return;

            MessageBox.Show(
                owner,
                Localization.Loc.F("GH_RecoveredBody", gameName),
                Localization.Loc.S("GH_RecoveredTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { /* a recuperação é melhor esforço: nunca pode impedir o app de abrir */ }
    }

    // Grava o erro em %LOCALAPPDATA%\Pulse1x\crash.log e avisa o usuário. Nunca lança (um erro
    // dentro do logger não pode virar um segundo crash).
    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse1x");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "crash.log");
            string entry = $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {source} ===={Environment.NewLine}" +
                           $"{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(file, entry);

            MessageBox.Show(
                $"Ocorreu um erro inesperado:{Environment.NewLine}{Environment.NewLine}{ex?.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"Detalhes salvos em:{Environment.NewLine}{file}",
                "Pulse1x", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* logger nunca pode derrubar o app */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowWait?.Unregister(null);
        _showWindowSignal?.Dispose();
        _trayIconService?.Dispose();
        _hardwareMonitorService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        // Criado sem posse (initiallyOwned: false), então só descartamos — liberar um mutex não
        // possuído lançaria exceção.
        _globalInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
