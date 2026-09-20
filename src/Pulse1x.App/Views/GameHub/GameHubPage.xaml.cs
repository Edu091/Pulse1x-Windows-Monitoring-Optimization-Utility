using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.Services.Profiles;
using Pulse1x.App.ViewModels.GameHub;

namespace Pulse1x.App.Views.GameHub;

/// <summary>
/// Tela do GameHub. O code-behind cuida do que é puramente visual ou de janela: o cruzamento suave
/// entre os fundos ao trocar de jogo, o parallax, o carregamento das capas conforme os cartões
/// aparecem e a abertura das janelas auxiliares. A lógica de biblioteca e de perfis fica na ViewModel.
/// </summary>
public partial class GameHubPage : Page
{
    private readonly GameHubViewModel _viewModel;
    private readonly GameLibraryService _library;
    private readonly ProfileStoreService _profiles;
    private readonly GameArtService _art;
    private readonly PowerPlanService _power;
    private readonly OemVendorService _oem;
    private readonly AudioService _audio;
    private readonly DisplayService _display;
    private readonly SettingsService _settings;
    private readonly PlayMetricsService _metrics;

    /// <summary>Pedido de sair do modo GameHub, atendido pela janela principal.</summary>
    public event Action? ExitRequested;

    public GameHubPage(
        GameHubViewModel viewModel,
        GameLibraryService library,
        ProfileStoreService profiles,
        GameArtService art,
        PowerPlanService power,
        OemVendorService oem,
        AudioService audio,
        DisplayService display,
        SettingsService settings,
        PlayMetricsService metrics)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _library = library;
        _profiles = profiles;
        _art = art;
        _power = power;
        _oem = oem;
        _audio = audio;
        _display = display;
        _settings = settings;
        _metrics = metrics;

        DataContext = viewModel;

        viewModel.ScrollToRequested += card => LibraryList.ScrollIntoView(card);
        viewModel.EditGameRequested += OpenGameEditor;
        viewModel.EditProfileRequested += OpenProfileEditor;
        viewModel.ManageEmulatorsRequested += OpenAddGames;
        viewModel.ChangeArtRequested += (entry, _) => OpenCoverPicker(entry);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyHubSettings();

        // O controle e a busca de capas só trabalham com a página visível — mesmo padrão das
        // demais categorias do Pulse1x.
        IsVisibleChanged += (_, e) =>
        {
            bool visible = (bool)e.NewValue;
            _viewModel.SetActive(visible);
            if (visible)
            {
                ApplyHubSettings();
                RunAutoScan();
                StartLaunchers();
                PlayOpenAnimation();
            }
        };
    }

    // =====================================================================================
    //  Entrada do controle (dono único)
    // =====================================================================================

    private readonly HubInputRouter _router = new();
    private GamepadService? _gamepad;

    /// <summary>
    /// Assume a entrada do controle enquanto o GameHub está aberto. É o ÚNICO ponto do hub que
    /// escuta o controle: antes, a ViewModel e a janela principal também escutavam, e os três
    /// reagiam ao mesmo toque — daí a navegação "sumir" e ações dispararem fora de contexto.
    /// </summary>
    public void AttachGamepad(GamepadService gamepad)
    {
        // Em modo não imersivo o botão GameHub continua acessível. Clicá-lo novamente não pode
        // inscrever os handlers outra vez: dois handlers para o mesmo toque fazem o seletor pular
        // (ou aparentemente inverter, quando um movimento desfaz o outro).
        if (ReferenceEquals(_gamepad, gamepad)) return;
        DetachGamepad();

        _gamepad = gamepad;
        gamepad.Navigate += OnGamepadNavigate;
        gamepad.Action += OnGamepadAction;
        _router.ZoneChanged += OnZoneChanged;
    }

    public void DetachGamepad()
    {
        if (_gamepad is null) return;
        _gamepad.Navigate -= OnGamepadNavigate;
        _gamepad.Action -= OnGamepadAction;
        _router.ZoneChanged -= OnZoneChanged;
        _gamepad = null;
    }

    /// <summary>Move o foco visual ao trocar de zona, para o usuário ver onde está.</summary>
    private void OnZoneChanged(HubZone zone)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (zone)
            {
                case HubZone.Grid:
                    FocusLibrary();
                    break;
                case HubZone.Chrome:
                    // Entra pelas abas, que é o primeiro grupo da faixa superior.
                    TabAll.Focus();
                    break;
                case HubZone.Hero:
                    PlayButton.Focus();
                    break;
                case HubZone.Menu:
                    GamepadFocusService.FocusFirst(MenuList);
                    break;
                case HubZone.Keyboard:
                    VirtualKeyboard.FocusFirstKey();
                    break;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnGamepadNavigate(GamepadDirection direction)
    {
        if (!IsVisible) return;

        // Com um diálogo aberto (Adicionar jogos, Perfil, Estatísticas...), a entrada pertence a
        // ele: navegamos pelo foco da janela ativa e não mexemos na biblioteca atrás.
        if (GamepadFocusService.IsDialogActive())
        {
            var dialog = GamepadFocusService.ActiveWindow();
            GamepadFocusService.EnsureFocusInActiveWindow();
            bool moved = dialog is StatisticsWindow statistics
                ? statistics.MoveGamepadFocus(direction)
                : GamepadFocusService.Move(direction);
            if (moved) _viewModel.PlaySound(HubSound.Navigate);
            return;
        }

        // O teclado e o menu são modais de verdade: nunca entregamos a navegação espacial do WPF
        // para a página por baixo, pois ela pode encontrar um controle externo ao painel.
        if (_router.Zone == HubZone.Keyboard)
        {
            if (VirtualKeyboard.Move(direction)) _viewModel.PlaySound(HubSound.Navigate);
            return;
        }

        if (_router.Zone == HubZone.Menu)
        {
            if (MoveMenuSelection(direction)) _viewModel.PlaySound(HubSound.Navigate);
            return;
        }

        // As zonas de foco (cromo e destaque) usam o foco espacial do WPF.
        if (_router.Zone != HubZone.Grid)
        {
            // Antes de mover o foco, vemos se o movimento deve trocar de zona.
            var exit = _router.ResolveVerticalExit(direction, atGridTopRow: false,
                hasSelection: _viewModel.SelectedGame is not null);

            if (exit is HubZone target && !_router.IsModal)
            {
                _router.SetZone(target);
                _viewModel.PlaySound(HubSound.Navigate);
                return;
            }

            if (GamepadFocusService.Move(direction))
                _viewModel.PlaySound(HubSound.Navigate);
            return;
        }

        // Na grade: subir na primeira linha sai para o destaque/cromo.
        if (direction == GamepadDirection.Up && _viewModel.IsAtGridTopRow)
        {
            var exit = _router.ResolveVerticalExit(direction, atGridTopRow: true,
                hasSelection: _viewModel.SelectedGame is not null);
            if (exit is HubZone target)
            {
                _router.SetZone(target);
                _viewModel.PlaySound(HubSound.Navigate);
            }
            return;
        }

        if (_viewModel.MoveSelection(direction))
            _viewModel.PlaySound(HubSound.Navigate);
    }

    /// <summary>Restaura o foco da zona atual depois de a janela sair do estado minimizado.</summary>
    public void RestoreGamepadFocus()
    {
        if (!IsVisible) return;
        OnZoneChanged(_router.Zone);
    }

    /// <summary>
    /// O menu lateral é uma lista vertical. Controlar o índice explicitamente impede que uma
    /// diagonal do analógico faça o WPF saltar para o cromo ou para a biblioteca atrás do overlay.
    /// </summary>
    private bool MoveMenuSelection(GamepadDirection direction)
    {
        // Não há navegação horizontal no menu; ignorá-la também neutraliza a segunda componente
        // de um analógico levemente diagonal.
        if (direction is GamepadDirection.Left or GamepadDirection.Right) return false;

        var items = FindDescendants<Button>(MenuList)
            .Where(button => button.IsVisible && button.IsEnabled)
            .ToList();
        if (items.Count == 0) return false;

        int current = items.FindIndex(button => button.IsKeyboardFocused);
        if (current < 0)
        {
            items[0].Focus();
            return true;
        }

        int next = current + (direction == GamepadDirection.Up ? -1 : 1);
        if (next < 0 || next >= items.Count) return false;

        return items[next].Focus();
    }

    /// <summary>
    /// Comportamento do B num diálogo: primeiro leva o seletor para a barra de ações
    /// (Salvar / Cancelar) e só fecha a janela se já estivermos lá. Evita descartar em silêncio o
    /// que o usuário ajustou numa tela de configuração.
    /// </summary>
    private static void CloseOrFocusActions(Window? window)
    {
        if (window is null) return;
        if (GamepadFocusService.FocusDialogActions(window)) return;
        window.Close();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private void OnGamepadAction(GamepadAction action)
    {
        if (!IsVisible) return;

        // ---- Diálogo aberto por cima: A aciona, B fecha ----
        if (GamepadFocusService.IsDialogActive())
        {
            switch (action)
            {
                case GamepadAction.Accept:
                    _viewModel.PlaySound(HubSound.Confirm);
                    GamepadFocusService.Accept();
                    break;
                case GamepadAction.Back:
                    _viewModel.PlaySound(HubSound.Back);
                    CloseOrFocusActions(GamepadFocusService.ActiveWindow());
                    break;
            }
            return;
        }

        // ---- Teclado virtual: modal, B fecha e A digita ----
        if (_router.Zone == HubZone.Keyboard)
        {
            switch (action)
            {
                case GamepadAction.Accept:
                    // Se o layout ainda estiver terminando, A só recupera o foco da primeira
                    // tecla; jamais aciona o botão que estava selecionado atrás do teclado.
                    if (!VirtualKeyboard.HasFocusInside) VirtualKeyboard.FocusFirstKey();
                    else GamepadFocusService.Accept();
                    break;
                case GamepadAction.Back:
                    _viewModel.PlaySound(HubSound.Back);
                    CloseKeyboard();
                    break;
            }
            return;
        }

        // ---- Menu lateral: modal ----
        if (_router.Zone == HubZone.Menu)
        {
            switch (action)
            {
                case GamepadAction.Accept:
                    // Mesmo princípio do teclado: o overlay nunca pode confirmar uma ação do hub
                    // que ficou com foco antes de o menu aparecer.
                    if (!GamepadFocusService.IsFocusInside(MenuList))
                    {
                        GamepadFocusService.FocusFirst(MenuList);
                    }
                    else
                    {
                        _viewModel.PlaySound(HubSound.Confirm);
                        GamepadFocusService.Accept();
                    }
                    break;
                case GamepadAction.Back:
                case GamepadAction.Menu:
                    _viewModel.PlaySound(HubSound.Back);
                    CloseMenu();
                    break;
            }
            return;
        }

        switch (action)
        {
            case GamepadAction.Accept:
                // Na grade, A joga. Nas outras zonas, A aciona o botão em foco.
                if (_router.Zone == HubZone.Grid)
                {
                    LaunchSelected();
                }
                else
                {
                    _viewModel.PlaySound(HubSound.Confirm);
                    GamepadFocusService.Accept();
                }
                break;

            case GamepadAction.Back:
                _viewModel.PlaySound(HubSound.Back);
                // Fora da grade, B volta para a grade; na grade, B limpa busca/filtros.
                if (_router.Zone != HubZone.Grid) _router.SetZone(HubZone.Grid);
                else _viewModel.ClearFiltersStep();
                break;

            case GamepadAction.Favorite:
                _viewModel.PlaySound(HubSound.Confirm);
                _viewModel.ToggleFavoriteSelected();
                break;

            // Y abre a busca com o teclado virtual — botão digital dedicado, sem gatilho analógico.
            case GamepadAction.Search:
                _viewModel.PlaySound(HubSound.Confirm);
                OpenKeyboard();
                break;

            case GamepadAction.NextTab:
                _viewModel.CycleSectionPublic(1);
                break;
            case GamepadAction.PreviousTab:
                _viewModel.CycleSectionPublic(-1);
                break;

            case GamepadAction.Menu:
                _viewModel.PlaySound(HubSound.Confirm);
                OpenMenu();
                break;

            case GamepadAction.Details:
                _viewModel.PlaySound(HubSound.Confirm);
                // O editor consulta dispositivos e planos de energia. Agendá-lo depois deste
                // evento deixa o ciclo do controle terminar antes de abrir a janela modal, sem a
                // pequena travada que acontecia ao apertar Start.
                Dispatcher.BeginInvoke(new Action(_viewModel.OpenSelectedProfile),
                    System.Windows.Threading.DispatcherPriority.Background);
                break;

            // L2 sobe o seletor para as ações do jogo. A partir de Jogar, direita permite chegar
            // a Perfil, favorito, capa e editar jogo sem precisar sair da biblioteca.
            case GamepadAction.GameActions:
                if (_viewModel.SelectedGame is not null)
                {
                    _router.SetZone(HubZone.Hero);
                    _viewModel.PlaySound(HubSound.Navigate);
                }
                break;
        }
    }

    /// <summary>
    /// Varredura automática ao abrir o GameHub, quando ligada nas Configurações. Roda em segundo
    /// plano e sem diálogo nenhum: se achar algo novo, a biblioteca simplesmente cresce.
    /// </summary>
    private bool _autoScanDone;

    private void RunAutoScan()
    {
        if (_autoScanDone) return;
        var hub = _settings.Current.GameHub;
        if (!hub.ScanOnOpen) return;

        _autoScanDone = true;
        if (_viewModel.ScanCommand.CanExecute(null)) _viewModel.ScanCommand.Execute(null);
    }

    /// <summary>
    /// Sobe os launchers escolhidos nas Configurações, se ainda não estiverem rodando. Roda uma vez
    /// por sessão e fora da thread de interface — abrir a Steam não pode travar a biblioteca.
    /// </summary>
    private bool _launchersStarted;

    private void StartLaunchers()
    {
        if (_launchersStarted) return;
        var wanted = _settings.Current.GameHub.AutoStartLaunchers;
        if (wanted.Count == 0) return;

        _launchersStarted = true;

        Task.Run(() =>
        {
            try
            {
                var kinds = wanted
                    .Select(name => Enum.TryParse<LauncherKind>(name, out var kind) ? kind : (LauncherKind?)null)
                    .Where(k => k is not null)
                    .Select(k => k!.Value);

                new LauncherStartupService().StartMissing(kinds);
            }
            catch { /* abrir launcher é conveniência: falhar aqui não atrapalha o hub */ }
        });
    }

    // =====================================================================================
    //  Animações de entrada
    // =====================================================================================

    /// <summary>
    /// Animação de abertura do hub: o conteúdo sobe e aparece, e a grade entra logo atrás. É o que
    /// dá a sensação de "ligar o console" em vez de simplesmente trocar de tela.
    /// </summary>
    public void PlayOpenAnimation()
    {
        if (!AnimationSettings.Enabled) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(420)));

        HeroContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        var slide = new TranslateTransform();
        LibraryList.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(AnimationSettings.ScaleOffset(40), 0, duration) { EasingFunction = ease });
        LibraryList.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(520))))
            { EasingFunction = ease });
    }

    /// <summary>
    /// Animação de "entrando no jogo": a capa em destaque cresce e a tela clareia por um instante,
    /// dando um retorno visual imediato ao apertar Jogar — o jogo leva segundos para aparecer, e
    /// sem isso o clique parece não ter feito nada.
    /// </summary>
    private void PlayLaunchAnimation()
    {
        if (!AnimationSettings.Enabled) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(520)));

        var scale = new ScaleTransform(1, 1);
        HeroContent.RenderTransformOrigin = new Point(0, 0.5);
        HeroContent.RenderTransform = scale;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.04, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.04, duration) { EasingFunction = ease });

        // Clarão curto por cima de tudo.
        LaunchFlash.Visibility = Visibility.Visible;
        var flash = new DoubleAnimation(0, 0.5, AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(160))))
        {
            AutoReverse = true,
            EasingFunction = ease,
        };
        flash.Completed += (_, _) => LaunchFlash.Visibility = Visibility.Collapsed;
        LaunchFlash.BeginAnimation(OpacityProperty, flash);
    }

    /// <summary>
    /// A caixa de busca recebeu o foco. O teclado virtual NÃO abre aqui: quem está com o teclado
    /// físico simplesmente digita, e abrir um teclado na tela por cima só atrapalharia. O teclado
    /// virtual é aberto de propósito, pelo botão Y do controle.
    /// </summary>
    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Clicar na busca com o mouse move a zona de entrada para o cromo, para o controle
        // continuar de onde o usuário está olhando.
        if (_router.Zone == HubZone.Grid) _router.SetZone(HubZone.Chrome);
    }

    /// <summary>Repassa à ViewModel as preferências próprias do GameHub.</summary>
    private void ApplyHubSettings()
    {
        var hub = _settings.Current.GameHub;
        _viewModel.ApplyCardSize(hub.CardSize);
        _viewModel.ShowTitles = hub.ShowTitlesOnCards;
    }

    // =====================================================================================
    //  Transição do destaque
    // =====================================================================================

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameHubViewModel.HeroImage))
            AnimateHero();
    }

    /// <summary>
    /// Entrada do novo fundo: sobe a opacidade enquanto desliza de leve para o lugar (parallax).
    /// Com as animações desligadas, o fundo simplesmente troca.
    /// </summary>
    private void AnimateHero()
    {
        if (!AnimationSettings.Enabled)
        {
            HeroImageLayer.BeginAnimation(OpacityProperty, null);
            HeroImageLayer.Opacity = 0.62;
            HeroParallax.X = 0;
            return;
        }

        var duration = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(460)));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        HeroImageLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 0.62, duration) { EasingFunction = ease });

        double offset = AnimationSettings.ScaleOffset(34);
        HeroParallax.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });

        HeroContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.3, 1, AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(320))))
            { EasingFunction = ease });
    }

    // =====================================================================================
    //  Biblioteca
    // =====================================================================================

    /// <summary>Carrega a capa quando o cartão entra na árvore visual, e não todas de uma vez.</summary>
    private void GameCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GameCardViewModel card })
            card.RequestCover((int)Math.Round(_viewModel.CardWidth * 1.3));
    }

    /// <summary>
    /// Informa à ViewModel quantos cartões cabem por linha. Sem isso, o "para cima"/"para baixo" do
    /// controle não teria como saber quantas posições pular.
    /// </summary>
    private void LibraryList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double cardWidth = _viewModel.CardWidth + 16;   // largura do cartão + espaçamento
        double available = LibraryList.ActualWidth - LibraryList.Padding.Left - LibraryList.Padding.Right;
        _viewModel.ColumnsPerRow = Math.Max(1, (int)(available / cardWidth));
    }

    private void LibraryList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LaunchSelected();

    private void Play_Click(object sender, RoutedEventArgs e) => LaunchSelected();

    /// <summary>Inicia o jogo em destaque, com o retorno visual de "entrando no jogo".</summary>
    private void LaunchSelected()
    {
        if (!_viewModel.PlayCommand.CanExecute(null)) return;
        PlayLaunchAnimation();
        _viewModel.PlayCommand.Execute(null);
    }

    private void ExitHub_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    // =====================================================================================
    //  Menu lateral (botão View)
    // =====================================================================================

    private HubMenuViewModel? _menu;

    private void OpenMenu_Click(object sender, RoutedEventArgs e) => OpenMenu();

    /// <summary>Abre o menu lateral deslizando da esquerda, no estilo do Big Picture.</summary>
    public void OpenMenu()
    {
        if (MenuOverlay.Visibility == Visibility.Visible) return;

        _menu = new HubMenuViewModel(_viewModel.SelectedGame?.Name);
        _menu.ActionChosen += OnMenuAction;
        _menu.CloseRequested += CloseMenu;

        MenuList.ItemsSource = _menu.Items;
        MenuSubtitle.Text = _viewModel.SelectedGame?.Name ?? "";
        MenuOverlay.Visibility = Visibility.Visible;
        _router.EnterModal(HubZone.Menu);

        if (AnimationSettings.Enabled)
        {
            MenuSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(-320, 0, AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(260))))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            MenuSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            MenuSlide.X = 0;
        }

    }

    public void CloseMenu()
    {
        if (MenuOverlay.Visibility != Visibility.Visible) return;

        if (AnimationSettings.Enabled)
        {
            var animation = new DoubleAnimation(0, -320,
                AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(200))))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            animation.Completed += (_, _) => MenuOverlay.Visibility = Visibility.Collapsed;
            MenuSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
        }
        else
        {
            MenuOverlay.Visibility = Visibility.Collapsed;
        }

        _router.ExitModal();
    }

    private void MenuBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseMenu();

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HubMenuItem item })
            _menu?.ChooseCommand.Execute(item);
    }

    private void OnMenuAction(HubMenuAction action)
    {
        switch (action)
        {
            case HubMenuAction.GameProfile:
                if (_viewModel.SelectedGame is not null) OpenProfileEditor(_viewModel.SelectedGame.Entry);
                break;
            case HubMenuAction.AddGames:
                OpenAddGames();
                break;
            case HubMenuAction.Statistics:
                OpenStatistics();
                break;
            case HubMenuAction.ScanGames:
                if (_viewModel.ScanCommand.CanExecute(null)) _viewModel.ScanCommand.Execute(null);
                break;
            case HubMenuAction.FetchCovers:
                if (_viewModel.FetchCoversCommand.CanExecute(null)) _viewModel.FetchCoversCommand.Execute(null);
                break;
            case HubMenuAction.Settings:
                SettingsRequested?.Invoke();
                break;
            case HubMenuAction.ExitHub:
                ExitRequested?.Invoke();
                break;
            case HubMenuAction.Sleep:
            case HubMenuAction.Restart:
            case HubMenuAction.Shutdown:
                HubMenuViewModel.RunPowerAction(action);
                break;
        }
    }

    /// <summary>Pedido de abrir as Configurações do Pulse1x, atendido pela janela principal.</summary>
    public event Action? SettingsRequested;

    // =====================================================================================
    //  Teclado virtual
    // =====================================================================================

    /// <summary>
    /// Abre o teclado virtual para digitar na busca usando o controle.
    ///
    /// Enquanto ele está aberto, a entrada fica presa nele (zona modal): o direcional anda pelas
    /// teclas e não mexe mais na grade atrás. Só ao concluir (Concluir, B ou Enter) o controle volta
    /// para a biblioteca.
    /// </summary>
    public void OpenKeyboard()
    {
        if (KeyboardOverlay.Visibility == Visibility.Visible) return;

        VirtualKeyboard.Text = _viewModel.SearchText;
        VirtualKeyboard.TextChanged -= OnKeyboardTextChanged;
        VirtualKeyboard.TextChanged += OnKeyboardTextChanged;
        VirtualKeyboard.Closed -= CloseKeyboard;
        VirtualKeyboard.Closed += CloseKeyboard;

        KeyboardOverlay.Visibility = Visibility.Visible;

        if (AnimationSettings.Enabled)
            Animations.FadeSlideIn(VirtualKeyboard, fromOffsetY: 24);

        _router.EnterModal(HubZone.Keyboard);
    }

    private void OnKeyboardTextChanged(string text) => _viewModel.SearchText = text;

    /// <summary>
    /// Esc é o atalho de teclado para o menu lateral. Nos overlays ele primeiro respeita o modal:
    /// fecha teclado/menu aberto, sem deixar o foco escapar para a biblioteca de trás.
    ///
    /// Quem escuta a tecla é a janela (<c>MainWindow</c>), não esta página: o PreviewKeyDown de uma
    /// Page só dispara com o foco de teclado dentro da árvore dela, e ao entrar no hub — ou ao
    /// voltar de uma janela filha — o foco fica na janela e o Esc se perdia.
    /// </summary>
    public void ToggleMenuFromKeyboard()
    {
        if (KeyboardOverlay.Visibility == Visibility.Visible) CloseKeyboard();
        else if (MenuOverlay.Visibility == Visibility.Visible) CloseMenu();
        else OpenMenu();
    }

    public void CloseKeyboard()
    {
        if (KeyboardOverlay.Visibility != Visibility.Visible) return;
        KeyboardOverlay.Visibility = Visibility.Collapsed;
        _router.ExitModal();
    }

    /// <summary>Devolve o foco ao item selecionado da biblioteca.</summary>
    private void FocusLibrary()
    {
        if (_viewModel.SelectedGame is null)
        {
            LibraryList.Focus();
            return;
        }

        LibraryList.ScrollIntoView(_viewModel.SelectedGame);
        if (LibraryList.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedGame)
            is System.Windows.Controls.ListBoxItem container)
            container.Focus();
        else
            LibraryList.Focus();
    }

    private void OpenStatistics()
    {
        var window = new StatisticsWindow(new StatisticsViewModel(
            _metrics, _settings, _library.Games, _viewModel.SelectedGame?.Entry.Id))
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
    }

    private void Statistics_Click(object sender, RoutedEventArgs e) => OpenStatistics();

    private void AddGames_Click(object sender, RoutedEventArgs e) => OpenAddGames();

    private void ChangeCover_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGame is not null) OpenCoverPicker(_viewModel.SelectedGame.Entry);
    }

    // =====================================================================================
    //  Janelas auxiliares
    // =====================================================================================

    private void OpenAddGames()
    {
        var viewModel = new AddGamesViewModel(_library, _profiles, _art);
        var window = new AddGamesWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void OpenGameEditor(GameEntry entry)
    {
        var viewModel = new GameEditorViewModel(entry, _library, _profiles, _art);
        var window = new GameEditorWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void OpenProfileEditor(GameEntry entry)
    {
        var viewModel = new ProfileEditorViewModel(entry, _profiles, _library, _power, _oem, _audio, _display);
        var window = new ProfileEditorWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    /// <summary>Abre a escolha de capa (sugestões online + imagem do computador).</summary>
    private void OpenCoverPicker(GameEntry entry)
    {
        var viewModel = new CoverPickerViewModel(entry, _art, _library);
        var window = new CoverPickerWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
