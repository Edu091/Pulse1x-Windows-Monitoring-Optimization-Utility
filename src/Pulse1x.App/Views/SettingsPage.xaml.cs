using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pulse1x.App.Localization;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // A escolha do arquivo é responsabilidade da janela, não da ViewModel.
        viewModel.PickBackgroundImageRequested += () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.S("GH_PickImage"),
                Filter = "Imagens (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|" +
                         "Todos os arquivos (*.*)|*.*",
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };

        viewModel.PickSoundRequested += () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.S("Settings_SoundCustom"),
                Filter = "Áudio (*.wav)|*.wav",
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };
    }
}
