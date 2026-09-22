using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class LatencyPage : Page
{
    private readonly LatencyViewModel _viewModel;
    public event Action? InputLabRequested;

    public LatencyPage(LatencyViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Liga o monitoramento em tempo real só enquanto a página está visível (economia de recursos).
        IsVisibleChanged += (_, e) => _viewModel.SetActive((bool)e.NewValue);
    }

    private void InputLab_Click(object sender, System.Windows.RoutedEventArgs e) => InputLabRequested?.Invoke();
}
