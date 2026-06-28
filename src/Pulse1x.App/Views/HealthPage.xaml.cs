using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class HealthPage : Page
{
    public HealthPage(HealthViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
