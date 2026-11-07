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
}
