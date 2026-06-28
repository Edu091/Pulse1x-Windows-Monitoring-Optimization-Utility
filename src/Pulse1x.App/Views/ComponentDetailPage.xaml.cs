using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class ComponentDetailPage : Page
{
    public ComponentDetailPage(ComponentDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
    }
}
