using Microsoft.Win32;
using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>
/// Janela de cadastro/edição de um item da biblioteca. A escolha de arquivos e pastas fica aqui
/// (é responsabilidade de janela, não de ViewModel) e é devolvida à ViewModel pelos callbacks.
/// </summary>
public partial class GameEditorWindow : FluentWindow
{
    public GameEditorWindow(GameEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PickFileRequested += (title, filter) => FilePickers.PickFile(this, title, filter);
        viewModel.PickFolderRequested += () => FilePickers.PickFolder(this);
        viewModel.CloseRequested += saved =>
        {
            DialogResult = saved;
            Close();
        };
    }
}

/// <summary>Seletores de arquivo e pasta compartilhados pelas janelas do GameHub.</summary>
internal static class FilePickers
{
    public static string? PickFile(System.Windows.Window owner, string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static string? PickFolder(System.Windows.Window owner)
    {
        // OpenFolderDialog é o seletor de pastas nativo do WPF a partir do .NET 8 — sem
        // dependências extras e com a aparência do Windows 11.
        var dialog = new OpenFolderDialog { Multiselect = false };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
