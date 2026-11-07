using System.Windows.Controls;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class SoundPage : Page
{
    public SoundPage(SoundViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Os medidores e os testes só rodam com a página aberta: o microfone não fica em uso (nem
        // aparece o aviso de privacidade do Windows) depois que o usuário sai da categoria.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) viewModel.OnShown();
            else viewModel.OnHidden();
        };
    }

    // Arrastar arquivos de áudio (por exemplo, memes baixados do navegador) para a mesa de sons.
    private void Soundboard_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Soundboard_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && DataContext is SoundViewModel vm)
            vm.ImportFiles(files);
        e.Handled = true;
    }
}
