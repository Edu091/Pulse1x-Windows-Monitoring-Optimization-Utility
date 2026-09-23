using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

public partial class OptimizationViewModel : ObservableObject, IDisposable
{
    private readonly MemoryOptimizationService _memoryOptimization;
    private readonly ISystemMetricsService _metrics;
    private readonly DispatcherTimer _timer;

    // Estatísticas de RAM exibidas no cartão.
    [ObservableProperty] private string ramUsedText = "--";
    [ObservableProperty] private string ramAvailableText = "--";
    [ObservableProperty] private double ramUsagePercent;

    // Estado da otimização em andamento.
    [ObservableProperty] private bool isOptimizing;
    [ObservableProperty] private double optimizeProgress;

    // Resultado da última otimização.
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool lastResultSuccess;
    [ObservableProperty] private string lastResultMessage = "";
    [ObservableProperty] private string lastResultDetails = "";

    // Seção recolhível "Como esta otimização funciona?".
    [ObservableProperty] private bool isExplainerExpanded;

    // Segunda ferramenta da categoria: Liberar Espaço em Disco.
    public DiskCleanupViewModel DiskCleanup { get; }

    // Terceira ferramenta da categoria: Comandos Especiais.
    public SpecialCommandsViewModel SpecialCommands { get; }

    // Quarta ferramenta da categoria: Otimizações Avançadas.
    public AdvancedOptimizationsViewModel AdvancedOptimizations { get; }

    public OptimizationViewModel(MemoryOptimizationService memoryOptimization, ISystemMetricsService metrics,
        DiskCleanupService diskCleanup, SpecialCommandsService specialCommands,
        AdvancedOptimizationService advancedOptimization, BloatwareDetectorService bloatwareDetector)
    {
        _memoryOptimization = memoryOptimization;
        _metrics = metrics;
        DiskCleanup = new DiskCleanupViewModel(diskCleanup);
        SpecialCommands = new SpecialCommandsViewModel(specialCommands);
        AdvancedOptimizations = new AdvancedOptimizationsViewModel(advancedOptimization, bloatwareDetector);

        RefreshRamStats();

        // Mantém os números de RAM atualizados enquanto a página está visível.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) =>
        {
            if (!IsOptimizing)
                RefreshRamStats();
        };
    }

    public void SetActive(bool active)
    {
        if (active)
        {
            RefreshRamStats();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void RefreshRamStats()
    {
        var ram = _metrics.ReadRam();
        double availableGb = Math.Max(0, ram.TotalGb - ram.UsedGb);

        RamUsedText = Loc.F("Opt_RamUsedOfTotal", $"{ram.UsedGb:0.0}", $"{ram.TotalGb:0.0}");
        RamAvailableText = Loc.F("Opt_RamFreeGb", $"{availableGb:0.0}");
        RamUsagePercent = ram.UsagePercent;
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        if (IsOptimizing) return;

        IsOptimizing = true;
        OptimizeProgress = 0;
        HasResult = false;

        var progress = new Progress<double>(p => OptimizeProgress = p * 100);

        try
        {
            var result = await _memoryOptimization.OptimizeAsync(progress);

            LastResultSuccess = result.Success;
            LastResultMessage = result.Message;
            LastResultDetails =
                Loc.F("Opt_BeforeLabel", $"{result.UsedBeforeGb:0.0}") + "  •  " +
                Loc.F("Opt_AfterLabel", $"{result.UsedAfterGb:0.0}") + "  •  " +
                Loc.F("Opt_FreedLabel", $"{result.FreedMb:0}") + $"  •  {result.ExecutedAt:HH:mm:ss}";
            HasResult = true;
        }
        catch
        {
            LastResultSuccess = false;
            LastResultMessage = Loc.S("Opt_FailedMessage");
            LastResultDetails = "";
            HasResult = true;
        }
        finally
        {
            IsOptimizing = false;
            OptimizeProgress = 0;
            RefreshRamStats();
        }
    }

    [RelayCommand]
    private void ToggleExplainer() => IsExplainerExpanded = !IsExplainerExpanded;

    public void Dispose() => _timer.Stop();
}
