using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>Janela de cadastro de emuladores e das pastas de ROMs que alimentam a biblioteca.</summary>
public partial class EmulatorWindow : FluentWindow
{
    public EmulatorWindow(EmulatorEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PickFileRequested += (title, filter) => FilePickers.PickFile(this, title, filter);
        viewModel.PickFolderRequested += () => FilePickers.PickFolder(this);
        viewModel.CloseRequested += _ => Close();
    }
}
