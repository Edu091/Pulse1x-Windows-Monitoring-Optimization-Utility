using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.FpsInGame;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels;

/// <summary>Item de escolha com rótulo localizado (resolução, preset, filtro).</summary>
public sealed record FpsOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public enum FpsGameFilter { Popular, Library, All }

/// <summary>
/// Categoria Fps-In-Game: diz se cada jogo roda no PC do usuário e com qual FPS médio, a partir da
/// base pronta embutida (<see cref="FpsDatabase"/>). A GPU é detectada sozinha, mas pode ser trocada
/// à mão — útil para máquinas não reconhecidas ou para simular um upgrade. Não há seletor de
/// gráficos: a lista mostra o melhor preset para 60 FPS e os detalhes trazem os quatro.
/// </summary>
public partial class FpsInGameViewModel : ObservableObject
{
    /// <summary>Quantos jogos a lista mostra de cada vez — a busca alcança a base inteira.</summary>
    private const int MaxResults = 250;
    private const int PopularCount = 300;

    private readonly SettingsService _settings;
    private readonly SystemInfoService _systemInfo;
    private readonly IHardwareMonitorService _hardware;
    private readonly GameLibraryService _library;
    private readonly DispatcherTimer _searchDebounce;

    private FpsDatabase? _db;
    private HashSet<string> _librarySteamIds = new();
    private HashSet<string> _libraryNames = new();
    private bool _initialized;
    private bool _suppressRefresh;

    public FpsInGameViewModel(SettingsService settings, SystemInfoService systemInfo,
        IHardwareMonitorService hardware, GameLibraryService library)
    {
        _settings = settings;
        _systemInfo = systemInfo;
        _hardware = hardware;
        _library = library;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Refresh();
        };

        BuildOptions();
        Loc.Instance.LanguageChanged += () =>
        {
            BuildOptions();
            if (_activeGpu is not null) GpuScoreText = Loc.F("Fps_GpuScore", _activeGpu.G3d.ToString("N0"));
            if (_ramGb is not > 0) RamText = Loc.S("Hw_NA");
            Refresh();
        };
        _library.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LoadLibrary();
            Refresh();
        });
    }

    // ---------------------------------------------------------------- estado

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _loadError = "";

    [ObservableProperty] private string _detectedGpuName = "";
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _ramText = "";
    [ObservableProperty] private bool _gpuNotRecognized;
    [ObservableProperty] private bool _gpuIsApproximate;

    [ObservableProperty] private IReadOnlyList<GpuSpec> _gpus = Array.Empty<GpuSpec>();
    [ObservableProperty] private GpuSpec? _selectedGpu;
    /// <summary>Texto digitado no seletor de GPU; filtra a lista por palavras ("4060", "rx 6600").</summary>
    [ObservableProperty] private string _gpuSearchText = "";
    [ObservableProperty] private string _gpuScoreText = "";
    [ObservableProperty] private bool _isGpuOverridden;

    [ObservableProperty] private IReadOnlyList<FpsOption<TargetResolution>> _resolutions = Array.Empty<FpsOption<TargetResolution>>();
    [ObservableProperty] private FpsOption<TargetResolution>? _selectedResolution;
    [ObservableProperty] private IReadOnlyList<FpsOption<FpsGameFilter>> _filters = Array.Empty<FpsOption<FpsGameFilter>>();
    [ObservableProperty] private FpsOption<FpsGameFilter>? _selectedFilter;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<GameFpsItemViewModel> _results = Array.Empty<GameFpsItemViewModel>();
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private string _popularSummary = "";
    [ObservableProperty] private bool _hasNoResults;

    [ObservableProperty] private GameFpsItemViewModel? _selectedGame;
    [ObservableProperty] private GameFpsDetailViewModel? _detail;

    public string Attribution => Loc.F("Fps_Attribution", FpsDatabase.SourceName);

    private double? _ramGb;
    /// <summary>VRAM dedicada da GPU detectada (GB); null em integradas, que usam a RAM do sistema.</summary>
    private double? _vramGb;

    // ---------------------------------------------------------------- inicialização

    /// <summary>Carrega base e hardware na primeira vez que a página aparece.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var dbTask = FpsDatabase.LoadAsync();
            var hwTask = Task.Run(() =>
            {
                var adapters = ReadVideoAdapters();
                if (adapters.Count == 0) adapters.Add(_systemInfo.GetStaticInfo().GpuName);
                return (Adapters: adapters, Cpu: _hardware.CpuName, Ram: ReadTotalRamGb(), Laptop: IsLaptop());
            });
            await Task.WhenAll(dbTask, hwTask);

            _db = dbTask.Result;
            var hw = hwTask.Result;
            CpuName = hw.Cpu;
            _ramGb = hw.Ram;
            RamText = hw.Ram is > 0 ? $"{Math.Round(hw.Ram.Value):0} GB" : Loc.S("Hw_NA");

            LoadLibrary();

            // Em notebooks híbridos e desktops com a integrada ativa há mais de um adaptador: vale o
            // mais forte que for reconhecido, que é onde o jogo roda.
            var match = GpuMatcher.MatchBest(_db.Gpus, hw.Adapters, hw.Cpu, hw.Laptop);
            var detected = match?.Gpu;
            GpuNotRecognized = match is null;
            _detectedApproximate = match?.IsApproximate == true;
            _detectedMatch = detected;
            DetectedGpuName = string.Join(" / ", hw.Adapters.Where(a => !GpuMatcher.IsVirtualAdapter(a)));
            _vramGb = null;
            if (detected is not null)
            {
                var adapterName = hw.Adapters.FirstOrDefault(a => GpuMatcher.Match(_db.Gpus, a, hw.Cpu, hw.Laptop)?.Gpu == detected);
                double vram = await Task.Run(() => GpuVramReader.ReadVramBytes(adapterName) / (1024d * 1024 * 1024));
                if (vram >= 1.5) _vramGb = vram;
            }

            // Escolha manual salva tem prioridade; sem ela, a detectada; sem nenhuma, uma GPU de
            // entrada comum, para a página não ficar vazia enquanto o usuário escolhe a dele.
            var saved = _settings.Current.FpsInGameGpu is { Length: > 0 } name
                ? _db.Gpus.FirstOrDefault(g => g.Name == name)
                : null;

            _suppressRefresh = true;
            Gpus = _db.Gpus;
            _gpuKeys = _db.Gpus.ToDictionary(g => g, g => TextNormalizer.Normalize(g.Name));
            SelectedGpu = saved ?? detected ?? _db.Gpus.FirstOrDefault(g => g.Name == "GeForce GTX 1650") ?? _db.Gpus[^1];
            _suppressRefresh = false;

            IsLoading = false;
            Refresh();
        }
        catch (Exception ex)
        {
            LoadError = Loc.F("Fps_LoadError", ex.Message);
            IsLoading = false;
        }
    }

    private GpuSpec? _detectedMatch;
    private bool _detectedApproximate;
    /// <summary>
    /// A GPU usada nos cálculos. Fica separada de <see cref="SelectedGpu"/> porque o ComboBox
    /// anula a seleção quando a lista filtrada deixa de conter o item, e o cálculo não pode sumir
    /// enquanto o usuário ainda está digitando.
    /// </summary>
    private GpuSpec? _activeGpu;
    private Dictionary<GpuSpec, string> _gpuKeys = new();

    private void BuildOptions()
    {
        _suppressRefresh = true;
        // Sem escolha salva, a resolução parte da do monitor principal.
        var res = SelectedResolution?.Value
                  ?? (Enum.TryParse<TargetResolution>(_settings.Current.FpsInGameResolution, out var r) ? r : DetectMonitorResolution());
        var filter = SelectedFilter?.Value ?? FpsGameFilter.Popular;

        Resolutions = new[]
        {
            new FpsOption<TargetResolution>(TargetResolution.R1080p, "1080p (Full HD)"),
            new FpsOption<TargetResolution>(TargetResolution.R1440p, "1440p (2K)"),
            new FpsOption<TargetResolution>(TargetResolution.R4K, "2160p (4K)"),
        };
        Filters = new[]
        {
            new FpsOption<FpsGameFilter>(FpsGameFilter.Popular, Loc.S("Fps_FilterPopular")),
            new FpsOption<FpsGameFilter>(FpsGameFilter.Library, Loc.S("Fps_FilterLibrary")),
            new FpsOption<FpsGameFilter>(FpsGameFilter.All, Loc.S("Fps_FilterAll")),
        };
        SelectedResolution = Resolutions.First(o => o.Value == res);
        SelectedFilter = Filters.First(o => o.Value == filter);
        OnPropertyChanged(nameof(Attribution));
        _suppressRefresh = false;
    }

    private void LoadLibrary()
    {
        var games = _library.Games;
        _librarySteamIds = games
            .SelectMany(g => new[] { g.Launcher == LauncherKind.Steam ? g.LauncherAppId : null, g.OnlineArtAppId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet();
        _libraryNames = games.Select(g => TextNormalizer.Normalize(g.Name)).Where(n => n.Length > 0).ToHashSet();
    }

    private static double? ReadTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in searcher.Get())
                if (mo["TotalPhysicalMemory"] is not null)
                    return Convert.ToDouble(mo["TotalPhysicalMemory"]) / (1024d * 1024 * 1024);
        }
        catch { /* sem RAM conhecida a checagem de memória simplesmente não é feita */ }
        return null;
    }

    /// <summary>Todos os adaptadores de vídeo do PC (o SystemInfoService só devolve um).</summary>
    private static List<string> ReadVideoAdapters()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
                if (mo["Name"] is string n && !string.IsNullOrWhiteSpace(n) && !names.Contains(n.Trim()))
                    names.Add(n.Trim());
        }
        catch { /* sem WMI cai no nome do SystemInfoService */ }
        return names;
    }

    /// <summary>Notebook pelo tipo de gabinete (SMBIOS); bateria sozinha não basta, nobreaks USB também aparecem.</summary>
    private static bool IsLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject mo in searcher.Get())
                if (mo["ChassisTypes"] is ushort[] types && types.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32))
                    return true;
        }
        catch { /* desconhecido: trata como desktop */ }
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    /// <summary>Resolução real (em pixels, sem escala de DPI) do monitor principal.</summary>
    private static TargetResolution DetectMonitorResolution()
    {
        try
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            // -1 = ENUM_CURRENT_SETTINGS
            if (EnumDisplaySettings(null, -1, ref mode))
            {
                // Pela altura, para monitores ultrawide (3440x1440) caírem em 1440p.
                if (mode.dmPelsHeight >= 2000) return TargetResolution.R4K;
                if (mode.dmPelsHeight >= 1400) return TargetResolution.R1440p;
            }
        }
        catch { /* padrão abaixo */ }
        return TargetResolution.R1080p;
    }

    // ---------------------------------------------------------------- reações

    partial void OnSelectedGpuChanged(GpuSpec? value)
    {
        // Digitar no seletor desmarca o item até achar outro: mantém o cálculo anterior.
        if (value is null) return;
        _activeGpu = value;
        GpuScoreText = Loc.F("Fps_GpuScore", value.G3d.ToString("N0"));
        IsGpuOverridden = !ReferenceEquals(value, _detectedMatch);
        GpuIsApproximate = !IsGpuOverridden && _detectedApproximate;
        if (_suppressRefresh) return;

        _settings.Current.FpsInGameGpu = IsGpuOverridden ? value.Name : null;
        _settings.Save();
        Refresh();
    }

    partial void OnSelectedResolutionChanged(FpsOption<TargetResolution>? value)
    {
        if (_suppressRefresh || value is null) return;
        _settings.Current.FpsInGameResolution = value.Value.ToString();
        _settings.Save();
        Refresh();
    }

    partial void OnSelectedFilterChanged(FpsOption<FpsGameFilter>? value)
    {
        if (_suppressRefresh) return;
        Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnSelectedGameChanged(GameFpsItemViewModel? value) => Detail = value is null ? null : BuildDetail(value.Game);

    partial void OnGpuSearchTextChanged(string value)
    {
        if (_db is null) return;
        string query = TextNormalizer.Normalize(value ?? "");
        // Texto vazio ou igual ao item escolhido (o ComboBox escreve o nome ao selecionar): lista inteira.
        if (query.Length == 0 || (_activeGpu is not null && query == _gpuKeys.GetValueOrDefault(_activeGpu)))
        {
            if (!ReferenceEquals(Gpus, _db.Gpus)) Gpus = _db.Gpus;
            return;
        }
        var terms = query.Split(' ');
        Gpus = _db.Gpus.Where(g => terms.All(t => _gpuKeys.GetValueOrDefault(g, "").Contains(t, StringComparison.Ordinal))).ToList();
    }

    /// <summary>Ao sair do seletor sem escolher nada, volta a mostrar a GPU em uso e a lista inteira.</summary>
    public void RestoreGpuSelection()
    {
        if (_db is null) return;
        if (!ReferenceEquals(Gpus, _db.Gpus)) Gpus = _db.Gpus;
        if (_activeGpu is not null && !ReferenceEquals(SelectedGpu, _activeGpu)) SelectedGpu = _activeGpu;
        GpuSearchText = _activeGpu?.Name ?? "";
    }

    [RelayCommand]
    private void UseDetectedGpu()
    {
        if (_detectedMatch is not null) SelectedGpu = _detectedMatch;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void OpenSource(string? url)
    {
        try { Process.Start(new ProcessStartInfo(url ?? FpsDatabase.SourceUrl) { UseShellExecute = true }); }
        catch { /* sem navegador padrão: nada a fazer */ }
    }

    // ---------------------------------------------------------------- cálculo

    private void Refresh()
    {
        if (_suppressRefresh || _db is null || _activeGpu is null || SelectedResolution is null) return;

        int score = _activeGpu.G3d;
        var res = SelectedResolution.Value;
        var refs = _db.ReferenceScores;

        string query = TextNormalizer.Normalize(SearchText ?? "");
        IEnumerable<GameSpec> source = _db.Games;
        int total;

        if (query.Length > 0)
        {
            // A busca sempre percorre a base inteira, independente do filtro.
            var terms = query.Split(' ');
            var matches = source.Where(g => terms.All(t => g.SearchKey.Contains(t, StringComparison.Ordinal)));
            if (SelectedFilter?.Value == FpsGameFilter.Library) matches = matches.Where(IsInLibrary);
            var list = matches
                .OrderByDescending(g => g.SearchKey == query)
                .ThenByDescending(g => g.SearchKey.StartsWith(query, StringComparison.Ordinal))
                .ThenBy(g => g.Popularity ?? int.MaxValue)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            total = list.Count;
            source = list;
        }
        else
        {
            var list = (SelectedFilter?.Value ?? FpsGameFilter.Popular) switch
            {
                FpsGameFilter.Library => source.Where(IsInLibrary).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                FpsGameFilter.All => source.OrderBy(g => g.Popularity ?? int.MaxValue).ThenByDescending(g => g.Year ?? 0).ToList(),
                _ => source.Where(g => g.Popularity is not null).OrderBy(g => g.Popularity).Take(PopularCount).ToList(),
            };
            total = list.Count;
            source = list;
        }

        var previous = SelectedGame?.Game;
        Results = source.Take(MaxResults)
            .Select(g =>
            {
                var (preset, estimate) = FpsEstimator.Recommend(g, refs, score, _ramGb, res);
                return new GameFpsItemViewModel(g, preset, estimate, IsInLibrary(g));
            })
            .ToList();
        HasNoResults = Results.Count == 0;
        ResultSummary = total > Results.Count
            ? Loc.F("Fps_ShowingOf", Results.Count.ToString("N0"), total.ToString("N0"))
            : Loc.F("Fps_Showing", total.ToString("N0"));

        // Resumo do topo: quantos dos 100 jogos mais populares chegam a 60 FPS em algum preset.
        var top = _db.Games.Where(g => g.Popularity is not null).OrderBy(g => g.Popularity).Take(100).ToList();
        int smooth = top.Count(g => FpsEstimator.BestPresetFor(g, refs, score, res) is not null);
        int playable = top.Count(g => FpsEstimator.Recommend(g, refs, score, _ramGb, res).Estimate.Verdict >= RunVerdict.Playable);
        PopularSummary = Loc.F("Fps_PopularSummary", smooth, playable, top.Count, SelectedResolution.Label);

        // Os itens são recriados a cada cálculo: mantém o mesmo jogo selecionado.
        var keep = Results.FirstOrDefault(r => ReferenceEquals(r.Game, previous)) ?? Results.FirstOrDefault();
        if (ReferenceEquals(keep, SelectedGame)) Detail = keep is null ? null : BuildDetail(keep.Game);
        else SelectedGame = keep; // OnSelectedGameChanged monta os detalhes
    }

    private bool IsInLibrary(GameSpec g) =>
        (g.SteamAppId is not null && _librarySteamIds.Contains(g.SteamAppId)) || _libraryNames.Contains(g.SearchKey);

    private GameFpsDetailViewModel? BuildDetail(GameSpec game)
    {
        if (_db is null || _activeGpu is null || SelectedResolution is null) return null;
        int score = _activeGpu.G3d;
        var res = SelectedResolution.Value;

        var best = FpsEstimator.BestPresetFor(game, _db.ReferenceScores, score, res);
        var (recommended, current) = FpsEstimator.Recommend(game, _db.ReferenceScores, score, _ramGb, res);
        var rows = Enum.GetValues<GraphicsPreset>()
            .Select(p => new PresetRowViewModel(PresetLabel(p),
                FpsEstimator.Estimate(game, _db.ReferenceScores, score, _ramGb, p, res), p == recommended))
            .ToList();

        string suggestion = best is null
            ? Loc.S("Fps_NoPresetFor60")
            : Loc.F("Fps_BestPresetFor60", PresetLabel(best.Value), SelectedResolution.Label);

        var warnings = new List<string>();
        if (current.BelowMinGpu) warnings.Add(Loc.S("Fps_WarnMinGpu"));
        if (current.BelowMinRam) warnings.Add(Loc.F("Fps_WarnMinRam", game.MinRamGb!.Value.ToString("0.#"), RamText));
        else if (current.BelowRecRam) warnings.Add(Loc.F("Fps_WarnRecRam", game.RecRamGb!.Value.ToString("0.#"), RamText));
        // VRAM só é conferida na placa detectada (numa GPU escolhida à mão não sabemos quanto há).
        if (_vramGb is not null && !IsGpuOverridden && FpsEstimator.RequiredVramGb(game.MinGpu) is { } minVram
            && _vramGb.Value + 0.25 < minVram)
            warnings.Add(Loc.F("Fps_WarnVram", minVram.ToString("0.#"), _vramGb.Value.ToString("0.#")));
        if (game.FpsCap is > 0 and < FpsEstimator.GlobalCap) warnings.Add(Loc.F("Fps_WarnCap", game.FpsCap.Value));
        if (GpuIsApproximate) warnings.Add(Loc.S("Fps_WarnApproxGpu"));
        // A base só modela a GPU. Muito acima de 150 FPS o processador costuma ser o limite, e
        // CPUs com menos de 4 threads nem abrem boa parte dos jogos atuais.
        if (!current.HitCap && current.Fps > 150) warnings.Add(Loc.S("Fps_WarnCpuBound"));
        if (Environment.ProcessorCount < 4 && game.Year is >= 2015) warnings.Add(Loc.F("Fps_WarnFewThreads", Environment.ProcessorCount));

        // Comparação da GPU do usuário com as exigidas, em % do score.
        // Muito acima (3× ou mais) a porcentagem exata não diz nada útil — vira "bem acima".
        string Compare(int? required)
        {
            if (required is not > 0) return "";
            int pct = (int)Math.Round(score * 100.0 / required.Value);
            return pct >= 300 ? Loc.S("Fps_GpuWayAbove") : Loc.F("Fps_GpuVsRequired", pct);
        }

        return new GameFpsDetailViewModel
        {
            Name = game.Name,
            Year = game.Year?.ToString() ?? "",
            Rows = rows,
            Suggestion = suggestion,
            Warnings = warnings,
            MinGpu = Fallback(game.MinGpu),
            MinCpu = Fallback(game.MinCpu),
            MinRam = game.MinRamGb is > 0 ? $"{game.MinRamGb:0.#} GB" : "—",
            MinGpuCompare = Compare(game.MinG3d),
            RecGpu = Fallback(game.RecGpu),
            RecCpu = Fallback(game.RecCpu),
            RecRam = game.RecRamGb is > 0 ? $"{game.RecRamGb:0.#} GB" : "—",
            RecGpuCompare = Compare(game.RecG3d),
            HasRecommended = !string.IsNullOrWhiteSpace(game.RecGpu) || game.RecRamGb is > 0,
            PageUrl = game.PageUrl,
        };

        static string Fallback(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    public static string PresetLabel(GraphicsPreset p) => Loc.S(p switch
    {
        GraphicsPreset.Low => "Fps_PresetLow",
        GraphicsPreset.Medium => "Fps_PresetMedium",
        GraphicsPreset.Ultra => "Fps_PresetUltra",
        _ => "Fps_PresetHigh",
    });

    public static string VerdictLabel(RunVerdict v) => Loc.S(v switch
    {
        RunVerdict.CannotRun => "Fps_VerdictCannotRun",
        RunVerdict.Struggles => "Fps_VerdictStruggles",
        RunVerdict.Playable => "Fps_VerdictPlayable",
        RunVerdict.Smooth => "Fps_VerdictSmooth",
        _ => "Fps_VerdictVerySmooth",
    });

    public static string VerdictColor(RunVerdict v) => v switch
    {
        RunVerdict.CannotRun => "#DC2626",
        RunVerdict.Struggles => "#F97316",
        RunVerdict.Playable => "#EAB308",
        RunVerdict.Smooth => "#22C55E",
        _ => "#14B8A6",
    };

    public static string RangeText(FpsEstimate e)
    {
        if (e.HitCap)
            return e.Cap >= FpsEstimator.GlobalCap ? $"{FpsEstimator.GlobalCap}+ FPS" : Loc.F("Fps_CappedAt", e.Cap);
        if (e.Fps < 1) return "< 1 FPS";
        return $"{e.RangeLow}-{e.RangeHigh} FPS";
    }
}

/// <summary>Linha da lista de jogos.</summary>
public sealed class GameFpsItemViewModel
{
    public GameFpsItemViewModel(GameSpec game, GraphicsPreset preset, FpsEstimate estimate, bool inLibrary)
    {
        Game = game;
        Preset = preset;
        Estimate = estimate;
        IsInLibrary = inLibrary;
    }

    public GameSpec Game { get; }
    public GraphicsPreset Preset { get; }
    public FpsEstimate Estimate { get; }
    public bool IsInLibrary { get; }
    /// <summary>Preset em que a faixa de FPS foi calculada (o melhor para 60 FPS).</summary>
    public string PresetText => FpsInGameViewModel.PresetLabel(Preset);

    public string Name => Game.Name;
    public string Year => Game.Year?.ToString() ?? "";
    public string RangeText => FpsInGameViewModel.RangeText(Estimate);
    public string VerdictText => FpsInGameViewModel.VerdictLabel(Estimate.Verdict);
    public string VerdictColor => FpsInGameViewModel.VerdictColor(Estimate.Verdict);
}

/// <summary>FPS de um preset na resolução escolhida, no painel de detalhes.</summary>
public sealed class PresetRowViewModel
{
    public PresetRowViewModel(string preset, FpsEstimate estimate, bool isCurrent)
    {
        Preset = preset;
        Estimate = estimate;
        IsCurrent = isCurrent;
    }

    public string Preset { get; }
    public FpsEstimate Estimate { get; }
    public bool IsCurrent { get; }
    public string RangeText => FpsInGameViewModel.RangeText(Estimate);
    public string VerdictText => FpsInGameViewModel.VerdictLabel(Estimate.Verdict);
    public string VerdictColor => FpsInGameViewModel.VerdictColor(Estimate.Verdict);
}

/// <summary>Painel de detalhes do jogo selecionado.</summary>
public sealed class GameFpsDetailViewModel
{
    public string Name { get; init; } = "";
    public string Year { get; init; } = "";
    public IReadOnlyList<PresetRowViewModel> Rows { get; init; } = Array.Empty<PresetRowViewModel>();
    public string Suggestion { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool HasWarnings => Warnings.Count > 0;
    public string MinGpu { get; init; } = "";
    public string MinCpu { get; init; } = "";
    public string MinRam { get; init; } = "";
    public string MinGpuCompare { get; init; } = "";
    public string RecGpu { get; init; } = "";
    public string RecCpu { get; init; } = "";
    public string RecRam { get; init; } = "";
    public string RecGpuCompare { get; init; } = "";
    public bool HasRecommended { get; init; }
    public string PageUrl { get; init; } = "";
}
