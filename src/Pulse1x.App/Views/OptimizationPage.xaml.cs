using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class OptimizationPage : Page
{
    public OptimizationPage(OptimizationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
