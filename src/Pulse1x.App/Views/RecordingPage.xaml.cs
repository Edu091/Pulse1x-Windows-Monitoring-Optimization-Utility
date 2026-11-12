using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class RecordingPage : Page
{
    public RecordingPage(RecordingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Monitores, microfones e a lista de vídeos podem ter mudado desde a última visita.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) viewModel.OnShown();
        };
    }
}
