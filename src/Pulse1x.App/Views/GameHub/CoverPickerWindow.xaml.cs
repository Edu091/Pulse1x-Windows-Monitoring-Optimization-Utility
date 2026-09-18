using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>Escolha da capa de um item: sugestões buscadas online ou uma imagem do computador.</summary>
public partial class CoverPickerWindow : FluentWindow
{
    public CoverPickerWindow(CoverPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PickFileRequested += (title, filter) => FilePickers.PickFile(this, title, filter);
        viewModel.CloseRequested += applied =>
        {
            DialogResult = applied;
            Close();
        };
    }
}
