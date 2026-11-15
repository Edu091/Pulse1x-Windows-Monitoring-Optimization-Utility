using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Pulse1x.App.Controls;

/// <summary>
/// Item da navegação lateral. É um <see cref="Button"/> (e não um RadioButton) de propósito: o
/// botão A do controle aciona botões pelo mesmo caminho do clique (ver GamepadFocusService.Accept),
/// enquanto num RadioButton ele só marcaria o item, sem navegar. O estado "ativo" é definido pela
/// MainWindow em <see cref="IsActive"/>, e o desenho fica no estilo implícito da MainWindow.
/// </summary>
public class NavItem : Button
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(SymbolRegular), typeof(NavItem),
            new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavItem),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(NavItem),
            new PropertyMetadata(false));

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(NavItem),
            new PropertyMetadata(null));

    /// <summary>Ícone Fluent do item (fica preenchido quando o item está ativo).</summary>
    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>Texto do item.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Item da página aberta no momento.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Selo opcional à direita (ex.: "REC" enquanto grava). Vazio ou nulo esconde.</summary>
    public string? Badge
    {
        get => (string?)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }
}
