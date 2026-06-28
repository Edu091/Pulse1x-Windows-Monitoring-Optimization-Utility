using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Models;

namespace Pulse1x.App.ViewModels;

public partial class DiskMetricViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string primaryValue = "--";
    [ObservableProperty] private string secondaryValue = "--";
    [ObservableProperty] private double usagePercent;
    [ObservableProperty] private UsageLevel usageLevel;

    public DiskMetricViewModel(string name)
    {
        Name = name;
    }
}
