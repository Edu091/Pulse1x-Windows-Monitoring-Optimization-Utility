using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Card de uma otimização avançada individual. Antes de ser exibido, seu estado é
/// verificado automaticamente (<see cref="RefreshStateAsync"/>) para refletir se a
/// otimização já está ativa. O interruptor aplica/reverte através do serviço.
/// </summary>
public partial class AdvancedOptimizationViewModel : ObservableObject
{
    private readonly AdvancedOptimization _model;
    private readonly AdvancedOptimizationService _service;

    // Suprime o handler do interruptor quando NÓS atualizamos IsApplied a partir do estado real
    // (para não disparar uma nova aplicação/reversão em loop).
    private bool _suppressToggle;

    // Guarda síncrona de re-entrância: marcada ANTES de qualquer await. Enquanto uma operação
    // está em voo, novos cliques e o RefreshAll automático não podem sobrescrever o interruptor —
    // era essa corrida que fazia o toggle "voltar sozinho" ou "não desativar".
    private bool _operationInFlight;

    public string Id => _model.Id;
    public string Icon => _model.Icon;
    public string Title => _model.Title;
    public string Description => _model.Description;
    public string Category => _model.Category;

    public string? Warning => _model.Warning;
    public bool HasWarning => !string.IsNullOrEmpty(_model.Warning);

    [ObservableProperty] private bool isApplied;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isAvailable = true;
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private string stateText = Loc.S("AdvOpt_Checking");
    [ObservableProperty] private string? stateDetail;
    [ObservableProperty] private string statusMessage = "";

    public bool HasStatus => StatusMessage.Length > 0;
    public bool HasStateDetail => !string.IsNullOrEmpty(StateDetail);

    /// <summary>O interruptor só aceita clique quando a otimização é aplicável e nada está em andamento.</summary>
    public bool CanToggle => IsAvailable && !IsBusy;

    public AdvancedOptimizationViewModel(AdvancedOptimization model, AdvancedOptimizationService service)
    {
        _model = model;
        _service = service;
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnStateDetailChanged(string? value) => OnPropertyChanged(nameof(HasStateDetail));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
    partial void OnIsAvailableChanged(bool value) => OnPropertyChanged(nameof(CanToggle));

    /// <summary>Verifica o estado real da otimização no sistema e atualiza o interruptor.</summary>
    public async Task RefreshStateAsync()
    {
        // Não mexer no interruptor enquanto o usuário tem uma aplicação/reversão em andamento:
        // o ReconcileStateAsync da própria operação cuidará do estado final.
        if (_operationInFlight) return;

        try
        {
            if (_model.IsAvailableAsync is not null)
                IsAvailable = await _model.IsAvailableAsync();

            if (!IsAvailable)
            {
                StateText = Loc.S("AdvOpt_NotAvailableOnThisWindows");
                return;
            }

            await ReadAndApplyRealStateAsync();
        }
        catch
        {
            StateText = Loc.S("AdvOpt_CouldNotCheckState");
        }
    }

    // Lê o estado real e posiciona o interruptor de acordo, sem disparar o handler.
    private async Task ReadAndApplyRealStateAsync()
    {
        bool applied = await _model.IsAppliedAsync();

        _suppressToggle = true;
        IsApplied = applied;
        _suppressToggle = false;

        StateText = applied ? Loc.S("AdvOpt_StateOn") : Loc.S("AdvOpt_StateOff");

        if (_model.StateDetailAsync is not null)
            StateDetail = await _model.StateDetailAsync();
    }

    partial void OnIsAppliedChanged(bool value)
    {
        if (_suppressToggle) return;
        _ = ToggleAsync(value);
    }

    private async Task ToggleAsync(bool apply)
    {
        // Guarda síncrona — impede que um segundo clique ou o refresh automático entre antes do
        // primeiro await. Sem isso o interruptor podia ser sobrescrito no meio da operação.
        if (_operationInFlight) return;
        _operationInFlight = true;
        IsBusy = true;
        StatusMessage = "";

        try
        {
            if (apply)
            {
                await _model.ApplyAsync();
                StatusMessage = "✅ " + Loc.S("AdvOpt_OptimizationApplied");
            }
            else
            {
                await _service.RevertOptimizationAsync(_model.Id);
                StatusMessage = "↩ " + Loc.S("AdvOpt_SettingsRestored");
            }
        }
        catch (Exception ex)
        {
            // A alteração pode ter falhado parcialmente; o estado real é relido abaixo.
            StatusMessage = "❌ " + Loc.S("AdvOpt_FailedShort") + ex.Message;
        }
        finally
        {
            // Relê o estado real UMA vez e sincroniza o interruptor com ele, AINDA ocupado. Se a
            // operação realmente não mudou o sistema, o interruptor volta à posição real — caso
            // contrário, permanece onde o usuário deixou. Só então liberamos o controle: assim não
            // há janela em que um clique rápido seria ignorado em silêncio.
            try { await ReadAndApplyRealStateAsync(); }
            catch { StateText = Loc.S("AdvOpt_CouldNotCheckState"); }
            IsBusy = false;
            _operationInFlight = false;
        }
    }
}

/// <summary>
/// Card de um perfil de Ajustes Visuais (Melhor Aparência / Equilibrado / Máximo Desempenho).
/// </summary>
public partial class VisualProfileViewModel : ObservableObject
{
    public VisualProfile Profile { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty] private bool isActive;

    public VisualProfileViewModel(VisualProfile profile, string title, string description)
    {
        Profile = profile;
        Title = title;
        Description = description;
    }
}

/// <summary>
/// ViewModel da ferramenta "Otimizações Avançadas". Agrupa as otimizações por categoria,
/// gerencia os perfis de Ajustes Visuais e expõe o histórico de reversão (todas as alterações
/// feitas, com data/hora, valor anterior e novo).
/// </summary>
public partial class AdvancedOptimizationsViewModel : ObservableObject
{
    private readonly AdvancedOptimizationService _service;

    [ObservableProperty] private bool isSectionExpanded;
    [ObservableProperty] private bool isExplainerExpanded;
    [ObservableProperty] private bool isHistoryExpanded;
    [ObservableProperty] private bool isRefreshing;

    public ObservableCollection<AdvancedOptimizationViewModel> PrivacyOptimizations { get; } = new();
    public ObservableCollection<AdvancedOptimizationViewModel> PerformanceOptimizations { get; } = new();
    public ObservableCollection<VisualProfileViewModel> VisualProfiles { get; } = new();
    public ObservableCollection<OptimizationChange> History { get; } = new();

    public bool HasHistory => History.Count > 0;

    /// <summary>Ferramenta aninhada: Detector de Bloatware (análise e recomendação seguras).</summary>
    public BloatwareDetectorViewModel Detector { get; }

    public AdvancedOptimizationsViewModel(AdvancedOptimizationService service, BloatwareDetectorService detector)
    {
        _service = service;
        Detector = new BloatwareDetectorViewModel(detector);

        BuildOptimizationCards();
        BuildVisualProfiles();

        service.OptimizationsChanged += OnOptimizationsChanged;
    }

    private void BuildOptimizationCards()
    {
        PrivacyOptimizations.Clear();
        PerformanceOptimizations.Clear();

        foreach (var opt in _service.Optimizations)
        {
            var vm = new AdvancedOptimizationViewModel(opt, _service);
            if (opt.Category == "Privacidade") PrivacyOptimizations.Add(vm);
            else PerformanceOptimizations.Add(vm);
        }
    }

    private void BuildVisualProfiles()
    {
        VisualProfiles.Clear();
        VisualProfiles.Add(new VisualProfileViewModel(VisualProfile.BestAppearance,
            Loc.S("AdvOpt_BestAppearanceTitle"), Loc.S("AdvOpt_BestAppearanceDesc")));
        VisualProfiles.Add(new VisualProfileViewModel(VisualProfile.Balanced,
            Loc.S("AdvOpt_BalancedVisualTitle"), Loc.S("AdvOpt_BalancedVisualDesc")));
        VisualProfiles.Add(new VisualProfileViewModel(VisualProfile.BestPerformance,
            Loc.S("AdvOpt_BestPerformanceTitle"), Loc.S("AdvOpt_BestPerformanceDesc")));
        RefreshVisualProfiles();
    }

    // Idioma trocado: a lista de otimizações foi reconstruída com os novos textos —
    // refaz os cartões e reconsulta o estado real de cada um.
    private void OnOptimizationsChanged()
    {
        BuildOptimizationCards();
        BuildVisualProfiles();
        _ = RefreshAllAsync();
    }

    /// <summary>Verifica automaticamente o estado de todas as otimizações ao abrir a seção.</summary>
    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            foreach (var vm in PrivacyOptimizations) await vm.RefreshStateAsync();
            foreach (var vm in PerformanceOptimizations) await vm.RefreshStateAsync();
            RefreshVisualProfiles();
            RefreshHistory();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RefreshVisualProfiles()
    {
        foreach (var p in VisualProfiles)
            p.IsActive = _service.IsVisualProfileApplied(p.Profile);
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var c in _service.ChangeLog.GetAllActive())
            History.Add(c);
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private void ApplyVisualProfile(VisualProfileViewModel profile)
    {
        _service.ApplyVisualProfile(profile.Profile);
        RefreshVisualProfiles();
        RefreshHistory();
    }

    [RelayCommand]
    private async Task RevertChangeAsync(OptimizationChange change)
    {
        await _service.RevertChangeAsync(change);
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            Loc.S("AdvOpt_RevertAllConfirm"),
            Loc.S("AdvOpt_RevertAllTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        foreach (var change in _service.ChangeLog.GetAllActive())
            await _service.RevertChangeAsync(change);

        await RefreshAllAsync();
    }

    partial void OnIsSectionExpandedChanged(bool value)
    {
        if (value) _ = RefreshAllAsync();
    }
}
