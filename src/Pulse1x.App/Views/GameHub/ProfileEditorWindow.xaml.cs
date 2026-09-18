using System.Windows;
using Pulse1x.App.Localization;
using Pulse1x.App.ViewModels.GameHub;
using Wpf.Ui.Controls;

namespace Pulse1x.App.Views.GameHub;

/// <summary>
/// Janela do editor de perfil. O code-behind só cuida do que é de janela: sugerir os processos que
/// estão abertos agora no seletor de "fechar antes de jogar" e escolher o executável dos
/// aplicativos que abrem junto com o jogo.
/// </summary>
public partial class ProfileEditorWindow : FluentWindow
{
    private readonly ProfileEditorViewModel _viewModel;

    public ProfileEditorWindow(ProfileEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Lista de sugestões com os programas abertos agora — evita o usuário ter que descobrir o
        // nome exato do processo.
        ProcessCombo.ItemsSource = ProfileEditorViewModel.RunningProcesses();

        viewModel.CloseRequested += saved =>
        {
            DialogResult = saved;
            Close();
        };
    }

    private void AddStartApp_Click(object sender, RoutedEventArgs e)
    {
        string? file = FilePickers.PickFile(this, Loc.S("GH_PickExecutable"),
            "Executáveis (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os arquivos (*.*)|*.*");
        if (file is not null) _viewModel.AddStartApp(file);
    }
}
