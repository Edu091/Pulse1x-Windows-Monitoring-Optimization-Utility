using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class BenchmarkPage : Page
{
    public BenchmarkPage(BenchmarkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) viewModel.OnShown();
        };
    }
}
