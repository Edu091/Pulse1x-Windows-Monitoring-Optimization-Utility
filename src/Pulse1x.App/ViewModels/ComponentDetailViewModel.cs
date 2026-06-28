using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Models;
using Pulse1x.App.Services;

namespace Pulse1x.App.ViewModels;

public partial class ComponentDetailViewModel : ObservableObject
{
    private readonly HardwareDetailsService _detailsService;

    [ObservableProperty] private string title = "Detalhes";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private bool isLoading = true;

    public ObservableCollection<DetailGroup> Groups { get; } = new();

    public ComponentDetailViewModel(HardwareDetailsService detailsService, string kind, string id)
    {
        _detailsService = detailsService;
        Load(kind, id);
    }

    private async void Load(string kind, string id)
    {
        IsLoading = true;

        // A coleta usa WMI (lenta); roda em background e retorna à thread de UI.
        var details = await System.Threading.Tasks.Task.Run(() => _detailsService.GetDetails(kind, id));

        Title = details.Title;
        Subtitle = details.Subtitle;
        Groups.Clear();
        foreach (var group in details.Groups)
            Groups.Add(group);

        IsLoading = false;
    }
}
