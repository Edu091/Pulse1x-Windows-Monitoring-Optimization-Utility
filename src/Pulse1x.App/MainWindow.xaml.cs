using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Pulse1x.App.Services;
using Pulse1x.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Pulse1x.App;

public partial class MainWindow : FluentWindow
{
    private readonly SettingsService _settingsService;
    private readonly DashboardPage _dashboardPage;
    private readonly OptimizationPage _optimizationPage;
    private readonly HealthPage _healthPage;
    private readonly LatencyPage _latencyPage;
    private readonly UtilityPage _utilityPage;
    private readonly SettingsPage _settingsPage;
    private readonly AboutPage _aboutPage;

    private Control[] _navButtons = System.Array.Empty<Control>();
    private UIElement[] _navIndicators = System.Array.Empty<UIElement>();
    private bool _firstNavigation = true;

    public TrayIconService? TrayIconService { get; set; }

    private bool _allowClose;

    public MainWindow(
        SettingsService settingsService,
        DashboardPage dashboardPage,
        OptimizationPage optimizationPage,
        HealthPage healthPage,
        LatencyPage latencyPage,
        UtilityPage utilityPage,
        SettingsPage settingsPage,
        AboutPage aboutPage)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _dashboardPage = dashboardPage;
        _optimizationPage = optimizationPage;
        _healthPage = healthPage;
        _latencyPage = latencyPage;
        _utilityPage = utilityPage;
        _settingsPage = settingsPage;
        _aboutPage = aboutPage;

        _navButtons = new Control[]
        {
            DashboardButton, OptimizationButton, HealthButton, LatencyButton,
            UtilityButton, SettingsButton, AboutButton,
        };
        // Barras de acento (à esquerda de cada item) — paralelas a _navButtons, na mesma ordem.
        _navIndicators = new UIElement[]
        {
            DashboardIndicator, OptimizationIndicator, HealthIndicator, LatencyIndicator,
            UtilityIndicator, SettingsIndicator, AboutIndicator,
        };

        // Anima a entrada de cada página ao navegar (fade + leve deslize), respeitando o
        // ajuste de animações nas Configurações. A animação de abertura (Loaded) cuida do
        // fade-in inicial sem "flash", mantendo o valor base visível como segurança.
        ContentFrame.Navigated += OnFrameNavigated;

        // Feedback tátil de clique para TODOS os botões do app (um só lugar). Encolhe levemente
        // ao pressionar e volta ao soltar. Só botões (não ToggleButton/CheckBox), para não
        // conflitar com a rotação do chevron (anim:Anim.Spin usa RenderTransform).
        PreviewMouseLeftButtonDown += OnAnyButtonPressed;
        PreviewMouseLeftButtonUp += OnAnyButtonReleased;
        PreviewMouseUp += OnAnyButtonReleased;

        NavigateTo(_dashboardPage, DashboardButton);

        Loaded += (_, _) =>
        {
            // ApplicationThemeManager.Apply chamado antes da janela existir nem sempre
            // propaga as cores corretamente para todos os controles; reaplicar aqui
            // garante que o tema escuro funcione já na primeira abertura.
            ApplicationThemeManager.Apply(_settingsService.Current.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

            // Animação de abertura: fade + leve zoom do conteúdo principal.
            Animations.OpenWindow(ContentRoot);
        };
    }

    // Navega para a página e marca a aba correspondente como ativa.
    private void NavigateTo(System.Windows.Controls.Page page, Control navButton)
    {
        ContentFrame.Navigate(page);
        SetActiveNav(navButton);
    }

    // Destaca a aba ativa acendendo sua barra de acento (à esquerda) e apagando as demais.
    private void SetActiveNav(Control active)
    {
        int idx = System.Array.IndexOf(_navButtons, active);
        for (int i = 0; i < _navIndicators.Length; i++)
            Animations.FadeTo(_navIndicators[i], i == idx ? 1.0 : 0.0);
    }

    private System.Windows.Controls.Button? _pressedButton;

    private void OnAnyButtonPressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!AnimationSettings.Enabled) return;
        var button = FindAncestorButton(e.OriginalSource as DependencyObject);
        if (button is null) return;
        _pressedButton = button;
        Animations.PressScale(button, 0.96);
    }

    private void OnAnyButtonReleased(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_pressedButton is null) return;
        Animations.PressScale(_pressedButton, 1.0);
        _pressedButton = null;
    }

    // Sobe na árvore visual a partir do alvo do clique até achar um Button (ui:Button deriva de
    // System.Windows.Controls.Button). ToggleButton/CheckBox ficam de fora de propósito.
    private static System.Windows.Controls.Button? FindAncestorButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Button b) return b;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        // A primeira navegação acontece antes da janela aparecer; a animação de abertura já
        // cobre essa entrada, então não animamos a página duas vezes.
        if (_firstNavigation)
        {
            _firstNavigation = false;
            return;
        }

        if (e.Content is UIElement content)
            Animations.FadeSlideIn(content);
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_dashboardPage, DashboardButton);

    private void OptimizationButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_optimizationPage, OptimizationButton);

    private void HealthButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_healthPage, HealthButton);

    private void LatencyButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_latencyPage, LatencyButton);

    private void UtilityButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_utilityPage, UtilityButton);

    /// <summary>Permite que outras páginas (ex.: Saúde) abram a categoria Otimização.</summary>
    public void NavigateToOptimization() => NavigateTo(_optimizationPage, OptimizationButton);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsPage, SettingsButton);

    private void AboutButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_aboutPage, AboutButton);

    private void FluentWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settingsService.Current.MinimizeToTray)
        {
            TrayIconService?.MinimizeToTray();
        }
    }

    public void ExitApplication()
    {
        _allowClose = true;
        Close();
        Application.Current.Shutdown();
    }

    private void FluentWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // ExitApplication() já dispara o Shutdown; nada a fazer aqui.
        if (_allowClose)
            return;

        // Com "minimizar para a bandeja" ligado, fechar apenas esconde a janela.
        if (_settingsService.Current.MinimizeToTray)
        {
            e.Cancel = true;
            TrayIconService?.MinimizeToTray();
            return;
        }

        // Fechamento real (minimizar para bandeja desligado): encerra a aplicação
        // inteira. Sem isto, como o ShutdownMode é OnExplicitShutdown e o ícone da
        // bandeja mantém o app vivo, o processo continuaria rodando em segundo plano
        // a cada vez que a janela é fechada — acumulando processos fantasmas.
        _allowClose = true;
        Application.Current.Shutdown();
    }
}
