using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Views.WinCustom;

/// <summary>
/// Um valor de enum com seu rótulo traduzido, para aparecer numa caixa de combinação. Evita
/// espalhar conversores por cada seletor da seção.
/// </summary>
public sealed record EnumChoice<T>(T Value, string Label) where T : struct, Enum;

/// <summary>
/// Caixa de diálogo simples de uma linha de texto (usada por "Renomear tema"). O WPF não traz uma
/// pronta, e abrir uma janela inteira só para isto seria exagero.
/// </summary>
public static class PromptDialog
{
    /// <summary>Devolve o texto digitado, ou null se o usuário cancelar.</summary>
    public static string? Show(Window? owner, string title, string prompt, string initialValue)
    {
        var textBox = new Wpf.Ui.Controls.TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 8, 0, 12),
        };

        var okButton = new Wpf.Ui.Controls.Button
        {
            Content = "OK",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };

        var cancelButton = new Wpf.Ui.Controls.Button
        {
            Content = Loc.S("Common_Cancel"),
            Width = 90,
            IsCancel = true,
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);

        var window = new Wpf.Ui.Controls.FluentWindow
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                },
            },
        };

        var root = (Grid)window.Content;
        var titleBar = new Wpf.Ui.Controls.TitleBar { Title = title };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(panel, 1);
        root.Children.Add(titleBar);
        root.Children.Add(panel);

        bool accepted = false;
        okButton.Click += (_, _) => { accepted = true; window.Close(); };
        cancelButton.Click += (_, _) => window.Close();

        window.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
        window.ShowDialog();

        return accepted ? textBox.Text : null;
    }
}
