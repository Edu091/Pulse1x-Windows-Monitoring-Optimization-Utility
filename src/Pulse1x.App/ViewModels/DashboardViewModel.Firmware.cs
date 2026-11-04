using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

/// <summary>
/// Seção "BIOS e desempenho" do Dashboard: opções de firmware e do Windows que mais pesam em
/// desempenho e uso, com o que o aparelho suporta e o que fazer. Lida uma vez ao abrir o Dashboard
/// (nada disso muda sem reiniciar) e de novo pelo botão Atualizar.
/// </summary>
public partial class DashboardViewModel
{
    /// <summary>Serviço de leitura; atribuído na montagem do app (fica fora do construtor).</summary>
    public FirmwareStatusService? Firmware { get; set; }

    [ObservableProperty] private IReadOnlyList<FirmwareItemViewModel> _firmwareBiosItems = Array.Empty<FirmwareItemViewModel>();
    [ObservableProperty] private IReadOnlyList<FirmwareItemViewModel> _firmwareWindowsItems = Array.Empty<FirmwareItemViewModel>();
    [ObservableProperty] private bool _isFirmwareLoading;
    [ObservableProperty] private string _firmwareSummary = "";
    /// <summary>Seção aberta? Começa recolhida — o cabeçalho já mostra o resumo.</summary>
    [ObservableProperty] private bool _isFirmwareExpanded;

    public string FirmwareToggleText => Loc.S(IsFirmwareExpanded ? "Fw_Collapse" : "Fw_Expand");

    partial void OnIsFirmwareExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(FirmwareToggleText));
        _settingsService.Current.DashboardFirmwareExpanded = value;
        _settingsService.Save();
    }

    [RelayCommand]
    private void ToggleFirmware() => IsFirmwareExpanded = !IsFirmwareExpanded;

    public bool CanRestartToFirmware => FirmwareStatusService.SupportsFirmwareRestart;

    private bool _firmwareLoaded;
    private IReadOnlyList<FirmwareItem> _firmwareRaw = Array.Empty<FirmwareItem>();

    /// <summary>Primeira leitura, ao abrir o Dashboard.</summary>
    public void EnsureFirmwareLoaded()
    {
        if (_firmwareLoaded) return;
        _firmwareLoaded = true;
        // Os textos dos itens saem no idioma da leitura: ao trocar o idioma, lê de novo.
        Loc.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(FirmwareToggleText));
            _ = RefreshFirmwareAsync();
        };
        _ = RefreshFirmwareAsync();
    }

    [RelayCommand]
    private async Task RefreshFirmwareAsync()
    {
        if (Firmware is null || IsFirmwareLoading) return;
        IsFirmwareLoading = true;
        try
        {
            _firmwareRaw = await Firmware.ReadAsync();
            BuildFirmwareItems();
        }
        catch { /* leitura de conveniência: nunca derruba o Dashboard */ }
        finally { IsFirmwareLoading = false; }
    }

    private void BuildFirmwareItems()
    {
        FirmwareBiosItems = _firmwareRaw.Where(i => i.Group == FirmwareGroup.Bios).Select(i => new FirmwareItemViewModel(i)).ToList();
        FirmwareWindowsItems = _firmwareRaw.Where(i => i.Group == FirmwareGroup.Windows).Select(i => new FirmwareItemViewModel(i)).ToList();
        int good = _firmwareRaw.Count(i => i.State == FirmwareState.Good);
        int attention = _firmwareRaw.Count(i => i.State == FirmwareState.Attention);
        FirmwareSummary = Loc.F("Fw_Summary", good, attention);
    }

    [RelayCommand]
    private void RestartToFirmware()
    {
        var answer = MessageBox.Show(Loc.S("Fw_RestartConfirm"), "Pulse1x", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        if (!FirmwareStatusService.RestartToFirmware())
            MessageBox.Show(Loc.S("Fw_RestartFailed"), "Pulse1x", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>Cartão de um item de BIOS/Windows.</summary>
public sealed class FirmwareItemViewModel
{
    public FirmwareItemViewModel(FirmwareItem item)
    {
        Title = item.Title;
        Value = item.Value;
        Compatibility = item.Compatibility;
        Tip = item.Tip;
        (Color, StateText) = item.State switch
        {
            FirmwareState.Good => ("#22C55E", Loc.S("Fw_StateGood")),
            FirmwareState.Attention => ("#F97316", Loc.S("Fw_StateAttention")),
            FirmwareState.Info => ("#60A5FA", Loc.S("Fw_StateInfo")),
            FirmwareState.NotSupported => ("#9CA3AF", Loc.S("Fw_StateNotSupported")),
            _ => ("#9CA3AF", Loc.S("Fw_StateUnknown")),
        };
    }

    public string Title { get; }
    public string Value { get; }
    public string Compatibility { get; }
    public string Tip { get; }
    public string Color { get; }
    public string StateText { get; }
}
