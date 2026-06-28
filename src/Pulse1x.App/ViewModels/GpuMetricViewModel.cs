using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;
using LiveChartsCore.Defaults;

namespace Pulse1x.App.ViewModels;

public partial class GpuMetricViewModel : ObservableObject
{
    [ObservableProperty] private string name;
    [ObservableProperty] private double usagePercent;
    [ObservableProperty] private string temperatureText = "--";
    [ObservableProperty] private UsageLevel usageLevel;

    public string ChartTitle => string.Format(Loc.S("Dashboard_GpuUsageChart"), Name);

    public ObservableCollection<DateTimePoint> ChartValues { get; } = new();
    public Color AccentColor { get; }

    public GpuMetricViewModel(string name, Color accentColor)
    {
        this.name = name;
        AccentColor = accentColor;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(ChartTitle));
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(ChartTitle));
}
