using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class UtilityPage : Page
{
    public UtilityPage(PostFormatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
