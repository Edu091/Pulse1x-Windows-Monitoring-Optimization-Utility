using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using Pulse1x.App.Services;
using Pulse1x.App.Views;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// ViewModel da central "Status dos Servidores" (dentro da categoria Latência). Monitora, em tempo
/// real, o status de serviços online (jogos, comunicação, streaming, IA, infraestrutura e nuvem),
/// agrupados por empresa em cartões expansíveis. Atualiza automaticamente em segundo plano enquanto
/// a página está visível e permite atualização manual, pesquisa instantânea e a abertura de uma
/// janela de detalhes por serviço. Segue a regra bilíngue (assina <see cref="Loc.LanguageChanged"/>).
/// </summary>
public partial class ServerStatusViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(90);

    private readonly ServerStatusService _service;
    private readonly DispatcherTimer _timer;
    private bool _loadedOnce;

    /// <summary>Todas as empresas monitoradas (fonte fixa; nunca filtrada).</summary>
    public ObservableCollection<CompanyStatusViewModel> AllCompanies { get; } = new();

    /// <summary>Empresas exibidas após aplicar a pesquisa (o que a interface percorre).</summary>
    public ObservableCollection<CompanyStatusViewModel> Companies { get; } = new();

    [ObservableProperty] private bool isSectionExpanded = true;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string lastUpdateText = "—";
    [ObservableProperty] private int monitoredServiceCount;
    public int MonitoredCompanyCount => AllCompanies.Count;

    public ServerStatusViewModel(ServerStatusService service)
    {
        _service = service;

        foreach (var def in StatusCatalog.Companies)
            AllCompanies.Add(new CompanyStatusViewModel(def));
        ApplyFilter();
        RecountServices();

        Loc.Instance.LanguageChanged += OnLanguageChanged;

        _timer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    /// <summary>Liga/desliga a atualização automática conforme a página fica visível (economia de rede).
    /// No primeiro acesso dispara uma carga inicial.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _timer.Start();
            if (!_loadedOnce) _ = RefreshAsync();
        }
        else
        {
            _timer.Stop();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    // Pesquisa instantânea: mantém a empresa se o nome dela OU o nome de algum serviço bater.
    private void ApplyFilter()
    {
        string term = SearchText.Trim();
        Companies.Clear();
        foreach (var c in AllCompanies)
            if (c.Matches(term))
                Companies.Add(c);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            // Cada empresa se atualiza e aplica o resultado assim que sua própria consulta termina,
            // então os cartões vão "acendendo" progressivamente em vez de esperar todos.
            await Task.WhenAll(AllCompanies.Select(c => c.RefreshAsync(_service)));
            _loadedOnce = true;
            LastUpdateText = DateTime.Now.ToString("HH:mm:ss");
            RecountServices();
        }
        finally { IsRefreshing = false; }
    }

    private void RecountServices() =>
        MonitoredServiceCount = AllCompanies.Sum(c => c.Services.Count);

    private void OnLanguageChanged()
    {
        foreach (var c in AllCompanies) c.RefreshTexts();
    }

    public void Dispose()
    {
        _timer.Stop();
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Cartão expansível de uma empresa: indicador geral, contagem de serviços e a lista de
/// serviços (revelada ao expandir). Guarda os estados em enum para reavaliar os textos ao trocar
/// de idioma sem uma nova consulta de rede.</summary>
public partial class CompanyStatusViewModel : ObservableObject
{
    private readonly StatusCompanyDef _def;
    private ServiceState _overallState = ServiceState.Unknown;

    public string Name => _def.Name;
    public string Icon => _def.Icon;
    public string StatusUrl => _def.StatusUrl;
    public string CategoryLabel => Loc.S(StatusCatalog.CategoryKey(_def.Category));

    public ObservableCollection<ServiceStatusViewModel> Services { get; } = new();

    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string overallEmoji = "⚪";
    [ObservableProperty] private string overallColor = "#9CA3AF";
    [ObservableProperty] private string overallLabel = "";
    [ObservableProperty] private string incidentSummary = "";
    [ObservableProperty] private bool hasIncident;

    public string ServiceCountText => Services.Count.ToString();

    public CompanyStatusViewModel(StatusCompanyDef def)
    {
        _def = def;
        // Estado inicial: serviços curados como "status indisponível" até a primeira consulta.
        foreach (var name in def.FallbackServices)
            Services.Add(new ServiceStatusViewModel(name, ServiceState.Unknown, null, def));
        ApplyOverall(ServiceState.Unknown);
    }

    /// <summary>True se a empresa deve aparecer para o termo de pesquisa (nome próprio ou de um serviço).</summary>
    public bool Matches(string term)
    {
        if (string.IsNullOrEmpty(term)) return true;
        if (Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        return Services.Any(s => s.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshAsync(ServerStatusService service)
    {
        IsLoading = true;
        try
        {
            var result = await service.FetchAsync(_def);

            Services.Clear();
            foreach (var s in result.Services)
                Services.Add(new ServiceStatusViewModel(s.Name, s.State, s.Incident, _def));
            OnPropertyChanged(nameof(ServiceCountText));

            ApplyOverall(result.Overall);
            IncidentSummary = result.IncidentSummary ?? "";
            HasIncident = !string.IsNullOrEmpty(IncidentSummary);
        }
        finally { IsLoading = false; }
    }

    private void ApplyOverall(ServiceState state)
    {
        _overallState = state;
        OverallEmoji = ServiceStatusVisuals.Emoji(state);
        OverallColor = ServiceStatusVisuals.Hex(state);
        OverallLabel = Loc.S(ServiceStatusVisuals.LabelKey(state));
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(CategoryLabel));
        OverallLabel = Loc.S(ServiceStatusVisuals.LabelKey(_overallState));
        foreach (var s in Services) s.RefreshTexts();
    }
}

/// <summary>Linha de um serviço dentro do cartão da empresa: nome + status atual. Ao clicar, abre a
/// janela de detalhes (com o incidente ativo, quando houver, e o link oficial).</summary>
public partial class ServiceStatusViewModel : ObservableObject
{
    private ServiceState _state;
    private readonly ServiceIncident? _incident;
    private readonly StatusCompanyDef _company;

    public string Name { get; }

    [ObservableProperty] private string emoji = "⚪";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string color = "#9CA3AF";

    public ServiceStatusViewModel(string name, ServiceState state, ServiceIncident? incident, StatusCompanyDef company)
    {
        Name = name;
        _incident = incident;
        _company = company;
        _state = state;
        ApplyState(state);
    }

    private void ApplyState(ServiceState state)
    {
        _state = state;
        Emoji = ServiceStatusVisuals.Emoji(state);
        Color = ServiceStatusVisuals.Hex(state);
        StatusText = Loc.S(ServiceStatusVisuals.LabelKey(state));
    }

    public void RefreshTexts() => StatusText = Loc.S(ServiceStatusVisuals.LabelKey(_state));

    [RelayCommand]
    private void ShowDetail()
    {
        var window = new ServiceDetailWindow(new ServiceDetailContext(
            _company.Name, _company.Icon, Name, _state, _company.StatusUrl, _incident))
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        window.ShowDialog();
    }
}
