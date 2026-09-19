using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Card de um item detectado pelo Detector de Bloatware. Expõe a análise (impacto, risco,
/// recomendação) em forma de selos visuais e o painel "Mais Informações" educativo. As ações
/// (Desativar/Reativar/Desinstalar/Ignorar) só ocorrem por clique e, quando alteram o
/// sistema, exigem confirmação, oferecem ponto de restauração e são registradas no histórico.
/// </summary>
public partial class BloatwareItemViewModel : ObservableObject
{
    private readonly BloatwareItem _item;
    private readonly BloatwareDetectorService _service;
    private readonly Action _onChanged;

    public BloatwareItem Model => _item;
    public string Key => _item.Key;

    [ObservableProperty] private bool isExpanded;          // painel "Mais Informações"
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool currentlyEnabled;

    public BloatwareItemViewModel(BloatwareItem item, BloatwareDetectorService service, Action onChanged)
    {
        _item = item;
        _service = service;
        _onChanged = onChanged;
        currentlyEnabled = item.CurrentlyEnabled;
    }

    // ---- Identificação ----
    public string Name => _item.Name;
    public string Vendor => _item.Vendor;
    public string CategoryLabel => _item.CategoryLabel;
    public string Description => _item.Description;

    public string Icon => _item.Kind switch
    {
        BloatKind.App => "📦",
        BloatKind.Service => "⚙️",
        BloatKind.ScheduledTask => "🗓️",
        BloatKind.StartupItem => "🚀",
        _ => "•",
    };

    // ---- Selos de análise ----
    public string ImpactBadge => _item.Impact switch
    {
        ImpactLevel.VeryLow => "🟢 " + Loc.S("Bloat_ImpactVeryLow"),
        ImpactLevel.Low => "🟢 " + Loc.S("Bloat_ImpactLow"),
        ImpactLevel.Medium => "🟡 " + Loc.S("Bloat_ImpactMedium"),
        ImpactLevel.High => "🟠 " + Loc.S("Bloat_ImpactHigh"),
        ImpactLevel.VeryHigh => "🔴 " + Loc.S("Bloat_ImpactVeryHigh"),
        _ => "",
    };

    public string RiskBadge => _item.Risk switch
    {
        BloatRiskLevel.Safe => "🟢 " + Loc.S("Bloat_RiskSafe"),
        BloatRiskLevel.Caution => "🟡 " + Loc.S("Bloat_RiskCaution"),
        BloatRiskLevel.NotRecommended => "🔴 " + Loc.S("Bloat_RiskNotRecommended"),
        _ => "",
    };

    public string RecommendationBadge => _item.Recommendation switch
    {
        RecommendationLevel.Remove => Loc.S("Bloat_RecommendRemove"),
        RecommendationLevel.Optional => Loc.S("Bloat_RecommendOptional"),
        RecommendationLevel.Keep => Loc.S("Bloat_RecommendKeep"),
        _ => "",
    };

    public string StateText => CurrentlyEnabled ? "" : Loc.S("Bloat_StateDisabled");
    public bool ShowStateBadge => !CurrentlyEnabled;

    public string SizeText => _item.SizeBytes > 0 ? FormatBytes(_item.SizeBytes) : "";
    public bool HasSize => _item.SizeBytes > 0;

    public string BackgroundText => _item.RunsInBackground ? Loc.S("Bloat_RunsInBackground") : Loc.S("Bloat_NotInBackground");
    public string StartupImpactText => Loc.F("Bloat_StartupImpact", _item.StartupImpact);

    // ---- "Mais Informações" ----
    public string WhatItIs => _item.WhatItIs;
    public string WhoInstalled => _item.WhoInstalled;
    public string WhenUsed => _item.WhenUsed;
    public string BenefitsKeeping => _item.BenefitsKeeping;
    public string BenefitsRemoving => _item.BenefitsRemoving;
    public string Consequences => _item.Consequences;

    // ---- Visibilidade das ações ----
    public bool ShowDisable => _item.CanDisable && CurrentlyEnabled;
    public bool ShowEnable => _item.CanDisable && !CurrentlyEnabled;
    public bool ShowUninstall => _item.CanUninstall && CurrentlyEnabled;
    public bool IsCritical => _item.IsCritical;
    public bool HasStatus => StatusMessage.Length > 0;

    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnCurrentlyEnabledChanged(bool value) => Notify();
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    private void Notify()
    {
        OnPropertyChanged(nameof(ShowDisable));
        OnPropertyChanged(nameof(ShowEnable));
        OnPropertyChanged(nameof(ShowUninstall));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ShowStateBadge));
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        string risk = _item.Risk == BloatRiskLevel.NotRecommended ? "⚠ " + Loc.S("Bloat_NotRecommendedWarning") + "\n\n" : "";
        var confirm = MessageBox.Show(
            $"{risk}" + Loc.F("Bloat_DisableConfirmHeader", Name) + "\n\n" +
            Loc.S("Bloat_DisableConfirmBody") + "\n\n" +
            Loc.F("Bloat_RiskRecommendationLines", RiskBadge, RecommendationBadge) + "\n\n" +
            Loc.S("Bloat_WantToContinue"),
            Loc.S("Bloat_ConfirmDisableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await OfferRestorePointAsync();

        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _service.DisableAsync(_item);
            CurrentlyEnabled = _item.CurrentlyEnabled;
            StatusMessage = "↩ " + Loc.S("Bloat_DisabledRecorded");
        }
        catch (Exception ex) { StatusMessage = "❌ " + Loc.S("Bloat_FailedShort") + ex.Message; }
        finally { IsBusy = false; _onChanged(); }
    }

    [RelayCommand]
    private async Task EnableAsync()
    {
        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _service.EnableAsync(_item);
            CurrentlyEnabled = _item.CurrentlyEnabled;
            StatusMessage = "✅ " + Loc.S("Bloat_Reenabled");
        }
        catch (Exception ex) { StatusMessage = "❌ " + Loc.S("Bloat_FailedShort") + ex.Message; }
        finally { IsBusy = false; _onChanged(); }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        bool reversible = _item.IsUwp;
        string note = reversible
            ? Loc.S("Bloat_UninstallNoteUwp")
            : Loc.S("Bloat_UninstallNoteOfficial");
        var confirm = MessageBox.Show(
            Loc.F("Bloat_UninstallConfirmHeader", Name) + $"\n\n{note}\n\n" +
            Loc.F("Bloat_RiskRecommendationLines", RiskBadge, RecommendationBadge) + "\n\n" +
            Loc.S("Bloat_UninstallNotReversible") + "\n\n" + Loc.S("Bloat_WantToContinue"),
            Loc.S("Bloat_ConfirmUninstallTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        await OfferRestorePointAsync();

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var result = await _service.UninstallAsync(_item);
            switch (result.Outcome)
            {
                case UninstallOutcome.SilentSuccess:
                    CurrentlyEnabled = false;
                    StatusMessage = "✅ " + Loc.S("Bloat_AppRemoved");
                    await OfferLeftoverCleanupAsync();
                    break;
                case UninstallOutcome.OpenedInteractive:
                    // O desinstalador está aberto e quem conduz é o usuário; o estado do item só
                    // muda de verdade na próxima varredura.
                    StatusMessage = "➡ " + Loc.S("Bloat_OfficialUninstallerOpened");
                    break;
                default:
                    StatusMessage = "❌ " + Loc.S("Bloat_FailedShort") + (result.Detail ?? "");
                    break;
            }
        }
        catch (Exception ex) { StatusMessage = "❌ " + Loc.S("Bloat_FailedShort") + ex.Message; }
        finally { IsBusy = false; _onChanged(); }
    }

    /// <summary>
    /// Depois de uma desinstalação bem-sucedida, procura pastas que o programa deixou para trás e
    /// pergunta se devem sair também. É opcional de propósito: apagar pasta é a única coisa aqui que
    /// não tem volta, então nada acontece sem o usuário ver a lista e o espaço envolvido.
    /// </summary>
    private async Task OfferLeftoverCleanupAsync()
    {
        var leftovers = await _service.ScanLeftoversAsync(_item);
        if (leftovers.Count == 0) return;

        long total = leftovers.Sum(l => l.SizeBytes);
        string list = string.Join("\n", leftovers.Take(10).Select(l => $"• {l.Path} ({FormatBytes(l.SizeBytes)})"));
        if (leftovers.Count > 10) list += "\n" + Loc.F("Bloat_AndMoreFolders", (leftovers.Count - 10).ToString());

        var wants = MessageBox.Show(
            Loc.F("Bloat_LeftoversFound", leftovers.Count.ToString(), FormatBytes(total)) + "\n\n" + list +
            "\n\n" + Loc.S("Bloat_LeftoversNotReversible") + "\n\n" + Loc.S("Bloat_WantToContinue"),
            Loc.S("Bloat_LeftoversTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (wants != MessageBoxResult.Yes) return;

        var (removed, freed) = await _service.RemoveLeftoversAsync(leftovers);
        StatusMessage = "✅ " + Loc.F("Bloat_LeftoversRemoved", removed.ToString(), FormatBytes(freed));
    }

    [RelayCommand]
    private void Ignore()
    {
        _service.SetIgnored(Key, true);
        _onChanged();
    }

    // Oferece (sem obrigar) a criação de um ponto de restauração antes de uma alteração.
    private async Task OfferRestorePointAsync()
    {
        var wants = MessageBox.Show(
            Loc.S("Bloat_OfferRestorePointBody"),
            Loc.S("Bloat_RestorePointTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (wants != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusMessage = Loc.S("Bloat_CreatingRestorePoint");
        bool ok = await _service.CreateRestorePointAsync();
        StatusMessage = ok ? "✅ " + Loc.S("Bloat_RestorePointCreated") : "⚠ " + Loc.S("Bloat_RestorePointNotCreated");
        IsBusy = false;
    }

    private static string FormatBytes(long bytes)
    {
        double b = bytes;
        if (b >= 1073741824) return $"{b / 1073741824:0.0} GB";
        if (b >= 1048576) return $"{b / 1048576:0} MB";
        if (b >= 1024) return $"{b / 1024:0} KB";
        return $"{bytes} B";
    }
}

/// <summary>
/// ViewModel do Detector de Bloatware. Faz a varredura (somente leitura), apresenta os itens com
/// sua análise e gera o relatório final com as contagens por recomendação. Nenhuma alteração é
/// aplicada automaticamente — tudo parte de um clique explícito do usuário em cada card.
/// </summary>
public partial class BloatwareDetectorViewModel : ObservableObject
{
    private readonly BloatwareDetectorService _service;
    private readonly List<BloatwareItemViewModel> _all = new();

    [ObservableProperty] private bool isSectionExpanded;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private bool hasScanned;
    [ObservableProperty] private bool showIgnored;

    [ObservableProperty] private int removeCount;
    [ObservableProperty] private int optionalCount;
    [ObservableProperty] private int keepCount;
    [ObservableProperty] private string reportText = "";

    public ObservableCollection<BloatwareItemViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;
    public bool ShowEmpty => HasScanned && !IsScanning && Items.Count == 0;

    public BloatwareDetectorViewModel(BloatwareDetectorService service)
    {
        _service = service;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            var detected = await Task.Run(() => _service.ScanAsync());
            _all.Clear();
            foreach (var it in detected)
                _all.Add(new BloatwareItemViewModel(it, _service, RebuildView));
            HasScanned = true;
            RebuildView();
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    partial void OnShowIgnoredChanged(bool value) => RebuildView();

    // Reconstrói a lista visível aplicando o filtro de ignorados e recalcula o relatório.
    private void RebuildView()
    {
        Items.Clear();
        foreach (var vm in _all)
        {
            if (!ShowIgnored && _service.IsIgnored(vm.Key)) continue;
            Items.Add(vm);
        }

        RemoveCount = Items.Count(i => i.Model.Recommendation == RecommendationLevel.Remove);
        OptionalCount = Items.Count(i => i.Model.Recommendation == RecommendationLevel.Optional);
        KeepCount = Items.Count(i => i.Model.Recommendation == RecommendationLevel.Keep);

        ReportText = HasScanned
            ? Loc.F("Bloat_ReportText", Items.Count, RemoveCount, OptionalCount, KeepCount)
            : "";

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmpty));
    }
}
