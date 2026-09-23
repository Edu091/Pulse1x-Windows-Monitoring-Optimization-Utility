using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Opção de filtro por plataforma (o "Todos" tem <see cref="Kind"/> nulo).</summary>
public record LauncherFilterOption(LauncherKind? Kind, string Label);

/// <summary>Um perfil na lista de escolha rápida do destaque. Id nulo = "nenhum perfil".</summary>
public record ProfileChoiceViewModel(string? Id, string Name, string Summary, bool IsCurrent);

/// <summary>Opção de ordenação da biblioteca.</summary>
public record SortOption(GameSortMode Mode, string Label);

/// <summary>
/// ViewModel do GameHub: a biblioteca, a seleção em destaque, os filtros, a detecção automática e a
/// ligação com o motor de perfis.
///
/// Como nas outras categorias do Pulse1x, os textos são relidos ao trocar de idioma (assinatura de
/// <see cref="Loc.LanguageChanged"/>) e o trabalho pesado (varredura, busca de capas) roda fora da
/// thread de interface.
/// </summary>
public partial class GameHubViewModel : ObservableObject, IDisposable
{
    private readonly GameLibraryService _library;
    private readonly ProfileStoreService _profiles;
    private readonly GameArtService _art;
    private readonly GameSessionManager _sessions;
    private readonly GamingModeService _gamingMode;
    private readonly ThemeService _theme;
    private readonly GamepadService _gamepad;
    private readonly GameHubSoundService _sounds;
    private readonly HubStatusService? _status;

    /// <summary>Estatísticas de sessão — de onde vem o FPS médio real de cada jogo.</summary>
    public PlayMetricsService? Metrics { get; set; }

    private readonly List<GameCardViewModel> _allCards = new();
    private CancellationTokenSource? _artCts;
    private bool _isPageActive;

    /// <summary>Itens exibidos agora (já filtrados e ordenados).</summary>
    public ObservableCollection<GameCardViewModel> Games { get; } = new();

    public ObservableCollection<LauncherFilterOption> LauncherFilters { get; } = new();
    public ObservableCollection<SortOption> SortOptions { get; } = new();
    public ObservableCollection<string> CategoryFilters { get; } = new();

    // ---- Filtros ----
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private LauncherFilterOption? selectedLauncherFilter;
    [ObservableProperty] private SortOption? selectedSort;
    [ObservableProperty] private string? selectedCategory;
    [ObservableProperty] private bool favoritesOnly;
    [ObservableProperty] private bool recentOnly;

    // ---- Seleção em destaque ----
    [ObservableProperty] private GameCardViewModel? selectedGame;
    [ObservableProperty] private BitmapImage? heroImage;
    [ObservableProperty] private BitmapImage? heroCover;

    // ---- Estado ----
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string scanStatus = "";
    [ObservableProperty] private bool isLaunching;
    [ObservableProperty] private string sessionStatus = "";
    [ObservableProperty] private bool isSessionRunning;
    [ObservableProperty] private bool gamepadConnected;
    [ObservableProperty] private string controllerGlyphs = "Auto";
    [ObservableProperty] private bool cardInputActive = true;

    private ControllerButtonLabels ButtonLabels => ControllerGlyphs switch
    {
        "Xbox" => ControllerButtonLabels.Xbox,
        "PlayStation" => new("Cross", "Circle", "Square", "Triangle", "L1", "R1", "Share", "Options", "L2"),
        _ => _gamepad.ActiveControllerLabels,
    };

    private static string Glyph(string name) => name switch
    {
        "Cross" => "\u00d7", "Circle" => "\u25cb", "Square" => "\u25a1", "Triangle" => "\u25b3",
        "South" => "\u2193", "East" => "\u2192", "West" => "\u2190", "North" => "\u2191",
        _ => name,
    };

    public string AcceptGlyph => Glyph(ButtonLabels.Accept);
    public string BackGlyph => Glyph(ButtonLabels.Back);
    public string FavoriteGlyph => Glyph(ButtonLabels.Favorite);
    public string SearchGlyph => Glyph(ButtonLabels.Search);
    public string MenuGlyph => ButtonLabels.Menu;
    public string ActionsGlyph => ButtonLabels.GameActions;
    private bool HasPlayStationGlyphs => ButtonLabels.Accept == "Cross";
    public string AcceptGlyphBrush => HasPlayStationGlyphs ? "#2563EB" : "#16883F";
    public string BackGlyphBrush => HasPlayStationGlyphs ? "#B84050" : "#DC2626";
    public string FavoriteGlyphBrush => HasPlayStationGlyphs ? "#9D477F" : "#2563EB";
    public string SearchGlyphBrush => HasPlayStationGlyphs ? "#167C68" : "#A77910";
    public string CardAcceptLabel => Loc.S(CardInputActive ? "GH_PlayDoubleTap" : "GH_Confirm");
    public string ControllerMenuHint => Loc.F("GH_MenuHint", MenuGlyph);

    partial void OnControllerGlyphsChanged(string value) => RefreshControllerHints();
    partial void OnCardInputActiveChanged(bool value) => OnPropertyChanged(nameof(CardAcceptLabel));

    private void OnActiveControllerChanged(ControllerIdentity? identity)
    {
        GamepadConnected = identity is not null;
        RefreshControllerHints();
    }

    private void RefreshControllerHints()
    {
        foreach (string property in new[] { nameof(AcceptGlyph), nameof(BackGlyph), nameof(FavoriteGlyph),
            nameof(SearchGlyph), nameof(MenuGlyph), nameof(ActionsGlyph), nameof(AcceptGlyphBrush),
            nameof(BackGlyphBrush), nameof(FavoriteGlyphBrush), nameof(SearchGlyphBrush),
            nameof(ControllerMenuHint), nameof(CardAcceptLabel) }) OnPropertyChanged(property);
    }
    [ObservableProperty] private string emptyMessage = "";

    /// <summary>Desfoque do fundo em destaque, governado pela intensidade global de blur.</summary>
    [ObservableProperty] private double heroBlurRadius = 6;

    // =====================================================================================
    //  Aparência da grade e estado do rodapé
    // =====================================================================================

    /// <summary>Largura/altura do cartão, derivadas do tamanho escolhido nas Configurações.
    /// A proporção 2:3 é a das capas verticais das lojas.</summary>
    [ObservableProperty] private double cardWidth = 168;
    [ObservableProperty] private double cardHeight = 252;

    /// <summary>Mostrar o nome sobre a capa (preferência do GameHub).</summary>
    [ObservableProperty] private bool showTitles = true;

    /// <summary>Aba "Todos": é o estado em que nem Recentes nem Favoritos estão marcados.</summary>
    public bool SectionAll
    {
        get => !RecentOnly && !FavoritesOnly;
        set
        {
            if (!value) return;
            RecentOnly = false;
            FavoritesOnly = false;
            OnPropertyChanged();
        }
    }

    /// <summary>Contagem "exibidos de total" mostrada no rodapé.</summary>
    public string LibraryCountText => Loc.F("GH_LibraryCount", Games.Count, _allCards.Count);

    /// <summary>Uma única linha de estado no rodapé: varredura ou sessão, o que estiver acontecendo.</summary>
    public string FooterStatus => !string.IsNullOrEmpty(SessionStatus) ? SessionStatus : ScanStatus;

    /// <summary>Há algo em andamento (varredura ou preparação de sessão).</summary>
    public bool IsBusyAny => IsScanning || IsLaunching;

    // =====================================================================================
    //  Barra de estado (relógio, bateria, temperatura)
    // =====================================================================================

    [ObservableProperty] private string clockText = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string batteryText = "";
    [ObservableProperty] private string batteryIcon = "";
    [ObservableProperty] private string batteryBrush = "#8B8F98";
    [ObservableProperty] private bool hasBattery;
    [ObservableProperty] private string temperatureText = "";
    [ObservableProperty] private bool hasTemperature;

    // ---- Faixa de telemetria do destaque ----
    // CPU, GPU e RAM são leitura ao vivo do sistema; o FPS é o do próprio jogo em destaque, da
    // última vez que ele rodou, porque medir quadros de um jogo que não está aberto é impossível.
    [ObservableProperty] private string heroCpuText = "";
    [ObservableProperty] private string heroGpuText = "";
    [ObservableProperty] private string heroRamText = "";
    [ObservableProperty] private string heroFpsText = "";

    public bool HasHeroCpu => !string.IsNullOrEmpty(HeroCpuText);
    public bool HasHeroGpu => !string.IsNullOrEmpty(HeroGpuText);
    public bool HasHeroRam => !string.IsNullOrEmpty(HeroRamText);
    public bool HasHeroFps => !string.IsNullOrEmpty(HeroFpsText);

    partial void OnHeroCpuTextChanged(string value) => OnPropertyChanged(nameof(HasHeroCpu));
    partial void OnHeroGpuTextChanged(string value) => OnPropertyChanged(nameof(HasHeroGpu));
    partial void OnHeroRamTextChanged(string value) => OnPropertyChanged(nameof(HasHeroRam));
    partial void OnHeroFpsTextChanged(string value) => OnPropertyChanged(nameof(HasHeroFps));

    private System.Windows.Threading.DispatcherTimer? _statusTimer;

    /// <summary>
    /// Atualiza relógio, bateria e temperatura a cada 5 segundos enquanto o hub está visível. É
    /// barato e para junto com a página — o Modo Gaming também o desliga durante a partida.
    /// </summary>
    private void StartStatusTimer()
    {
        if (_status is null) return;

        _statusTimer ??= new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };

        _statusTimer.Tick -= OnStatusTick;
        _statusTimer.Tick += OnStatusTick;
        _statusTimer.Start();
        OnStatusTick(null, EventArgs.Empty);
    }

    private void StopStatusTimer() => _statusTimer?.Stop();

    private void OnStatusTick(object? sender, EventArgs e)
    {
        if (_status is null) return;

        var status = _status.Read();

        ClockText = status.Clock;

        HasBattery = status.HasBattery;
        if (status.HasBattery && status.BatteryPercent is int percent)
        {
            BatteryText = $"{percent}%";
            BatteryIcon = status.Charging ? "" : "";
            // Vermelho abaixo de 20% sem carregar: é a única situação em que o número importa.
            BatteryBrush = !status.Charging && percent <= 20 ? "#DC2626" : "#8B8F98";
        }

        var hottest = new[] { status.CpuTemperature, status.GpuTemperature }
            .Where(t => t is > 0)
            .DefaultIfEmpty(null)
            .Max();

        HasTemperature = hottest is not null;
        if (hottest is double temp) TemperatureText = $"{temp:0}°C";

        HeroCpuText = status.CpuTemperature is > 0 ? $"{status.CpuTemperature:0}°C" : "";
        HeroGpuText = status.GpuTemperature is > 0 ? $"{status.GpuTemperature:0}°C" : "";
        HeroRamText = status.RamUsedGb is > 0 ? $"{status.RamUsedGb:0.0} GB" : "";
    }

    /// <summary>
    /// FPS médio que o jogo em destaque alcançou da última vez. Não é leitura ao vivo — fora de
    /// uma partida não há quadros para medir —, e sim o que ficou registrado nas estatísticas.
    /// </summary>
    private void UpdateHeroFps()
    {
        var entry = SelectedGame?.Entry;
        if (entry is null || Metrics is null) { HeroFpsText = ""; return; }

        try
        {
            double? fps = Metrics.StatsFor(entry.Id)?.AverageFps;
            HeroFpsText = fps is > 0 ? $"{fps:0} FPS" : "";
        }
        catch { HeroFpsText = ""; }
    }

    /// <summary>Pedido para a tela rolar até o item selecionado (navegação por controle/teclado).</summary>
    public event Action<GameCardViewModel>? ScrollToRequested;

    /// <summary>Pedidos de abertura de janelas auxiliares, atendidos pelo code-behind da página.</summary>
    public event Action<GameEntry>? EditGameRequested;
    public event Action<GameEntry>? EditProfileRequested;
    public event Action? ManageEmulatorsRequested;
    public event Action<GameEntry, ArtKind>? ChangeArtRequested;

    public GameHubViewModel(
        GameLibraryService library,
        ProfileStoreService profiles,
        GameArtService art,
        GameSessionManager sessions,
        GamingModeService gamingMode,
        ThemeService theme,
        GamepadService gamepad,
        GameHubSoundService sounds,
        HubStatusService? status = null)
    {
        _library = library;
        _profiles = profiles;
        _art = art;
        _sessions = sessions;
        _gamingMode = gamingMode;
        _theme = theme;
        _gamepad = gamepad;
        _sounds = sounds;
        _status = status;

        BuildFilterOptions();

        _library.Changed += OnLibraryChanged;
        _sessions.SessionStarted += OnSessionStarted;
        _sessions.SessionEnded += OnSessionEnded;
        _sessions.StepReported += OnStepReported;
        // A entrada do controle é roteada pela PÁGINA (HubInputRouter), que sabe em que zona o
        // usuário está. A ViewModel só expõe as ações; assim não há dois donos do mesmo evento.
        _gamepad.ActiveControllerChanged += OnActiveControllerChanged;
        GamepadConnected = _gamepad.IsConnected;
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        // O desfoque do destaque segue o mesmo ajuste de intensidade de blur do app inteiro.
        _theme.AppearanceChanged += UpdateBlurFromTheme;
        UpdateBlurFromTheme();

        ReloadLibrary();
    }

    // =====================================================================================
    //  Ciclo de vida da página
    // =====================================================================================

    /// <summary>
    /// Liga/desliga o que só faz sentido com o GameHub visível: a leitura do controle e a busca de
    /// capas. É o mesmo padrão das outras categorias (Latência, Dashboard).
    /// </summary>
    public void SetActive(bool active)
    {
        _isPageActive = active;
        _gamepad.SetActive(active && !_gamingMode.IsActive);

        if (active)
        {
            StartStatusTimer();
            _ = FetchArtAsync();
        }
        else
        {
            StopStatusTimer();
            _artCts?.Cancel();
        }
    }

    // =====================================================================================
    //  Biblioteca
    // =====================================================================================

    private void ReloadLibrary()
    {
        string? previousId = SelectedGame?.Id;

        _allCards.Clear();
        foreach (var entry in _library.Games)
            _allCards.Add(new GameCardViewModel(entry, _art));

        RefreshCategoryFilters();
        ApplyFilters();

        // Mantém a seleção do usuário quando possível; senão, destaca o primeiro item.
        SelectedGame = Games.FirstOrDefault(c => c.Id == previousId) ?? Games.FirstOrDefault();
    }

    private void OnLibraryChanged() =>
        System.Windows.Application.Current?.Dispatcher.Invoke(ReloadLibrary);

    /// <summary>Aplica busca, filtros e ordenação sobre a biblioteca inteira.</summary>
    private void ApplyFilters()
    {
        IEnumerable<GameCardViewModel> query = _allCards.Where(c => !c.Entry.IsHidden);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim();
            query = query.Where(c => c.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        if (SelectedLauncherFilter?.Kind is LauncherKind kind)
            query = query.Where(c => c.Entry.Launcher == kind);

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != Loc.S("GH_AllCategories"))
            query = query.Where(c => string.Equals(c.Entry.Category, SelectedCategory, StringComparison.CurrentCultureIgnoreCase));

        if (FavoritesOnly) query = query.Where(c => c.Entry.IsFavorite);

        // "Recentes": jogado nos últimos 30 dias, do mais recente para o mais antigo.
        if (RecentOnly)
            query = query.Where(c => c.Entry.LastPlayed is DateTime d && d > DateTime.Now.AddDays(-30));

        query = (SelectedSort?.Mode ?? GameSortMode.Name) switch
        {
            GameSortMode.LastPlayed => query.OrderByDescending(c => c.Entry.LastPlayed ?? DateTime.MinValue)
                                            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            GameSortMode.Playtime => query.OrderByDescending(c => c.Entry.TotalMinutesPlayed)
                                          .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            GameSortMode.Added => query.OrderByDescending(c => c.Entry.AddedAt),
            GameSortMode.Launcher => query.OrderBy(c => c.Entry.Launcher)
                                          .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        // Favoritos sempre no topo — é o que o usuário quer alcançar primeiro.
        var ordered = query.OrderByDescending(c => c.Entry.IsFavorite).ToList();

        Games.Clear();
        foreach (var card in ordered) Games.Add(card);

        OnPropertyChanged(nameof(LibraryCountText));
        RebuildContinuePlaying();

        // Sem resultado de busca/filtro, a grade fica limpa sem exibir uma mensagem no centro.
        // A orientação continua aparecendo apenas quando a biblioteca realmente não tem jogos.
        EmptyMessage = _allCards.Count == 0 ? Loc.S("GH_EmptyLibrary") : "";

        if (SelectedGame is not null && !Games.Contains(SelectedGame))
            SelectedGame = Games.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedLauncherFilterChanged(LauncherFilterOption? value) => ApplyFilters();
    partial void OnSelectedSortChanged(SortOption? value) => ApplyFilters();
    partial void OnSelectedCategoryChanged(string? value) => ApplyFilters();

    partial void OnFavoritesOnlyChanged(bool value)
    {
        // As três abas são exclusivas entre si: marcar uma desmarca a outra.
        if (value && RecentOnly) RecentOnly = false;
        OnPropertyChanged(nameof(SectionAll));
        ApplyFilters();
    }

    partial void OnRecentOnlyChanged(bool value)
    {
        if (value && FavoritesOnly) FavoritesOnly = false;
        OnPropertyChanged(nameof(SectionAll));
        ApplyFilters();
    }

    partial void OnScanStatusChanged(string value) => OnPropertyChanged(nameof(FooterStatus));
    partial void OnSessionStatusChanged(string value) => OnPropertyChanged(nameof(FooterStatus));
    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsBusyAny));
    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(IsBusyAny));

    /// <summary>Recalcula o tamanho dos cartões a partir da preferência do GameHub.</summary>
    public void ApplyCardSize(CardSize size)
    {
        // A faixa do topo é quem carrega os cartões grandes; a grade abaixo é o catálogo completo e
        // funciona melhor compacta, mostrando mais jogos de uma vez sem precisar rolar.
        (CardWidth, CardHeight) = size switch
        {
            CardSize.Small => (104d, 156d),
            CardSize.Large => (164d, 246d),
            _ => (132d, 198d),
        };
    }

    // =====================================================================================
    //  Seleção em destaque
    // =====================================================================================

    partial void OnSelectedGameChanged(GameCardViewModel? oldValue, GameCardViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;

        // Som de navegação — só quando o usuário realmente mudou de jogo, nunca na carga inicial
        // nem quando a lista se reconstrói sozinha (varredura, troca de filtro).
        if (oldValue is not null && newValue is not null && !ReferenceEquals(oldValue, newValue))
            _sounds.Play(HubSound.Navigate);

        UpdateHeroFps();

        // O fundo é pesado (imagem grande) e a seleção muda a cada toque no direcional. Decodificar
        // na hora travava a thread de interface, o que deixava a navegação dura E engolia o som
        // curto que acabou de tocar. Então o fundo entra com um pequeno atraso: percorrer a
        // biblioteca fica instantâneo e só o jogo em que o usuário parou é carregado de fato.
        ScheduleHeroLoad(newValue);
    }

    /// <summary>
    /// Adia o carregamento da arte em destaque. Enquanto o usuário percorre a biblioteca, cada nova
    /// seleção reinicia a contagem; a imagem só é decodificada quando ele para. É o que mantém a
    /// navegação fluida numa biblioteca grande.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _heroTimer;
    private GameCardViewModel? _pendingHero;

    private void ScheduleHeroLoad(GameCardViewModel? card)
    {
        _pendingHero = card;

        if (_heroTimer is null)
        {
            _heroTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(130),
            };
            _heroTimer.Tick += (_, _) =>
            {
                _heroTimer!.Stop();
                LoadHeroNow(_pendingHero);
            };
        }

        _heroTimer.Stop();
        _heroTimer.Start();

        // Os textos do destaque não custam nada e devem acompanhar a seleção na hora.
        RaiseHeroTexts();
    }

    /// <summary>
    /// Qual carregamento é o atual. Como a decodificação acontece fora da interface, uma seleção
    /// mais nova pode terminar antes de uma mais antiga; este contador faz o resultado atrasado ser
    /// descartado em vez de sobrescrever o destaque correto.
    /// </summary>
    private int _heroGeneration;

    private async void LoadHeroNow(GameCardViewModel? card)
    {
        int generation = ++_heroGeneration;

        string? heroPath = card?.Entry.HeroPath ?? card?.Entry.CoverPath;
        string? coverPath = card?.Entry.CoverPath ?? card?.Entry.IconPath;

        if (heroPath is null && coverPath is null)
        {
            HeroImage = null;
            HeroCover = null;
            _theme.SetSelectedGameArt(null, null);
            return;
        }

        // A decodificação sai da interface. Um hero de 1280px custa dezenas de milissegundos, e
        // fazer isso no fio da interface era o que provocava a travadinha ao parar num jogo.
        // O Freeze() dentro de LoadBitmap é o que permite usar a imagem noutro fio com segurança.
        var (hero, cover) = await Task.Run(() => (
            GameArtService.LoadBitmap(heroPath, 1280),
            GameArtService.LoadBitmap(coverPath, 400)));

        // Chegou tarde: o usuário já está em outro jogo.
        if (generation != _heroGeneration) return;

        HeroImage = hero;
        HeroCover = cover;

        // O desfoque depende de o fundo ser arte widescreen ou a capa esticada — só dá para saber
        // agora, com o jogo já resolvido.
        UpdateBlurFromTheme();

        // Alimenta o tema: o fundo "baseado no jogo" e a adaptação de cores vêm daqui.
        _theme.SetSelectedGameArt(card?.Entry.HeroPath, card?.Entry.CoverPath);
    }

    private void RaiseHeroTexts()
    {
        OnPropertyChanged(nameof(HeroName));
        OnPropertyChanged(nameof(HeroPlatform));
        OnPropertyChanged(nameof(HeroPlaytime));
        OnPropertyChanged(nameof(HeroLastPlayed));
        OnPropertyChanged(nameof(HeroProfileName));
        OnPropertyChanged(nameof(HeroProfileSummary));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIsFavorite));
    }

    public bool HasSelection => SelectedGame is not null;
    public string HeroName => SelectedGame?.Name ?? "";
    public string HeroPlatform => SelectedGame?.PlatformText ?? "";
    public string HeroPlaytime => SelectedGame?.PlaytimeText ?? "";
    public string HeroLastPlayed => SelectedGame?.LastPlayedText ?? "";
    public bool SelectedIsFavorite => SelectedGame?.Entry.IsFavorite ?? false;

    public string HeroProfileName
    {
        get
        {
            var profile = _profiles.Find(SelectedGame?.Entry.ProfileId);
            return profile?.Name ?? Loc.S("GH_NoProfile");
        }
    }

    public string HeroProfileSummary
    {
        get
        {
            var profile = _profiles.Find(SelectedGame?.Entry.ProfileId);
            return profile?.SummaryText ?? Loc.S("GH_NoProfileHint");
        }
    }

    // =====================================================================================
    //  Continuar jogando
    // =====================================================================================

    /// <summary>
    /// A faixa do topo: os jogos que já foram abertos, do mais recente para o mais antigo. É o
    /// atalho para retomar o que estava em andamento, sem procurar na grade inteira.
    ///
    /// Os itens são os MESMOS objetos da grade, de propósito: a capa já carregada é reaproveitada
    /// e o favorito continua sincronizado. Por isso a faixa é um ItemsControl e não um ListBox —
    /// um mesmo item selecionado em duas listas brigaria pela seleção.
    /// </summary>
    public ObservableCollection<GameCardViewModel> ContinuePlaying { get; } = new();

    public bool HasContinuePlaying => ContinuePlaying.Count > 0;

    /// <summary>Clicar num card da faixa leva o destaque até aquele jogo.</summary>
    [RelayCommand]
    private void SelectFromContinue(GameCardViewModel? card)
    {
        if (card is null) return;
        SelectedGame = Games.Contains(card) ? card : Games.FirstOrDefault(g => g.Id == card.Id) ?? card;
        if (SelectedGame is not null) ScrollToRequested?.Invoke(SelectedGame);
    }

    /// <summary>O que a faixa do topo mostra.</summary>
    public enum HighlightMode { Recent, Favorites, MostPlayed }

    [ObservableProperty] private HighlightMode highlightRow = HighlightMode.Recent;

    public bool HighlightIsRecent => HighlightRow == HighlightMode.Recent;
    public bool HighlightIsFavorites => HighlightRow == HighlightMode.Favorites;
    public bool HighlightIsMostPlayed => HighlightRow == HighlightMode.MostPlayed;

    /// <summary>Título da faixa, que acompanha o modo escolhido.</summary>
    public string HighlightTitle => HighlightRow switch
    {
        HighlightMode.Favorites => Loc.S("GH_Favorites"),
        HighlightMode.MostPlayed => Loc.S("GH_MostPlayed"),
        _ => Loc.S("GH_ContinuePlaying"),
    };

    partial void OnHighlightRowChanged(HighlightMode value)
    {
        OnPropertyChanged(nameof(HighlightIsRecent));
        OnPropertyChanged(nameof(HighlightIsFavorites));
        OnPropertyChanged(nameof(HighlightIsMostPlayed));
        OnPropertyChanged(nameof(HighlightTitle));
        RebuildContinuePlaying();
    }

    [RelayCommand]
    private void SetHighlightRow(string? mode) => HighlightRow = mode switch
    {
        "favorites" => HighlightMode.Favorites,
        "mostplayed" => HighlightMode.MostPlayed,
        _ => HighlightMode.Recent,
    };

    private void RebuildContinuePlaying()
    {
        IEnumerable<GameCardViewModel> source = HighlightRow switch
        {
            HighlightMode.Favorites => _allCards
                .Where(c => c.Entry.IsFavorite)
                .OrderByDescending(c => c.Entry.LastPlayed ?? DateTime.MinValue),

            HighlightMode.MostPlayed => _allCards
                .Where(c => c.Entry.TotalMinutesPlayed > 0)
                .OrderByDescending(c => c.Entry.TotalMinutesPlayed),

            _ => _allCards
                .Where(c => c.Entry.LastPlayed is not null)
                .OrderByDescending(c => c.Entry.LastPlayed),
        };

        ContinuePlaying.Clear();
        foreach (var card in source.Take(6)) ContinuePlaying.Add(card);

        OnPropertyChanged(nameof(HasContinuePlaying));
    }

    // =====================================================================================
    //  Escolha de perfil
    // =====================================================================================

    /// <summary>
    /// Perfis oferecidos ao clicar no seletor do destaque: "nenhum perfil" e os cadastrados. É a
    /// troca rápida — editar o conteúdo de um perfil continua sendo trabalho do editor, alcançável
    /// pelo último item da lista.
    /// </summary>
    public ObservableCollection<ProfileChoiceViewModel> ProfileChoices { get; } = new();

    [ObservableProperty] private bool isProfilePickerOpen;

    [RelayCommand]
    private void OpenProfilePicker()
    {
        if (SelectedGame is null) return;

        ProfileChoices.Clear();
        string? current = SelectedGame.Entry.ProfileId;

        ProfileChoices.Add(new ProfileChoiceViewModel(null, Loc.S("GH_NoProfile"), "", current is null));
        foreach (var profile in _profiles.Profiles)
            ProfileChoices.Add(new ProfileChoiceViewModel(
                profile.Id, profile.Name, profile.SummaryText, profile.Id == current));

        IsProfilePickerOpen = true;
    }

    [RelayCommand]
    private void CloseProfilePicker() => IsProfilePickerOpen = false;

    /// <summary>Aplica o perfil escolhido ao jogo em destaque e fecha a lista.</summary>
    [RelayCommand]
    private void ChooseProfile(ProfileChoiceViewModel? choice)
    {
        IsProfilePickerOpen = false;
        if (choice is null || SelectedGame is null) return;

        SelectedGame.Entry.ProfileId = choice.Id;
        _library.UpdateGame(SelectedGame.Entry);

        OnPropertyChanged(nameof(HeroProfileName));
        OnPropertyChanged(nameof(HeroProfileSummary));
        _sounds.Play(HubSound.Confirm);
    }

    /// <summary>Abre o editor do perfil atual — a porta para mudar o que o perfil faz.</summary>
    [RelayCommand]
    private void EditCurrentProfile()
    {
        IsProfilePickerOpen = false;
        if (SelectedGame is not null) EditProfileRequested?.Invoke(SelectedGame.Entry);
    }

    // =====================================================================================
    //  Comandos
    // =====================================================================================

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (SelectedGame is null || IsSessionRunning) return;

        _sounds.Play(HubSound.Launch);

        IsLaunching = true;
        SessionStatus = Loc.S("GH_Preparing");
        try
        {
            await _sessions.StartAsync(SelectedGame.Entry);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    [RelayCommand]
    private async Task StopSessionAsync() => await _sessions.StopAsync();

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedGame is null) return;
        SelectedGame.Entry.IsFavorite = !SelectedGame.Entry.IsFavorite;
        _library.UpdateGame(SelectedGame.Entry);
        OnPropertyChanged(nameof(SelectedIsFavorite));
    }

    [RelayCommand]
    private void EditGame()
    {
        if (SelectedGame is not null) EditGameRequested?.Invoke(SelectedGame.Entry);
    }

    [RelayCommand]
    private void EditProfile()
    {
        if (SelectedGame is not null) EditProfileRequested?.Invoke(SelectedGame.Entry);
    }

    [RelayCommand]
    private void ManageEmulators() => ManageEmulatorsRequested?.Invoke();

    [RelayCommand]
    private void ChangeCover()
    {
        if (SelectedGame is not null) ChangeArtRequested?.Invoke(SelectedGame.Entry, ArtKind.Cover);
    }

    [RelayCommand]
    private void ChangeHero()
    {
        if (SelectedGame is not null) ChangeArtRequested?.Invoke(SelectedGame.Entry, ArtKind.Hero);
    }

    [RelayCommand]
    private void RemoveGame()
    {
        if (SelectedGame is null) return;

        var result = System.Windows.MessageBox.Show(
            Loc.F("GH_RemoveConfirm", SelectedGame.Name),
            Loc.S("GH_Title"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;
        _library.RemoveGame(SelectedGame.Id);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanStatus = Loc.S("GH_Scanning");
        try
        {
            var progress = new Progress<string>(source => ScanStatus = Loc.F("GH_ScanningSource", source));
            var result = await _library.ScanAsync(progress);

            ScanStatus = result.Added == 0 && result.Updated == 0 && result.Removed == 0
                ? Loc.S("GH_ScanNothing")
                : Loc.F("GH_ScanResult", result.Added, result.Updated, result.Removed);

            await FetchArtAsync();
        }
        catch (Exception ex)
        {
            ScanStatus = Loc.F("GH_ScanFailed", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task ScanShortcutsAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanStatus = Loc.S("GH_Scanning");
        try
        {
            var result = await _library.ScanShortcutsAsync();
            ScanStatus = Loc.F("GH_ScanResult", result.Added, result.Updated, result.Removed);
            await FetchArtAsync();
        }
        finally { IsScanning = false; }
    }

    // =====================================================================================
    //  Arte
    // =====================================================================================

    /// <summary>
    /// Completa a arte que estiver faltando, em segundo plano. É cancelável e para sozinho durante
    /// o Modo Gaming — buscar capa enquanto o usuário joga seria exatamente o tipo de trabalho que o
    /// modo existe para evitar.
    /// </summary>
    /// <summary>
    /// Procura capa de verdade para os itens que ficaram sem — os que exibem o quadrado colorido
    /// gerado pelo Pulse1x. Diferente da busca automática, esta é pedida pelo usuário e esquece as
    /// falhas anteriores: a internet pode ter voltado, ou o acervo pode ter ganhado aquele título.
    /// </summary>
    [RelayCommand]
    private async Task FetchCoversAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanStatus = Loc.S("GH_FetchingCovers");
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        var token = _artCts.Token;

        try
        {
            var entries = _allCards.Select(c => c.Entry).ToList();
            var progress = new Progress<(int done, int total, string name)>(p =>
                ScanStatus = Loc.F("GH_FetchingCoversProgress", p.done, p.total, p.name));

            int found = await _art.RefetchMissingCoversAsync(entries, progress, token);

            if (found > 0)
            {
                _library.Save();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var card in _allCards.ToList())
                    {
                        card.ReleaseCover();
                        card.RequestCover();
                    }
                    if (SelectedGame is not null) OnSelectedGameChanged(null, SelectedGame);
                });
            }

            ScanStatus = found == 0 ? Loc.S("GH_FetchCoversNone") : Loc.F("GH_FetchCoversResult", found);
        }
        catch (OperationCanceledException) { ScanStatus = ""; }
        catch (Exception ex) { ScanStatus = Loc.F("GH_ScanFailed", ex.Message); }
        finally { IsScanning = false; }
    }

    /// <summary>
    /// Placeholders já tentados nesta sessão. Uma capa que não existe em lugar nenhum não deve
    /// custar uma busca cada vez que o hub é aberto.
    /// </summary>
    private bool _retriedPlaceholders;

    private async Task FetchArtAsync()
    {
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        var token = _artCts.Token;

        try
        {
            bool anyChanged = false;

            foreach (var card in _allCards.ToList())
            {
                if (token.IsCancellationRequested || _gamingMode.IsActive) return;

                bool changed = await _art.EnsureArtAsync(card.Entry, token);
                if (!changed) continue;

                anyChanged = true;
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    card.ReleaseCover();
                    card.RequestCover();
                    if (card == SelectedGame) OnSelectedGameChanged(null, card);
                });
            }

            if (anyChanged) _library.Save();

            // Os itens que ficaram com o quadrado colorido merecem uma segunda tentativa: a capa
            // pode não ter sido encontrada porque a internet estava fora, ou porque a versão
            // anterior do Pulse1x ainda não sabia onde procurar. Uma vez por sessão — uma capa que
            // realmente não existe não deve custar uma busca a cada abertura do hub.
            if (!_retriedPlaceholders)
            {
                _retriedPlaceholders = true;
                await RetryPlaceholdersAsync(token);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* arte é cosmética: uma falha aqui nunca impede o uso da biblioteca */ }
    }

    private async Task RetryPlaceholdersAsync(CancellationToken token)
    {
        var entries = _allCards.Select(c => c.Entry).ToList();
        if (!entries.Any(GameArtService.NeedsRealCover)) return;

        int found = await _art.RefetchMissingCoversAsync(entries, null, token);
        if (found == 0) return;

        _library.Save();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var card in _allCards.ToList())
            {
                card.ReleaseCover();
                card.RequestCover();
            }
            if (SelectedGame is not null) OnSelectedGameChanged(null, SelectedGame);
        });
    }

    // =====================================================================================
    //  Sessão
    // =====================================================================================

    private void OnSessionStarted(GameSession session) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsSessionRunning = true;
            SessionStatus = Loc.F("GH_SessionRunning", session.Game.Name);
            // Durante a partida o controle pertence ao jogo, não ao GameHub.
            _gamepad.SetActive(false);
        });

    private void OnSessionEnded(GameSession session) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsSessionRunning = false;
            SessionStatus = Loc.F("GH_SessionEnded", session.Game.Name,
                (int)session.Duration.TotalMinutes);
            _gamepad.SetActive(_isPageActive);

            var card = _allCards.FirstOrDefault(c => c.Id == session.Game.Id);
            card?.Refresh();
            OnPropertyChanged(nameof(HeroPlaytime));
            OnPropertyChanged(nameof(HeroLastPlayed));
        });

    private void OnStepReported(ProfileStepProgress step) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            string label = Loc.S(step.LabelKey);
            SessionStatus = step.Detail is null ? label : $"{label} — {step.Detail}";
        });

    // =====================================================================================
    //  Controle
    // =====================================================================================

    /// <summary>Quantos cartões cabem por linha — informado pela página conforme a largura real.</summary>
    public int ColumnsPerRow { get; set; } = 6;

    /// <summary>
    /// Onde o controle está navegando agora. Fora da grade (num menu, no teclado virtual ou nos
    /// botões do topo), quem manda é o foco do WPF — a grade só volta a responder quando o
    /// contexto retorna para ela.
    /// </summary>
    [ObservableProperty] private bool gridHasFocus = true;

    /// <summary>O item selecionado está na primeira linha da grade? O roteador usa isto para saber
    /// quando "subir" deve sair da grade em vez de mover a seleção.</summary>
    public bool IsAtGridTopRow
    {
        get
        {
            if (SelectedGame is null) return true;
            int index = Games.IndexOf(SelectedGame);
            return index < Math.Max(1, ColumnsPerRow);
        }
    }

    /// <summary>
    /// Move a seleção dentro da grade. Devolve false quando o movimento sairia da grade — aí quem
    /// decide o que fazer é o roteador de entrada da página.
    /// </summary>
    public bool MoveSelection(GamepadDirection direction)
    {
        if (Games.Count == 0) return false;

        int index = SelectedGame is null ? 0 : Games.IndexOf(SelectedGame);
        if (index < 0) index = 0;

        int columns = Math.Max(1, ColumnsPerRow);
        int next = direction switch
        {
            GamepadDirection.Left => index - 1,
            GamepadDirection.Right => index + 1,
            GamepadDirection.Up => index - columns,
            _ => index + columns,
        };

        // Fora dos limites: a seleção fica onde está. Não damos a volta, que é o comportamento das
        // interfaces de console e evita saltos desorientadores.
        if (next < 0 || next >= Games.Count) return false;

        SelectedGame = Games[next];
        ScrollToRequested?.Invoke(Games[next]);
        return true;
    }

    /// <summary>Toca um som da navegação do hub (usado pelo roteador de entrada).</summary>
    public void PlaySound(HubSound sound) => _sounds.Play(sound);

    /// <summary>Ações expostas ao roteador de entrada da página.</summary>
    public void PlaySelected()
    {
        if (!IsSessionRunning) _ = PlayAsync();
    }

    public void ToggleFavoriteSelected() => ToggleFavorite();

    public void OpenSelectedProfile() => EditProfile();

    /// <summary>Alterna entre as abas (L1/R1).</summary>
    public void CycleSectionPublic(int delta) => CycleSection(delta);

    /// <summary>Limpa busca e filtros — o que se espera do botão de voltar numa biblioteca.</summary>
    public bool ClearFiltersStep()
    {
        if (!string.IsNullOrEmpty(SearchText)) { SearchText = ""; return true; }
        if (RecentOnly || FavoritesOnly) { SectionAll = true; return true; }
        if (SelectedLauncherFilter?.Kind is not null)
        {
            SelectedLauncherFilter = LauncherFilters.FirstOrDefault();
            return true;
        }
        return false;
    }

        /// <summary>
    /// Alterna entre as abas Todos → Recentes → Favoritos (e de volta). Índices: 0/1/2, na mesma
    /// ordem em que aparecem na tela.
    /// </summary>
    private void CycleSection(int delta)
    {
        int current = RecentOnly ? 1 : FavoritesOnly ? 2 : 0;
        int next = (current + delta + 3) % 3;

        _sounds.Play(HubSound.Navigate);

        switch (next)
        {
            case 1: RecentOnly = true; break;
            case 2: FavoritesOnly = true; break;
            default: SectionAll = true; break;
        }
    }

    private void CycleLauncherFilter(int delta)
    {
        if (LauncherFilters.Count == 0) return;
        int index = SelectedLauncherFilter is null ? 0 : LauncherFilters.IndexOf(SelectedLauncherFilter);
        index = (index + delta + LauncherFilters.Count) % LauncherFilters.Count;
        SelectedLauncherFilter = LauncherFilters[index];
    }

    // =====================================================================================
    //  Opções e idioma
    // =====================================================================================

    private void BuildFilterOptions()
    {
        var previousLauncher = SelectedLauncherFilter?.Kind;
        var previousSort = SelectedSort?.Mode;

        LauncherFilters.Clear();
        LauncherFilters.Add(new LauncherFilterOption(null, Loc.S("GH_AllPlatforms")));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Steam, "Steam"));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Epic, "Epic Games"));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Gog, "GOG"));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Emulator, Loc.S("GH_PlatformEmulator")));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Manual, Loc.S("GH_PlatformManual")));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Shortcut, Loc.S("GH_PlatformShortcut")));
        LauncherFilters.Add(new LauncherFilterOption(LauncherKind.Application, Loc.S("GH_PlatformApp")));

        SortOptions.Clear();
        SortOptions.Add(new SortOption(GameSortMode.Name, Loc.S("GH_SortName")));
        SortOptions.Add(new SortOption(GameSortMode.LastPlayed, Loc.S("GH_SortLastPlayed")));
        SortOptions.Add(new SortOption(GameSortMode.Playtime, Loc.S("GH_SortPlaytime")));
        SortOptions.Add(new SortOption(GameSortMode.Added, Loc.S("GH_SortAdded")));
        SortOptions.Add(new SortOption(GameSortMode.Launcher, Loc.S("GH_SortLauncher")));

        SelectedLauncherFilter = LauncherFilters.FirstOrDefault(f => f.Kind == previousLauncher) ?? LauncherFilters[0];
        SelectedSort = SortOptions.FirstOrDefault(s => s.Mode == previousSort) ?? SortOptions[0];
    }

    private void RefreshCategoryFilters()
    {
        string? previous = SelectedCategory;
        CategoryFilters.Clear();
        CategoryFilters.Add(Loc.S("GH_AllCategories"));
        foreach (var category in _library.Categories) CategoryFilters.Add(category);
        SelectedCategory = CategoryFilters.Contains(previous ?? "") ? previous : CategoryFilters[0];
    }

    private void OnLanguageChanged()
    {
        RefreshControllerHints();
        BuildFilterOptions();
        RefreshCategoryFilters();
        foreach (var card in _allCards) card.Refresh();
        OnPropertyChanged(nameof(HeroPlatform));
        OnPropertyChanged(nameof(HeroPlaytime));
        OnPropertyChanged(nameof(HeroLastPlayed));
        OnPropertyChanged(nameof(HeroProfileName));
        OnPropertyChanged(nameof(HeroProfileSummary));
        ApplyFilters();
    }

    private void UpdateBlurFromTheme() => HeroBlurRadius = _theme.EffectiveBlur(BaseHeroBlur);

    /// <summary>
    /// Desfoque do fundo em destaque. Uma arte widescreen de verdade só precisa de um véu leve;
    /// quando o fundo é a própria capa vertical esticada para preencher a tela, o desfoque é o que
    /// transforma uma imagem distorcida numa mancha com as cores do jogo.
    /// </summary>
    private double BaseHeroBlur =>
        SelectedGame?.Entry is { } entry &&
        !string.IsNullOrEmpty(entry.HeroPath) &&
        !string.Equals(entry.HeroPath, entry.CoverPath, StringComparison.OrdinalIgnoreCase)
            ? 12 : 24;

    public void Dispose()
    {
        _artCts?.Cancel();
        _gamepad.ActiveControllerChanged -= OnActiveControllerChanged;
        _theme.AppearanceChanged -= UpdateBlurFromTheme;
        _library.Changed -= OnLibraryChanged;
        _sessions.SessionStarted -= OnSessionStarted;
        _sessions.SessionEnded -= OnSessionEnded;
        _sessions.StepReported -= OnStepReported;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }
}
