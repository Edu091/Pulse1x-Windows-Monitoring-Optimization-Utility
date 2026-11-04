using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class BenchmarkPage : Page
{
    private readonly BenchmarkViewModel _viewModel;
    private Window? _window;

    public BenchmarkPage(BenchmarkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) viewModel.OnShown();
        };

        // O stress continua ao trocar de página (dá para acompanhar as temperaturas no Dashboard),
        // mas para quando a janela sai de vista: com "minimizar para a bandeja", fechar a janela
        // deixaria a CPU a 100% escondida.
        Loaded += (_, _) =>
        {
            if (_window is not null) return;
            _window = Window.GetWindow(this);
            if (_window is null) return;
            _window.IsVisibleChanged += (_, e) =>
            {
                if (!(bool)e.NewValue) _viewModel.StopStress();
            };
            _window.StateChanged += (_, _) =>
            {
                if (_window.WindowState == WindowState.Minimized) _viewModel.StopStress();
            };
        };
    }
}
