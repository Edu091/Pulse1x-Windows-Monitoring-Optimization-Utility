using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Uma barra do gráfico de minutos por dia.</summary>
public class DayBarViewModel
{
    public DateTime Day { get; init; }
    public double Minutes { get; init; }
    /// <summary>Altura relativa (0 a 1) em relação ao dia de maior uso do período.</summary>
    public double Ratio { get; init; }
    public string Label => Day.ToString("dd/MM");
    public string ValueText => GameStats.FormatDuration(Minutes);
    public bool IsToday => Day.Date == DateTime.Today;
}

/// <summary>
/// Seção de estatísticas do GameHub: quanto tempo foi jogado, em que jogos, com que ritmo e — quando
/// a medição de FPS conseguiu trabalhar — com que desempenho.
///
/// Tudo é derivado do histórico de sessões, então os números batem com o que realmente aconteceu.
/// O usuário pode desligar o registro ou apagar o histórico a qualquer momento.
/// </summary>
public partial class StatisticsViewModel : ObservableObject
{
    private readonly PlayMetricsService _metrics;
    private readonly GameLibraryService _library;

    public event Action? CloseRequested;

    public ObservableCollection<GameStats> Games { get; } = new();
    public ObservableCollection<DayBarViewModel> DailyBars { get; } = new();

    [ObservableProperty] private string totalText = "";
    [ObservableProperty] private string perDayText = "";
    [ObservableProperty] private string sessionCountText = "";
    [ObservableProperty] private string peakText = "";
    [ObservableProperty] private string mostPlayedText = "";
    [ObservableProperty] private bool metricsEnabled = true;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private GameStats? selectedGame;

    public StatisticsViewModel(PlayMetricsService metrics, GameLibraryService library)
    {
        _metrics = metrics;
        _library = library;
        metricsEnabled = metrics.Enabled;

        Refresh();
    }

    private void Refresh()
    {
        Games.Clear();
        foreach (var stats in _metrics.AllStats()) Games.Add(stats);

        IsEmpty = Games.Count == 0;

        TotalText = GameStats.FormatDuration(_metrics.TotalMinutes);
        PerDayText = GameStats.FormatDuration(_metrics.AverageMinutesPerDay(30));
        SessionCountText = Games.Sum(g => g.SessionCount).ToString();

        var peak = _metrics.PeakDay();
        PeakText = peak is null
            ? "—"
            : $"{GameStats.FormatDuration(peak.Value.Minutes)} · {peak.Value.Day:dd/MM/yyyy}";

        MostPlayedText = Games.Count > 0 ? Games[0].GameName : "—";

        // Gráfico dos últimos 14 dias, normalizado pelo dia de maior uso.
        DailyBars.Clear();
        var daily = _metrics.DailyTotals(14);
        double max = daily.Count > 0 ? daily.Max(d => d.Minutes) : 0;

        foreach (var (day, minutes) in daily)
        {
            DailyBars.Add(new DayBarViewModel
            {
                Day = day,
                Minutes = minutes,
                Ratio = max > 0 ? minutes / max : 0,
            });
        }

        SelectedGame = Games.FirstOrDefault();
    }

    partial void OnMetricsEnabledChanged(bool value) => _metrics.Enabled = value;

    /// <summary>Apaga todo o histórico, depois de confirmar — é uma ação irreversível.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        var result = System.Windows.MessageBox.Show(
            Loc.S("GH_StatsClearConfirm"),
            Loc.S("GH_MenuStatistics"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        _metrics.Clear();
        Refresh();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
