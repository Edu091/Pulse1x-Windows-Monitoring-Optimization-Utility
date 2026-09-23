using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class OptimizationPage : Page
{
    private readonly OptimizationViewModel _viewModel;

    public OptimizationPage(OptimizationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        IsVisibleChanged += (_, e) => _viewModel.SetActive((bool)e.NewValue);
    }
}
