using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>Seção de estatísticas de uso: horas, ritmo, picos e FPS por jogo.</summary>
public partial class StatisticsWindow : FluentWindow
{
    public StatisticsWindow(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }
}
