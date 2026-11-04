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

        // O stress continua ao trocar de página e com a janela minimizada (dá para acompanhar as
        // temperaturas no Dashboard ou em outro programa), mas para quando a janela vai para a
        // bandeja: aí a CPU ficaria a 100% sem nada visível avisando.
        Loaded += (_, _) =>
        {
            if (_window is not null) return;
            _window = Window.GetWindow(this);
            if (_window is null) return;
            _window.IsVisibleChanged += (_, e) =>
            {
                if (!(bool)e.NewValue) _viewModel.StopStress();
            };
        };
    }
}
