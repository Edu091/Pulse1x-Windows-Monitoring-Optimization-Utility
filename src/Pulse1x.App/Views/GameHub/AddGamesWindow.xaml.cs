using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>
/// Assistente de adicionar jogos: reúne num lugar só a detecção nas lojas, a varredura de pastas,
/// o cadastro de arquivos avulsos, os emuladores e os atalhos do sistema.
/// </summary>
public partial class AddGamesWindow : FluentWindow
{
    public AddGamesWindow(AddGamesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PickFileRequested += (title, filter) => FilePickers.PickFile(this, title, filter);
        viewModel.PickFolderRequested += () => FilePickers.PickFolder(this);
        viewModel.CloseRequested += _ => Close();
    }
}
