using System.Windows.Controls;
using System.Windows.Input;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views;

public partial class FpsInGamePage : Page
{
    private readonly FpsInGameViewModel _viewModel;

    public FpsInGamePage(FpsInGameViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // A base (~16 mil jogos) só é descompactada quando a categoria é aberta pela primeira vez,
        // para não pesar na abertura do app.
        IsVisibleChanged += async (_, e) =>
        {
            if ((bool)e.NewValue) await _viewModel.EnsureLoadedAsync();
        };
    }

    // Enquanto o usuário digita no seletor de GPU, a lista filtrada fica aberta para ele escolher.
    private void GpuCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GpuCombo.IsKeyboardFocusWithin && !GpuCombo.IsDropDownOpen && GpuCombo.SelectedItem is null)
            GpuCombo.IsDropDownOpen = true;
    }

    private void GpuCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (GpuCombo.SelectedItem is null) _viewModel.RestoreGpuSelection();
    }

    private void GpuCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // A lista aberta também tira o foco da caixa de texto; só restaura quando o foco sai do
        // seletor de vez.
        if (!GpuCombo.IsKeyboardFocusWithin && !GpuCombo.IsDropDownOpen) _viewModel.RestoreGpuSelection();
    }
}
