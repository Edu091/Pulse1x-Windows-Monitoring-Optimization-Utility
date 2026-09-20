using Pulse1x.App.ViewModels.GameHub;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.Services;
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
        Loaded += (_, _) =>
        {
            Animations.OpenWindow(RootGrid);
            Dispatcher.BeginInvoke(() =>
            {
                if (GamesList.Items.Count > 0) GamepadFocusService.FocusFirst(GamesList);
                else MasterToggle.Focus();
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
    }
}
