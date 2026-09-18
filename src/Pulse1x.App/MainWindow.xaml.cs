using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;
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
    private readonly Views.GameHub.GameHubPage _gameHubPage;
    private readonly UtilityPage _utilityPage;
    private readonly SettingsPage _settingsPage;
    private readonly AboutPage _aboutPage;
    private readonly DonatePage _donatePage;

    private Control[] _navButtons = System.Array.Empty<Control>();
    private UIElement[] _navIndicators = System.Array.Empty<UIElement>();
    private bool _firstNavigation = true;

    public TrayIconService? TrayIconService { get; set; }

    private bool _allowClose;

    private readonly ThemeService _themeService;

    public MainWindow(
        SettingsService settingsService,
        ThemeService themeService,
        DashboardPage dashboardPage,
        OptimizationPage optimizationPage,
        HealthPage healthPage,
        LatencyPage latencyPage,
        Views.GameHub.GameHubPage gameHubPage,
        UtilityPage utilityPage,
        SettingsPage settingsPage,
        AboutPage aboutPage,
        DonatePage donatePage)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _themeService = themeService;
        _dashboardPage = dashboardPage;
        _optimizationPage = optimizationPage;
        _healthPage = healthPage;
        _latencyPage = latencyPage;
        _gameHubPage = gameHubPage;
        _utilityPage = utilityPage;
        _settingsPage = settingsPage;
        _aboutPage = aboutPage;
        _donatePage = donatePage;

        _navButtons = new Control[]
        {
            DashboardButton, OptimizationButton, HealthButton, LatencyButton, GameHubButton,
            UtilityButton, SettingsButton, AboutButton, DonateButton,
        };
        // Barras de acento (à esquerda de cada item) — paralelas a _navButtons, na mesma ordem.
        _navIndicators = new UIElement[]
        {
            DashboardIndicator, OptimizationIndicator, HealthIndicator, LatencyIndicator, GameHubIndicator,
            UtilityIndicator, SettingsIndicator, AboutIndicator, DonateIndicator,
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

        // Plano de fundo personalizado: aplicado agora e a cada mudança nas Configurações ou de
        // jogo selecionado no GameHub (modo "baseado no jogo").
        _themeService.AppearanceChanged += ApplyAppearance;
        ApplyAppearance();
    }

    // =====================================================================================
    //  Controle em todo o aplicativo
    // =====================================================================================

    private GamepadService? _gamepad;

    /// <summary>
    /// Liga a navegação por controle em TODO o Pulse1x, não só no GameHub. Fora do hub, o
    /// direcional move o foco pela interface e A aciona o que estiver em foco — assim as
    /// Configurações, as Otimizações e as demais categorias também funcionam sem mouse.
    ///
    /// Dentro do GameHub, a própria página assume a entrada (a grade tem navegação própria), então
    /// aqui simplesmente não interferimos.
    /// </summary>
    public void AttachGamepad(GamepadService gamepad)
    {
        _gamepad = gamepad;
        gamepad.Navigate += OnGlobalGamepadNavigate;
        gamepad.Action += OnGlobalGamepadAction;

        // Fora do hub o controle também deve funcionar, então a leitura fica sempre ativa enquanto
        // a janela existe; o GameHub apenas passa a tratar os eventos por conta própria.
        gamepad.SetActive(true);
    }

    private void OnGlobalGamepadNavigate(GamepadDirection direction)
    {
        if (_inGameHub) return;   // a página do hub trata a entrada

        // Vale também para os diálogos: o foco é global, então a navegação segue a janela ativa.
        GamepadFocusService.EnsureFocusInActiveWindow();
        GamepadFocusService.Move(direction);
    }

    private void OnGlobalGamepadAction(GamepadAction action)
    {
        if (_inGameHub) return;

        // Com um diálogo aberto, A aciona o que está em foco e B fecha a janela.
        if (GamepadFocusService.IsDialogActive())
        {
            if (action == GamepadAction.Accept) GamepadFocusService.Accept();
            else if (action == GamepadAction.Back) GamepadFocusService.ActiveWindow()?.Close();
            return;
        }

        switch (action)
        {
            case GamepadAction.Accept:
                GamepadFocusService.Accept();
                break;

            // Sem o hub aberto, os ombros percorrem as categorias do app.
            case GamepadAction.NextTab:
                CycleCategory(1);
                break;
            case GamepadAction.PreviousTab:
                CycleCategory(-1);
                break;

            // View abre o GameHub de qualquer tela — é o atalho para a experiência de sofá.
            case GamepadAction.Menu:
                EnterGameHub();
                break;
        }
    }

    /// <summary>Percorre as categorias da navegação lateral com os ombros do controle.</summary>
    private void CycleCategory(int delta)
    {
        var pages = new System.Windows.Controls.Page[]
        {
            _dashboardPage, _optimizationPage, _healthPage, _latencyPage, _utilityPage, _settingsPage,
        };
        var buttons = new Control[]
        {
            DashboardButton, OptimizationButton, HealthButton, LatencyButton, UtilityButton, SettingsButton,
        };

        int current = System.Array.FindIndex(pages, p => ReferenceEquals(p, ContentFrame.Content));
        if (current < 0) current = 0;

        int next = (current + delta + pages.Length) % pages.Length;
        NavigateTo(pages[next], buttons[next]);
    }

    /// <summary>
    /// Monta as camadas de fundo do aplicativo conforme a personalização escolhida. A ordem é
    /// sempre a mesma — preenchimento, imagem, escurecimento — e o conteúdo do app fica por cima.
    /// No modo padrão nada é desenhado e o Mica do Windows continua aparecendo.
    /// </summary>
    private void ApplyAppearance()
    {
        var appearance = _themeService.Appearance;

        // O Mica precisa sair de cena quando há um fundo próprio: os dois juntos deixariam a
        // imagem lavada e sem contraste.
        bool custom = appearance.Background != BackgroundMode.Default;
        WindowBackdropType = custom
            ? Wpf.Ui.Controls.WindowBackdropType.None
            : Wpf.Ui.Controls.WindowBackdropType.Mica;

        var fill = _themeService.BuildBackgroundBrush();
        BackgroundFill.Fill = fill;
        BackgroundFill.Visibility = fill is null ? Visibility.Collapsed : Visibility.Visible;

        string? imagePath = _themeService.CurrentBackgroundImage;
        if (string.IsNullOrEmpty(imagePath))
        {
            BackgroundImage.Source = null;
            BackgroundImage.Visibility = Visibility.Collapsed;
            BackgroundDarken.Visibility = Visibility.Collapsed;
            return;
        }

        var bitmap = Services.GameHub.GameArtService.LoadBitmap(imagePath, 1920);
        if (bitmap is null)
        {
            BackgroundImage.Visibility = Visibility.Collapsed;
            BackgroundDarken.Visibility = Visibility.Collapsed;
            return;
        }

        BackgroundImage.Source = ImageEffects.ApplySaturation(bitmap, appearance.BackgroundSaturation);
        BackgroundImage.Opacity = appearance.BackgroundOpacity;
        BackgroundImage.Stretch = appearance.BackgroundFit switch
        {
            BackgroundFit.Fit => System.Windows.Media.Stretch.Uniform,
            BackgroundFit.Stretch => System.Windows.Media.Stretch.Fill,
            BackgroundFit.Center or BackgroundFit.Tile => System.Windows.Media.Stretch.None,
            _ => System.Windows.Media.Stretch.UniformToFill,
        };
        BackgroundImage.Visibility = Visibility.Visible;

        // O raio passa pela intensidade global de blur, para um único ajuste governar todo o app.
        BackgroundBlur.Radius = _themeService.EffectiveBlur(appearance.BackgroundBlur);

        BackgroundDarken.Opacity = appearance.BackgroundDarken;
        BackgroundDarken.Visibility = appearance.BackgroundDarken > 0.01 ? Visibility.Visible : Visibility.Collapsed;
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

    private void GameHubButton_Click(object sender, RoutedEventArgs e) => EnterGameHub();

    // =====================================================================================
    //  Modo GameHub
    // =====================================================================================

    /// <summary>Estado da janela antes de entrar no GameHub, para devolvê-lo ao sair.</summary>
    private WindowState _stateBeforeHub = WindowState.Normal;
    private bool _inGameHub;

    /// <summary>
    /// Entra no GameHub. Quando o modo imersivo está ligado (padrão), a navegação lateral do
    /// Pulse1x some e o hub ocupa a janela inteira — é o que dá a ele a identidade de interface de
    /// console em vez de "mais uma aba do app". O botão discreto de sair, dentro do próprio hub,
    /// devolve o Pulse1x normal.
    /// </summary>
    public void EnterGameHub()
    {
        var hub = _settingsService.Current.GameHub;

        // Dentro do hub, a entrada do controle pertence à página: ela conhece as zonas (grade,
        // cromo, destaque, menu, teclado) e evita que dois handlers briguem pelo mesmo toque.
        if (_gamepad is not null) _gameHubPage.AttachGamepad(_gamepad);

        if (hub.MaximizeOnOpen && !_inGameHub)
        {
            _stateBeforeHub = WindowState;
            WindowState = WindowState.Maximized;
        }

        if (hub.Immersive)
        {
            NavColumn.Width = new GridLength(0);
            NavPanel.Visibility = Visibility.Collapsed;
            // A barra de título sai de cena: o hub desenha o próprio cromo, e a janela continua
            // arrastável pela área superior dele.
            AppTitleBar.Visibility = Visibility.Collapsed;
            TitleRow.Height = new GridLength(0);
        }

        _inGameHub = true;
        NavigateTo(_gameHubPage, GameHubButton);
    }

    /// <summary>Sai do GameHub e devolve a janela ao Pulse1x normal.</summary>
    public void ExitGameHub()
    {
        if (!_inGameHub) return;
        _inGameHub = false;

        _gameHubPage.DetachGamepad();

        NavColumn.Width = new GridLength(220);
        NavPanel.Visibility = Visibility.Visible;
        AppTitleBar.Visibility = Visibility.Visible;
        TitleRow.Height = GridLength.Auto;

        if (_settingsService.Current.GameHub.MaximizeOnOpen)
            WindowState = _stateBeforeHub;

        NavigateTo(_dashboardPage, DashboardButton);
    }

    private void UtilityButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_utilityPage, UtilityButton);

    /// <summary>Permite que outras páginas (ex.: Saúde) abram a categoria Otimização.</summary>
    public void NavigateToOptimization() => NavigateTo(_optimizationPage, OptimizationButton);

    /// <summary>Abre as Configurações (usado pelo menu lateral do GameHub).</summary>
    public void NavigateToSettings()
    {
        if (_inGameHub) ExitGameHub();
        NavigateTo(_settingsPage, SettingsButton);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsPage, SettingsButton);

    private void AboutButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_aboutPage, AboutButton);

    private void DonateButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_donatePage, DonateButton);

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
