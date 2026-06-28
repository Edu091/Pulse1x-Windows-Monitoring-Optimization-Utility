using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.Controls;
using Pulse1x.App.Models;
using Pulse1x.App.Services;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class DashboardPage : Page
{
    private DashboardViewModel? _viewModel;
    private readonly HardwareDetailsService _detailsService;

    public DashboardPage(DashboardViewModel viewModel, HardwareDetailsService detailsService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _detailsService = detailsService;

        viewModel.Sections.CollectionChanged += (_, _) => ApplySectionOrder();
        ApplySectionOrder();
    }

    // Ao clicar em qualquer card de métrica, abre a página de detalhes do componente
    // correspondente dentro do mesmo Frame (mantém a pilha de navegação p/ o "Voltar").
    private void MetricCard_Clicked(object? sender, System.EventArgs e)
    {
        if (sender is not MetricCard card || string.IsNullOrEmpty(card.DetailKind))
            return;

        var detailViewModel = new ComponentDetailViewModel(_detailsService, card.DetailKind, card.DetailId ?? "");
        var detailPage = new ComponentDetailPage(detailViewModel);
        NavigationService?.Navigate(detailPage);
    }

    // As três seções (Informações, Métricas, Gráficos) ficam em posições fixas no XAML;
    // ao reordenar via Sections (drag conceitual feito pelos botões ↑/↓), refletimos a
    // ordem ajustando a linha de cada bloco dentro do Grid que os contém.
    private void ApplySectionOrder()
    {
        if (_viewModel is null) return;

        for (int i = 0; i < _viewModel.Sections.Count; i++)
        {
            var block = _viewModel.Sections[i].Kind switch
            {
                DashboardSectionKind.SystemInfo => SystemInfoSectionRoot,
                DashboardSectionKind.Metrics => MetricsSectionRoot,
                DashboardSectionKind.Charts => ChartsSectionRoot,
                _ => null
            };

            if (block is not null)
                Grid.SetRow(block, i);
        }
    }
}
