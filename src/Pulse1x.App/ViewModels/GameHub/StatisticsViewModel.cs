using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

public class DayBarViewModel
{
    public DateTime Day { get; init; }
    public double Minutes { get; init; }
    public double Ratio { get; init; }
    public string Label => Day.ToString("dd/MM");
    public string ValueText => GameStats.FormatDuration(Minutes);
    public bool IsToday => Day.Date == DateTime.Today;
}

/// <summary>
/// Pro performance dashboard backed exclusively by the local GameHub session history.
/// Collection preferences are persisted in settings and applied to the next game session.
/// </summary>
public partial class StatisticsViewModel : ObservableObject
{
    private readonly PlayMetricsService _metrics;
    private readonly SettingsService _settings;

    public event Action? CloseRequested;

    public ObservableCollection<GameStats> Games { get; } = new();
    public ObservableCollection<DayBarViewModel> DailyBars { get; } = new();

    [ObservableProperty] private string totalText = "";
    [ObservableProperty] private string perDayText = "";
    [ObservableProperty] private string sessionCountText = "";
    [ObservableProperty] private string peakText = "";
    [ObservableProperty] private string mostPlayedText = "";
    [ObservableProperty] private bool metricsEnabled = true;
    [ObservableProperty] private bool playtimeEnabled = true;
    [ObservableProperty] private bool fpsEnabled = true;
    [ObservableProperty] private bool temperaturesEnabled = true;
    [ObservableProperty] private bool hardwareUsageEnabled = true;
    [ObservableProperty] private bool memoryEnabled = true;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private GameStats? selectedGame;
    [ObservableProperty] private string selectedTitle = "";
    [ObservableProperty] private string selectedPeriodText = "";
    [ObservableProperty] private string selectedPlaytimeText = "";
    [ObservableProperty] private string selectedFpsText = "";
    [ObservableProperty] private string selectedOnePercentLowText = "";
    [ObservableProperty] private string selectedCpuTemperatureText = "";
    [ObservableProperty] private string selectedGpuTemperatureText = "";
    [ObservableProperty] private string selectedRamText = "";
    [ObservableProperty] private string selectedCpuUsageText = "";
    [ObservableProperty] private string selectedGpuUsageText = "";
    [ObservableProperty] private string selectedLongestSessionText = "";
    [ObservableProperty] private string impactText = "";

    public StatisticsViewModel(PlayMetricsService metrics, SettingsService settings, string? selectedGameId = null)
    {
        _metrics = metrics;
        _settings = settings;

        var hub = settings.Current.GameHub;
        metricsEnabled = metrics.Enabled;
        playtimeEnabled = hub.MetricsPlaytimeEnabled;
        fpsEnabled = hub.MetricsFpsEnabled;
        temperaturesEnabled = hub.MetricsTemperaturesEnabled;
        hardwareUsageEnabled = hub.MetricsHardwareUsageEnabled;
        memoryEnabled = hub.MetricsMemoryEnabled;

        Refresh(selectedGameId);
        UpdateImpactText();
    }

    private void Refresh(string? selectedGameId = null)
    {
        Games.Clear();
        foreach (var stats in _metrics.AllStats()) Games.Add(stats);

        IsEmpty = Games.Count == 0;
        TotalText = GameStats.FormatDuration(_metrics.TotalMinutes);
        PerDayText = GameStats.FormatDuration(_metrics.AverageMinutesPerDay(30));
        SessionCountText = Games.Sum(g => g.SessionCount).ToString();

        var peak = _metrics.PeakDay();
        PeakText = peak is null
            ? "-"
            : $"{GameStats.FormatDuration(peak.Value.Minutes)} · {peak.Value.Day:dd/MM/yyyy}";
        MostPlayedText = Games.Count > 0 ? Games[0].GameName : "-";

        SelectedGame = selectedGameId is null
            ? Games.FirstOrDefault()
            : Games.FirstOrDefault(g => g.GameId == selectedGameId) ?? Games.FirstOrDefault();
        RefreshSelected();
    }

    partial void OnSelectedGameChanged(GameStats? value) => RefreshSelected();

    private void RefreshSelected()
    {
        var stats = SelectedGame;
        SelectedTitle = stats?.GameName ?? Loc.S("GH_StatsNoSelection");
        SelectedPeriodText = stats?.FirstPlayed is null
            ? Loc.S("GH_StatsNoSession")
            : Loc.F("GH_StatsPeriod", stats.FirstPlayed.Value.ToString("dd/MM/yyyy"),
                stats.LastPlayed?.ToString("dd/MM/yyyy") ?? "-");
        SelectedPlaytimeText = stats?.TotalText ?? "-";
        SelectedFpsText = FormatMetric(stats?.AverageFps, " FPS");
        SelectedOnePercentLowText = FormatMetric(stats?.AverageOnePercentLowFps, " FPS");
        SelectedCpuTemperatureText = FormatMetric(stats?.AverageCpuTemperature, " °C");
        SelectedGpuTemperatureText = FormatMetric(stats?.AverageGpuTemperature, " °C");
        SelectedRamText = stats?.AverageRamUsedGb is > 0
            ? $"{stats.AverageRamUsedGb:0.0} GB · {stats.AverageRamUsagePercent:0}%"
            : "-";
        SelectedCpuUsageText = FormatMetric(stats?.AverageCpuUsage, "%");
        SelectedGpuUsageText = FormatMetric(stats?.AverageGpuUsage, "%");
        SelectedLongestSessionText = stats?.LongestSessionText ?? "-";

        DailyBars.Clear();
        var daily = _metrics.DailyTotals(stats?.GameId, 14);
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
    }

    private static string FormatMetric(double? value, string suffix) => value is > 0
        ? $"{value.Value:0}{suffix}"
        : "-";

    partial void OnMetricsEnabledChanged(bool value)
    {
        _metrics.Enabled = value;
        _settings.Current.GameHub.MetricsEnabled = value;
        _settings.Save();
        UpdateImpactText();
    }

    partial void OnPlaytimeEnabledChanged(bool value) => ApplyCollectionSettings();
    partial void OnFpsEnabledChanged(bool value) => ApplyCollectionSettings();
    partial void OnTemperaturesEnabledChanged(bool value) => ApplyCollectionSettings();
    partial void OnHardwareUsageEnabledChanged(bool value) => ApplyCollectionSettings();
    partial void OnMemoryEnabledChanged(bool value) => ApplyCollectionSettings();

    private void ApplyCollectionSettings()
    {
        var hub = _settings.Current.GameHub;
        hub.MetricsPlaytimeEnabled = PlaytimeEnabled;
        hub.MetricsFpsEnabled = FpsEnabled;
        hub.MetricsTemperaturesEnabled = TemperaturesEnabled;
        hub.MetricsHardwareUsageEnabled = HardwareUsageEnabled;
        hub.MetricsMemoryEnabled = MemoryEnabled;
        _settings.Save();

        _metrics.Configure(new TelemetryCollectionOptions(
            PlaytimeEnabled, FpsEnabled, TemperaturesEnabled, HardwareUsageEnabled, MemoryEnabled));
        UpdateImpactText();
    }

    private void UpdateImpactText()
    {
        if (!MetricsEnabled)
        {
            ImpactText = Loc.S("GH_StatsImpactOff");
            return;
        }

        int activeSensors = (FpsEnabled ? 1 : 0) + (TemperaturesEnabled ? 1 : 0) +
                            (HardwareUsageEnabled ? 1 : 0) + (MemoryEnabled ? 1 : 0);
        ImpactText = activeSensors switch
        {
            0 => Loc.S("GH_StatsImpactMinimal"),
            <= 2 => Loc.S("GH_StatsImpactVeryLow"),
            _ => Loc.S("GH_StatsImpactLow"),
        };
    }

    [RelayCommand]
    private void ClearHistory()
    {
        var result = System.Windows.MessageBox.Show(
            Loc.S("GH_StatsClearConfirm"), Loc.S("GH_MenuStatistics"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        _metrics.Clear();
        Refresh();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
