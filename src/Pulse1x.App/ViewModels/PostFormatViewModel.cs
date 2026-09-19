using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>Um aplicativo na lista, com seleção, status de instalação e visibilidade de busca.</summary>
public partial class AppItemViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public AppEntry Entry { get; }

    public AppItemViewModel(AppEntry entry, Action onSelectionChanged)
    {
        Entry = entry;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Name => Entry.Name;
    public string Publisher => Entry.Publisher;
    public string Size => Entry.Size;
    public string Description => Entry.Description;
    public bool OpensOfficialPage => !Entry.UsesWinget;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isRecommended;
    [ObservableProperty] private bool isVisible = true;

    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string statusColor = "#808080";

    public bool HasStatus => StatusText.Length > 0;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>Reavalia textos vindos do idioma atual (descrição) após troca de idioma.</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Description));

    public void SetStatus(string text, string color)
    {
        StatusText = text;
        StatusColor = color;
    }
}

/// <summary>Um grupo de categoria com seu cabeçalho, ícone e itens.</summary>
public partial class CategoryGroupViewModel : ObservableObject
{
    public InstallCategory Category { get; }
    public string Icon { get; }
    public ObservableCollection<AppItemViewModel> Items { get; }

    public CategoryGroupViewModel(InstallCategory category, string icon, IEnumerable<AppItemViewModel> items)
    {
        Category = category;
        Icon = icon;
        Items = new ObservableCollection<AppItemViewModel>(items);
        // Drivers e fabricantes é a maior lista de todas e empurrava o resto da página para fora da
        // tela; ela começa recolhida, e as demais categorias seguem abertas como antes.
        isExpanded = category != InstallCategory.Drivers;
    }

    [ObservableProperty] private bool isVisible = true;

    [ObservableProperty] private bool isExpanded;

    /// <summary>Contagem exibida no cabeçalho quando o grupo está recolhido.</summary>
    public string CountLabel => Items.Count.ToString();

    public string Header => Loc.S(HeaderKey);
    public void RefreshLanguage() => OnPropertyChanged(nameof(Header));

    private string HeaderKey => Category switch
    {
        InstallCategory.Drivers => "Pf_Cat_Drivers",
        InstallCategory.Components => "Pf_Cat_Components",
        InstallCategory.Browsers => "Pf_Cat_Browsers",
        InstallCategory.Communication => "Pf_Cat_Communication",
        InstallCategory.Games => "Pf_Cat_Games",
        InstallCategory.Utilities => "Pf_Cat_Utilities",
        InstallCategory.Multimedia => "Pf_Cat_Multimedia",
        InstallCategory.Productivity => "Pf_Cat_Productivity",
        InstallCategory.Development => "Pf_Cat_Development",
        InstallCategory.Security => "Pf_Cat_Security",
        InstallCategory.Cloud => "Pf_Cat_Cloud",
        _ => "Pf_Cat_Utilities",
    };
}

/// <summary>Uma Configuração Recomendada selecionável.</summary>
public partial class RecommendedSettingViewModel : ObservableObject
{
    public RecommendedSetting Setting { get; }

    public RecommendedSettingViewModel(RecommendedSetting setting)
    {
        Setting = setting;
        IsSelected = setting.RecommendedDefault;
    }

    public string Title => Setting.Title;
    public string Description => Setting.Description;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string statusColor = "#808080";

    public bool HasStatus => StatusText.Length > 0;
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>Uma linha no resumo final das instalações.</summary>
public sealed class InstallSummaryLine
{
    public required string Text { get; init; }
    public required string Color { get; init; }
    public required string Icon { get; init; }
}

/// <summary>
/// Cérebro da Central Pós-Formatação: detecção inteligente do hardware, lista pesquisável de
/// aplicativos por categoria, seleção (manual, recomendados ou por perfil), fila de instalação
/// via winget (fonte oficial) com progresso e resumo, e as configurações recomendadas do Windows.
/// </summary>
public partial class PostFormatViewModel : ObservableObject
{
    private readonly AppInstallService _install;
    private readonly PostFormatTweaksService _tweaks;
    private readonly List<AppItemViewModel> _allItems = new();

    public ObservableCollection<CategoryGroupViewModel> Groups { get; } = new();
    public ObservableCollection<RecommendedSettingViewModel> Settings { get; } = new();
    public ObservableCollection<string> InstallLog { get; } = new();
    public ObservableCollection<InstallSummaryLine> Summary { get; } = new();

    /// <summary>
    /// Debloater: o Detector de Bloatware exposto aqui, ao lado da instalação. Depois de formatar,
    /// tirar o que veio de fábrica é a outra metade do trabalho — por isso as duas coisas moram na
    /// mesma página. É uma instância própria (a de Otimizações continua existindo), pois um mesmo
    /// ViewModel renderizado em duas páginas brigaria pelos mesmos elementos visuais.
    /// </summary>
    public BloatwareDetectorViewModel Debloater { get; }

    public PostFormatViewModel(AppInstallService install, PostFormatTweaksService tweaks,
        BloatwareDetectorService bloatwareDetector)
    {
        _install = install;
        _tweaks = tweaks;
        Debloater = new BloatwareDetectorViewModel(bloatwareDetector);

        BuildCatalog();

        foreach (var s in PostFormatCatalog.Settings)
            Settings.Add(new RecommendedSettingViewModel(s));

        Loc.Instance.LanguageChanged += OnLanguageChanged;

        _ = DetectAsync();
    }

    private void BuildCatalog()
    {
        string IconFor(InstallCategory c) => c switch
        {
            InstallCategory.Drivers => "🧩",
            InstallCategory.Components => "⚙️",
            InstallCategory.Browsers => "🌐",
            InstallCategory.Communication => "💬",
            InstallCategory.Games => "🎮",
            InstallCategory.Utilities => "🛠️",
            InstallCategory.Multimedia => "🎬",
            InstallCategory.Productivity => "📄",
            InstallCategory.Development => "👨‍💻",
            InstallCategory.Security => "🛡️",
            InstallCategory.Cloud => "☁️",
            _ => "📦",
        };

        foreach (var category in Enum.GetValues<InstallCategory>())
        {
            var items = PostFormatCatalog.Apps
                .Where(a => a.Category == category)
                .Select(a => new AppItemViewModel(a, UpdateSelectionState))
                .ToList();
            if (items.Count == 0) continue;
            _allItems.AddRange(items);
            Groups.Add(new CategoryGroupViewModel(category, IconFor(category), items));
        }
    }

    // ===================== Detecção Inteligente =====================

    [ObservableProperty] private bool isDetecting = true;
    [ObservableProperty] private bool hasDetection;
    [ObservableProperty] private string gpuText = "—";
    [ObservableProperty] private string cpuText = "—";
    [ObservableProperty] private string windowsText = "—";
    [ObservableProperty] private string archText = "—";
    [ObservableProperty] private string oemText = "—";

    // Começa "disponível" para não piscar o aviso antes da checagem; o valor real é definido
    // em DetectAsync, numa thread de fundo (a checagem do winget pode demorar alguns segundos
    // na primeira execução e travaria o startup se fosse feita na thread de UI).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWingetWarning))]
    private bool wingetAvailable = true;

    public bool ShowWingetWarning => !WingetAvailable;

    private async Task DetectAsync()
    {
        try
        {
            // Roda a checagem do winget e a detecção de hardware em paralelo (ambas em segundo
            // plano) para não atrasar o card de detecção caso o winget demore a responder.
            var wingetTask = Task.Run(() => _install.IsWingetAvailable);
            var profileTask = _install.DetectSystemAsync();
            WingetAvailable = await wingetTask;
            var profile = await profileTask;
            GpuText = profile.GpuName;
            CpuText = profile.CpuName;
            WindowsText = profile.WindowsVersion;
            ArchText = profile.Architecture;
            OemText = profile.OemManufacturer;

            // Marca como recomendado: itens "essenciais por padrão" + compatíveis com o hardware.
            foreach (var item in _allItems)
            {
                bool vendorMatch = item.Entry.Vendor != HwVendor.None && profile.Vendors.Contains(item.Entry.Vendor);
                item.IsRecommended = item.Entry.RecommendedDefault || vendorMatch;
            }
            HasDetection = true;
        }
        catch
        {
            // Detecção falhou: ainda marca os recomendados padrão para o botão funcionar.
            foreach (var item in _allItems)
                item.IsRecommended = item.Entry.RecommendedDefault;
        }
        finally
        {
            IsDetecting = false;
        }
    }

    // ===================== Busca =====================

    [ObservableProperty] private string searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        string q = value.Trim();
        foreach (var item in _allItems)
        {
            item.IsVisible = q.Length == 0
                || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var group in Groups)
            group.IsVisible = group.Items.Any(i => i.IsVisible);
    }

    // ===================== Seleção =====================

    [ObservableProperty] private int selectedCount;
    public bool AnySelected => SelectedCount > 0;
    public string SelectedCountText => Loc.F("Pf_SelectedCount", SelectedCount);

    private void UpdateSelectionState()
    {
        SelectedCount = _allItems.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(AnySelected));
        OnPropertyChanged(nameof(SelectedCountText));
        InstallSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var item in _allItems)
            if (item.IsRecommended) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in _allItems)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void ApplyProfile(string profileName)
    {
        if (!Enum.TryParse<InstallProfile>(profileName, out var profile)) return;

        foreach (var item in _allItems)
            item.IsSelected = false;

        var ids = new HashSet<string>(PostFormatCatalog.ProfileAppIds(profile));

        // Perfis Gamer e Completo incluem os drivers compatíveis com o hardware detectado.
        bool includeDrivers = profile is InstallProfile.Gamer or InstallProfile.Complete;

        foreach (var item in _allItems)
        {
            bool inProfile = ids.Contains(item.Entry.Id);
            bool driverMatch = includeDrivers && item.Entry.Category == InstallCategory.Drivers
                && item.IsRecommended && item.Entry.UsesWinget;
            if (inProfile || driverMatch)
                item.IsSelected = true;
        }
    }

    // ===================== Instalação =====================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool isInstalling;

    [ObservableProperty] private double installProgress;
    [ObservableProperty] private string currentAppText = "";
    [ObservableProperty] private string currentStatusText = "";
    [ObservableProperty] private bool hasSummary;
    [ObservableProperty] private string summaryHeadline = "";

    private bool CanInstall() => AnySelected && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedAsync()
    {
        if (IsInstalling) return;
        var queue = _allItems.Where(i => i.IsSelected).ToList();
        if (queue.Count == 0) return;

        IsInstalling = true;
        HasSummary = false;
        InstallLog.Clear();
        Summary.Clear();
        InstallProgress = 0;

        int done = 0, ok = 0, manual = 0, failed = 0;
        var progress = new Progress<string>(line =>
        {
            CurrentStatusText = line;
            InstallLog.Add(line);
            if (InstallLog.Count > 200) InstallLog.RemoveAt(0);
        });

        foreach (var item in queue)
        {
            CurrentAppText = item.Name;
            CurrentStatusText = Loc.S("Pf_StatusInstalling");
            item.SetStatus(Loc.S("Pf_StatusInstalling"), "#DC2626");
            InstallLog.Add($"━━ {item.Name} ━━");

            InstallResult result;
            try
            {
                result = await _install.InstallAsync(item.Entry, progress);
            }
            catch (Exception ex)
            {
                result = new InstallResult(InstallOutcome.Failed, ex.Message);
            }

            switch (result.Outcome)
            {
                case InstallOutcome.Success:
                    item.SetStatus(Loc.S("Pf_StatusInstalled"), "#22C55E");
                    ok++;
                    AddSummary("✅", item.Name, Loc.S("Pf_SumInstalled"), "#22C55E");
                    break;
                case InstallOutcome.AlreadyInstalled:
                    item.SetStatus(Loc.S("Pf_StatusAlready"), "#16A34A");
                    ok++;
                    AddSummary("✅", item.Name, Loc.S("Pf_SumAlready"), "#16A34A");
                    break;
                case InstallOutcome.OpenedOfficialPage:
                    item.SetStatus(Loc.S("Pf_StatusOpened"), "#F59E0B");
                    manual++;
                    AddSummary("🌐", item.Name, Loc.S("Pf_SumOpened"), "#F59E0B");
                    break;
                default:
                    item.SetStatus(Loc.S("Pf_StatusFailed"), "#DC2626");
                    failed++;
                    AddSummary("❌", item.Name, string.IsNullOrWhiteSpace(result.Message)
                        ? Loc.S("Pf_SumFailed") : result.Message, "#DC2626");
                    break;
            }

            done++;
            InstallProgress = done * 100.0 / queue.Count;
        }

        CurrentAppText = "";
        CurrentStatusText = "";
        SummaryHeadline = Loc.F("Pf_SummaryHeadline", ok, manual, failed);
        HasSummary = true;
        IsInstalling = false;
    }

    private void AddSummary(string icon, string name, string detail, string color) =>
        Summary.Add(new InstallSummaryLine { Icon = icon, Text = $"{name} — {detail}", Color = color });

    [RelayCommand]
    private void OpenWingetPage() => AppInstallService.OpenWingetInstallPage();

    // ===================== Configurações Recomendadas =====================

    [ObservableProperty] private bool isApplyingSettings;
    [ObservableProperty] private string settingsStatus = "";

    [RelayCommand]
    private void SelectRecommendedSettings()
    {
        foreach (var s in Settings)
            s.IsSelected = s.Setting.RecommendedDefault;
    }

    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        if (IsApplyingSettings) return;
        var queue = Settings.Where(s => s.IsSelected).ToList();
        if (queue.Count == 0)
        {
            SettingsStatus = Loc.S("Pf_NothingSelected");
            return;
        }

        IsApplyingSettings = true;
        int ok = 0, fail = 0;
        foreach (var s in queue)
        {
            s.StatusText = Loc.S("Pf_StatusApplying");
            s.StatusColor = "#DC2626";
            try
            {
                var r = await _tweaks.ApplyAsync(s.Setting.Id);
                if (r.ExitCode == 0) { s.StatusText = Loc.S("Pf_StatusApplied"); s.StatusColor = "#22C55E"; ok++; }
                else { s.StatusText = Loc.S("Pf_StatusFailed"); s.StatusColor = "#DC2626"; fail++; }
            }
            catch
            {
                s.StatusText = Loc.S("Pf_StatusFailed");
                s.StatusColor = "#DC2626";
                fail++;
            }
        }

        SettingsStatus = Loc.F("Pf_SettingsResult", ok, fail);
        IsApplyingSettings = false;
    }

    // ===================== Sobre =====================

    [ObservableProperty] private bool isAboutExpanded;

    [RelayCommand]
    private void ToggleAbout() => IsAboutExpanded = !IsAboutExpanded;

    // ===================== Idioma =====================

    private void OnLanguageChanged()
    {
        foreach (var item in _allItems) item.RefreshLanguage();
        foreach (var group in Groups) group.RefreshLanguage();
        foreach (var s in Settings) s.RefreshLanguage();
        OnPropertyChanged(nameof(SelectedCountText));
    }
}
