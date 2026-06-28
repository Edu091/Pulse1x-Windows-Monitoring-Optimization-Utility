using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pulse1x.App.Services;
using Pulse1x.App.ViewModels;
using Pulse1x.App.Views;
using Wpf.Ui.Appearance;

namespace Pulse1x.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Pulse1x_SingleInstance_Mutex";
    private const string ShowWindowSignalName = "Pulse1x_ShowWindow_Event";

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
        var settingsPage = new SettingsPage(new SettingsViewModel(
            settingsService,
            onIntervalChanged: dashboardViewModel.UpdateInterval,
            onMinimizeToTrayChanged: _ => { }));
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
        var latencyPage = new LatencyPage(new LatencyViewModel(networkLatencyService, networkOptimizationService, systemMetricsService));

        // Categoria Utilidade: Central Pós-Formatação — instala apps/componentes de fonte oficial
        // (winget) e aplica configurações recomendadas, com detecção inteligente do hardware.
        var appInstallService = new AppInstallService();
        var postFormatTweaksService = new PostFormatTweaksService(specialCommandsService);
        var utilityPage = new UtilityPage(new PostFormatViewModel(appInstallService, postFormatTweaksService));

        mainWindow = new MainWindow(settingsService, dashboardPage, optimizationPage, healthPage, latencyPage, utilityPage, settingsPage, aboutPage, donatePage);

        _trayIconService = new TrayIconService(mainWindow, onExitRequested: () =>
        {
            mainWindow.ExitApplication();
        });
        _trayIconService.Initialize();
        mainWindow.TrayIconService = _trayIconService;

        MainWindow = mainWindow;
        mainWindow.Show();

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
        base.OnExit(e);
    }
}
