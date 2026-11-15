using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly InputLabPage _inputLabPage;
    private readonly FpsInGamePage _fpsInGamePage;
    private readonly BenchmarkPage _benchmarkPage;
    private readonly SoundPage _soundPage;
    private readonly RecordingPage _recordingPage;
    private readonly Views.GameHub.GameHubPage _gameHubPage;
    private readonly UtilityPage _utilityPage;
    private readonly SettingsPage _settingsPage;
    private readonly AboutPage _aboutPage;
    private readonly DonatePage _donatePage;

    /// <summary>Páginas da navegação lateral na ordem em que aparecem, com o item de cada uma.</summary>
    private (System.Windows.Controls.Page Page, Controls.NavItem Item)[] _navMap = System.Array.Empty<(System.Windows.Controls.Page, Controls.NavItem)>();
    private Controls.NavItem[] _navButtons = System.Array.Empty<Controls.NavItem>();
    private Controls.NavItem? _activeNav;
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
        InputLabPage inputLabPage,
        FpsInGamePage fpsInGamePage,
        BenchmarkPage benchmarkPage,
        SoundPage soundPage,
        RecordingPage recordingPage,
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
        _inputLabPage = inputLabPage;
        _fpsInGamePage = fpsInGamePage;
        _benchmarkPage = benchmarkPage;
        _soundPage = soundPage;
        _recordingPage = recordingPage;
        _gameHubPage = gameHubPage;
        _utilityPage = utilityPage;
        _settingsPage = settingsPage;
        _aboutPage = aboutPage;
        _donatePage = donatePage;

        _latencyPage.InputLabRequested += NavigateToInputLab;
        _inputLabPage.BackRequested += NavigateToLatency;

        // Ordem visual da barra lateral: Visão geral → Otimização → Utilidades → Aplicativo. Os
        // ombros do controle percorrem as categorias nesta ordem (o GameHub fica de fora: ele
        // tem a própria experiência de tela cheia e é aberto pelo botão View).
        _navMap = new (System.Windows.Controls.Page, Controls.NavItem)[]
        {
            (_dashboardPage, DashboardButton),
            (_optimizationPage, OptimizationButton),
            (_latencyPage, LatencyButton),
            (_healthPage, HealthButton),
            (_benchmarkPage, BenchmarkButton),
            (_fpsInGamePage, FpsInGameButton),
            (_utilityPage, UtilityButton),
            (_soundPage, SoundButton),
            (_recordingPage, RecordingButton),
            (_settingsPage, SettingsButton),
            (_aboutPage, AboutButton),
            (_donatePage, DonateButton),
        };
        _navButtons = _navMap.Select(m => m.Item).Append(GameHubButton).ToArray();

        UpdateVersionText();
        Localization.Loc.Instance.LanguageChanged += UpdateVersionText;

        // A barra de acento acompanha o item ativo também quando o layout muda (idioma, tamanho).
        NavHost.SizeChanged += (_, _) => MoveSelectionPill(animate: false);

        // Selo "REC" no item Gravação enquanto uma gravação está em andamento.
        if (_recordingPage.DataContext is ViewModels.RecordingViewModel recording)
        {
            recording.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ViewModels.RecordingViewModel.IsRecording) or nameof(ViewModels.RecordingViewModel.IsPaused))
                    Dispatcher.BeginInvoke(() => UpdateRecordingBadge(recording));
            };
            UpdateRecordingBadge(recording);
        }

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
            _themeService.Apply();

            // Animação de abertura: fade + leve zoom do conteúdo principal, e os itens da
            // navegação entram em cascata.
            Animations.OpenWindow(ContentRoot);
            Animations.StaggerIn(NavHost.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<UIElement>()), offsetY: 6, stepMs: 18);
            MoveSelectionPill(animate: false);
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

    // O XInput/SDL entrega o controle mesmo com o Pulse1x atrás de outro programa ou escondido na
    // bandeja. Sem esta checagem, jogar outra coisa trocava as categorias do app (LB/RB) e o botão
    // View maximizava o GameHub por cima do jogo.
    private static bool IsPulse1xForeground() =>
        Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive && w.IsVisible) == true;

    private void OnGlobalGamepadNavigate(GamepadDirection direction)
    {
        if (!IsPulse1xForeground()) return;
        if (_inGameHub || ReferenceEquals(ContentFrame.Content, _inputLabPage)) return;

        // Vale também para os diálogos: o foco é global, então a navegação segue a janela ativa.
        GamepadFocusService.EnsureFocusInActiveWindow();
        GamepadFocusService.Move(direction);
    }

    private void OnGlobalGamepadAction(GamepadAction action)
    {
        if (!IsPulse1xForeground()) return;
        if (_inGameHub || ReferenceEquals(ContentFrame.Content, _inputLabPage)) return;

        // Com um diálogo aberto, A aciona o que está em foco e B fecha a janela.
        if (GamepadFocusService.IsDialogActive())
        {
            if (action == GamepadAction.Accept)
            {
                GamepadFocusService.Accept();
            }
            else if (action == GamepadAction.Back)
            {
                // B leva o seletor para os botões de ação (Salvar/Cancelar) antes de fechar —
                // fechar de primeira descartaria em silêncio o que foi ajustado.
                var dialog = GamepadFocusService.ActiveWindow();
                if (dialog is not null && !GamepadFocusService.FocusDialogActions(dialog))
                    dialog.Close();
            }
            return;
        }

        switch (action)
        {
            case GamepadAction.Accept:
                GamepadFocusService.Accept();
                break;

            // B sai da página e volta para a navegação lateral. As páginas do app aplicam as
            // mudanças na hora (não têm botão Salvar), então "voltar" aqui é subir um nível.
            case GamepadAction.Back:
                FocusCurrentNavButton();
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
                RequestEnterGameHub();
                break;
        }
    }

    /// <summary>
    /// Devolve o foco ao item da navegação lateral correspondente à página aberta — o "voltar" das
    /// telas do app, que não têm barra de ações própria.
    /// </summary>
    private void FocusCurrentNavButton()
    {
        if (NavPanel.Visibility != Visibility.Visible) return;

        if (ReferenceEquals(ContentFrame.Content, _inputLabPage))
        {
            LatencyButton.Focus();
            return;
        }

        int index = System.Array.FindIndex(_navMap, m => ReferenceEquals(m.Page, ContentFrame.Content));
        (index < 0 ? DashboardButton : _navMap[index].Item).Focus();
    }

    /// <summary>Percorre as categorias da navegação lateral com os ombros do controle.</summary>
    private void CycleCategory(int delta)
    {
        // Sobre e Doar ficam fora do ciclo: são páginas informativas no fim da lista.
        var cycle = _navMap.Where(m => !ReferenceEquals(m.Page, _aboutPage) && !ReferenceEquals(m.Page, _donatePage)).ToArray();

        object? content = ReferenceEquals(ContentFrame.Content, _inputLabPage) ? _latencyPage : ContentFrame.Content;
        int current = System.Array.FindIndex(cycle, m => ReferenceEquals(m.Page, content));
        if (current < 0) current = 0;

        int next = (current + delta + cycle.Length) % cycle.Length;
        NavigateTo(cycle[next].Page, cycle[next].Item);
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
    private void NavigateTo(System.Windows.Controls.Page page, Controls.NavItem navButton)
    {
        // Clicar de novo na aba já aberta não recarrega nem reanima a página.
        if (!ReferenceEquals(ContentFrame.Content, page))
            ContentFrame.Navigate(page);
        SetActiveNav(navButton);
    }

    // Marca o item ativo (realce + ícone preenchido) e desliza a barra de acento até ele.
    private void SetActiveNav(Controls.NavItem active)
    {
        foreach (var item in _navButtons)
            item.IsActive = ReferenceEquals(item, active);

        bool changed = !ReferenceEquals(_activeNav, active);
        _activeNav = active;

        // O layout do item pode ainda não existir (primeira navegação, volta do GameHub).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            () => MoveSelectionPill(animate: changed && IsLoaded));
    }

    private void MoveSelectionPill(bool animate)
    {
        var active = _activeNav;
        if (active is null || !active.IsVisible || NavHost.ActualHeight <= 0)
        {
            NavSelectionPill.BeginAnimation(UIElement.OpacityProperty, null);
            NavSelectionPill.Opacity = 0;
            return;
        }

        double y = active.TranslatePoint(new Point(0, 0), NavHost).Y + (active.ActualHeight - NavSelectionPill.Height) / 2;
        Animations.SlideIndicator(NavSelectionPill, NavPillTransform, y, animate);
    }

    private void UpdateVersionText() =>
        VersionText.Text = Localization.Loc.F("Shell_Version", AppInfoService.Version);

    private void UpdateRecordingBadge(ViewModels.RecordingViewModel recording)
    {
        RecordingButton.Badge = recording.IsRecording || recording.IsPaused
            ? Localization.Loc.S("Shell_RecBadge")
            : null;
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
            // Texto formatado (Run, Hyperlink) não faz parte da árvore visual: VisualTreeHelper
            // lançava exceção ao clicar nele. Esses sobem pela árvore lógica.
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
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
            Animations.PageEnter(content);
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_dashboardPage, DashboardButton);

    private void OptimizationButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_optimizationPage, OptimizationButton);

    private void HealthButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_healthPage, HealthButton);

    private void LatencyButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_latencyPage, LatencyButton);

    public void NavigateToInputLab()
    {
        ContentFrame.Navigate(_inputLabPage);
        SetActiveNav(LatencyButton);
    }

    public void NavigateToLatency() => NavigateTo(_latencyPage, LatencyButton);

    private void FpsInGameButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_fpsInGamePage, FpsInGameButton);

    private void BenchmarkButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_benchmarkPage, BenchmarkButton);

    private void SoundButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_soundPage, SoundButton);

    private void RecordingButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_recordingPage, RecordingButton);

    /// <summary>Abre a categoria Gravação (menu flutuante e erros de gravação pedem isso).</summary>
    public void NavigateToRecording()
    {
        if (_inGameHub) ExitGameHub();
        NavigateTo(_recordingPage, RecordingButton);
    }

    private void GameHubButton_Click(object sender, RoutedEventArgs e) => RequestEnterGameHub();

    private bool _confirmingGameHub;

    /// <summary>
    /// Pede confirmação antes de entrar no GameHub. O hub muda bastante a janela (tela cheia,
    /// some a navegação), então um clique por engano na barra lateral ou no botão View do
    /// controle não deve levar o usuário para lá sem querer. A abertura automática ao iniciar
    /// (Configurações ▸ GameHub) é uma escolha explícita e continua sem perguntar.
    /// </summary>
    public async void RequestEnterGameHub()
    {
        if (_inGameHub || _confirmingGameHub) return;
        _confirmingGameHub = true;
        try
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = Localization.Loc.S("Hub_ConfirmTitle"),
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = Localization.Loc.S("Hub_ConfirmBody"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380,
                },
                PrimaryButtonText = Localization.Loc.S("Hub_ConfirmEnter"),
                PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                CloseButtonText = Localization.Loc.S("Hub_ConfirmCancel"),
                IsSecondaryButtonEnabled = false,
            };

            var result = await box.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                EnterGameHub();
        }
        catch
        {
            // Se o diálogo não puder abrir, não deixa o usuário preso fora do hub.
            EnterGameHub();
        }
        finally
        {
            _confirmingGameHub = false;
        }
    }

    // =====================================================================================
    //  Modo GameHub
    // =====================================================================================

    /// <summary>Estado da janela antes de entrar no GameHub, para devolvê-lo ao sair.</summary>
    private WindowState _stateBeforeHub = WindowState.Normal;
    private bool _inGameHub;
    private bool _gamepadSuspendedForMinimize;
    private bool _gamepadWasActiveBeforeMinimize;

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
            SetContentSurfaceChrome(false);
            // A barra de título sai de cena: o hub desenha o próprio cromo, e a janela continua
            // arrastável pela área superior dele.
            AppTitleBar.Visibility = Visibility.Collapsed;
            TitleRow.Height = new GridLength(0);
        }

        _inGameHub = true;
        NavigateTo(_gameHubPage, GameHubButton);
    }

    /// <summary>
    /// A camada arredondada da área de conteúdo só faz sentido ao lado da navegação lateral. No
    /// GameHub imersivo o hub ocupa a janela inteira, sem borda nem véu por cima da arte do jogo.
    /// </summary>
    private void SetContentSurfaceChrome(bool visible)
    {
        if (visible)
        {
            ContentSurface.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
            ContentSurface.BorderThickness = new Thickness(1, 1, 0, 0);
            ContentSurface.CornerRadius = new CornerRadius(12, 0, 0, 0);
            AmbientGlow.Visibility = Visibility.Visible;
        }
        else
        {
            ContentSurface.Background = Brushes.Transparent;
            ContentSurface.BorderThickness = new Thickness(0);
            ContentSurface.CornerRadius = new CornerRadius(0);
            AmbientGlow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Esc abre (e fecha) o menu lateral do GameHub. O handler vive na janela, e não só na página,
    /// porque <c>PreviewKeyDown</c> de uma Page só dispara com o foco de teclado dentro da árvore
    /// dela — ao entrar no hub, ou ao voltar de uma janela filha, o foco fica na janela e a tecla
    /// nunca chegava à página.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_inGameHub || e.Key != Key.Escape || e.Handled) return;

        _gameHubPage.ToggleMenuFromKeyboard();
        e.Handled = true;
    }

    /// <summary>Sai do GameHub e devolve a janela ao Pulse1x normal.</summary>
    public void ExitGameHub()
    {
        if (!_inGameHub) return;
        _inGameHub = false;

        _gameHubPage.DetachGamepad();

        NavColumn.Width = new GridLength(236);
        NavPanel.Visibility = Visibility.Visible;
        SetContentSurfaceChrome(true);
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

    private bool _pagePausedForMinimize;

    /// <summary>
    /// Pausa (ou retoma) as leituras periódicas da página aberta. Minimizar não muda o IsVisible
    /// das páginas, então o Dashboard (sensores a cada segundo), a Latência (pings) e os
    /// medidores do Som continuavam rodando com a janela na barra de tarefas.
    /// </summary>
    private void SetCurrentPageActive(bool active)
    {
        if (_inGameHub) return; // o hub tem o próprio controle de atividade
        switch ((ContentFrame.Content as FrameworkElement)?.DataContext)
        {
            case ViewModels.DashboardViewModel dashboard: dashboard.SetActive(active); break;
            case ViewModels.LatencyViewModel latency: latency.SetActive(active); break;
            case ViewModels.OptimizationViewModel optimization: optimization.SetActive(active); break;
            case ViewModels.SoundViewModel sound:
                if (active) sound.OnShown(); else sound.OnHidden();
                break;
        }
    }

    private void FluentWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // O DispatcherTimer do controle pode ser pausado pelo Windows ao minimizar. Paramos
            // a leitura de propósito (limpando botões/analógico presos) e a religamos ao voltar.
            _gamepadWasActiveBeforeMinimize = _gamepad?.IsActive == true;
            _gamepad?.SetActive(false);
            _gamepadSuspendedForMinimize = true;

            SetCurrentPageActive(false);
            _pagePausedForMinimize = true;

            // O "-" minimiza para a barra de tarefas, como em qualquer programa. A bandeja fica só
            // para o "X" (ver FluentWindow_Closing): antes o "-" também escondia a janela na
            // bandeja e, sem nada na barra de tarefas, parecia que o Pulse1x tinha fechado.
            return;
        }

        if (_pagePausedForMinimize)
        {
            _pagePausedForMinimize = false;
            SetCurrentPageActive(true);
        }

        if (_gamepadSuspendedForMinimize)
        {
            _gamepadSuspendedForMinimize = false;
            _gamepad?.SetActive(_gamepadWasActiveBeforeMinimize);

            // Restaurar a janela também pode ter removido o foco visual. Devolvemos-o à zona
            // atual do hub (grade, menu ou teclado) antes da próxima entrada do controle.
            if (_inGameHub) _gameHubPage.RestoreGamepadFocus();
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
