using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pulse1x.App.Localization;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.Controls;

/// <summary>
/// Teclado virtual navegável por controle, no estilo do Big Picture.
///
/// As teclas são botões reais criados em código e colocados em painéis — não itens de um
/// ItemsControl. Isso importa: com containers gerados sob demanda, no instante em que o teclado
/// abria ainda não havia botão nenhum na árvore visual, o foco não tinha onde pousar e o direcional
/// continuava movendo os jogos no fundo. Com os botões já existentes, o foco entra na hora.
/// </summary>
public partial class GamepadKeyboard : UserControl
{
    private bool _shift;
    private readonly List<List<Button>> _rows = new();
    private Button? _firstKey;

    /// <summary>Texto digitado até agora.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            PreviewText.Text = _text;
            TextChanged?.Invoke(_text);
        }
    }
    private string _text = "";

    /// <summary>Disparado a cada alteração, para a busca filtrar ao vivo.</summary>
    public event Action<string>? TextChanged;

    /// <summary>Disparado quando o usuário conclui (Concluir, B ou Enter).</summary>
    public event Action? Closed;

    private static readonly string[] Rows =
    {
        "1234567890",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm-_",
    };

    public GamepadKeyboard()
    {
        InitializeComponent();
        BuildKeys();

        Loaded += (_, _) =>
        {
            HintText.Text = Loc.S("GH_KeyboardHint");
            PreviewText.Text = _text;
        };
    }

    // =====================================================================================
    //  Construção das teclas
    // =====================================================================================

    private void BuildKeys()
    {
        KeyRows.Children.Clear();
        _rows.Clear();
        _firstKey = null;

        foreach (var row in Rows)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var buttons = new List<Button>();

            foreach (char c in row)
            {
                var button = CreateKey(_shift ? char.ToUpperInvariant(c) : c);
                panel.Children.Add(button);
                buttons.Add(button);
                _firstKey ??= button;
            }

            KeyRows.Children.Add(panel);
            _rows.Add(buttons);
        }

        // Linha de ações
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var actionButtons = new List<Button>();

        Button Action(string content, Action onClick, double width)
        {
            var button = CreateKey(content, width);
            button.Click += (_, _) => onClick();
            actions.Children.Add(button);
            actionButtons.Add(button);
            return button;
        }

        Action("Shift", () => { _shift = !_shift; BuildKeys(); FocusFirstKey(); }, 92);
        Action(Loc.S("GH_KeySpace"), () => Text += " ", 210);
        // O caractere era exibido pela fonte padrão e virava um quadrado em alguns sistemas.
        // E750 é o ícone Backspace de Segoe MDL2 Assets.
        var backspace = Action("\uE750", () => { if (Text.Length > 0) Text = Text[..^1]; }, 92);
        backspace.FontFamily = new FontFamily("Segoe MDL2 Assets");
        backspace.FontSize = 16;
        Action(Loc.S("GH_KeyClear"), () => Text = "", 92);

        var done = Action(Loc.S("GH_KeyDone"), () => Closed?.Invoke(), 110);
        done.Background = (Brush)(TryFindResource("BrandAccentBrush") ?? Brushes.OrangeRed);

        KeyRows.Children.Add(actions);
        _rows.Add(actionButtons);
    }

    /// <summary>Cria uma tecla com o visual do hub e um destaque de foco inconfundível.</summary>
    private Button CreateKey(char character) => CreateKeyCore(character.ToString(), 46, () => Text += character);

    private Button CreateKey(string content, double width) => CreateKeyCore(content, width, null);

    private Button CreateKeyCore(string content, double width, Action? onClick)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = width,
            Height = 46,
            Margin = new Thickness(3),
            FontSize = content.Length == 1 ? 16 : 13,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1D, 0x21)),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = true,
            Template = (ControlTemplate)Resources["KeyTemplate"],
        };

        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    // =====================================================================================
    //  Foco
    // =====================================================================================

    /// <summary>
    /// Coloca o foco na primeira tecla. Tenta de novo no próximo ciclo de layout se ainda não for
    /// possível — abrir o teclado e o foco entrar são coisas que acontecem em quadros diferentes.
    /// </summary>
    public void FocusFirstKey() => FocusKey(_firstKey, attempt: 0);

    private void FocusKey(Button? key, int attempt)
    {
        if (key is null) return;

        if (key.IsVisible && key.Focus()) return;

        // Até três tentativas, na prioridade de layout: cobre o caso de o teclado ter acabado de
        // ficar visível e ainda não ter sido medido.
        if (attempt >= 3) return;
        Dispatcher.BeginInvoke(new Action(() => FocusKey(key, attempt + 1)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>O foco está dentro deste teclado?</summary>
    public bool HasFocusInside =>
        Keyboard.FocusedElement is DependencyObject focused && IsAncestorOf(focused);

    /// <summary>
    /// Move dentro da malha do teclado sem delegar para a navegação espacial global do WPF.
    /// Assim, chegar à borda nunca transfere o seletor para a tela que está atrás do overlay.
    /// </summary>
    public bool Move(GamepadDirection direction)
    {
        var focused = Keyboard.FocusedElement as Button;
        if (focused is null || !IsAncestorOf(focused))
        {
            FocusFirstKey();
            return true;
        }

        int rowIndex = _rows.FindIndex(row => row.Contains(focused));
        if (rowIndex < 0)
        {
            FocusFirstKey();
            return true;
        }

        var row = _rows[rowIndex];
        int columnIndex = row.IndexOf(focused);
        Button? next = direction switch
        {
            GamepadDirection.Left when columnIndex > 0 => row[columnIndex - 1],
            GamepadDirection.Right when columnIndex < row.Count - 1 => row[columnIndex + 1],
            GamepadDirection.Up when rowIndex > 0 => FindClosestKey(_rows[rowIndex - 1], focused),
            GamepadDirection.Down when rowIndex < _rows.Count - 1 => FindClosestKey(_rows[rowIndex + 1], focused),
            _ => null,
        };

        // A borda é uma parada, não uma saída: o teclado continua sendo modal.
        return next is not null && next.Focus();
    }

    private Button FindClosestKey(IReadOnlyList<Button> candidates, Button from)
    {
        double fromCenter = from.TranslatePoint(new Point(from.ActualWidth / 2, 0), this).X;
        return candidates.MinBy(candidate => Math.Abs(
            candidate.TranslatePoint(new Point(candidate.ActualWidth / 2, 0), this).X - fromCenter))!;
    }

    private bool IsAncestorOf(DependencyObject node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, this)) return true;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    // =====================================================================================
    //  Teclado físico
    // =====================================================================================

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            Text += e.Text;
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        switch (e.Key)
        {
            case Key.Back:
                if (Text.Length > 0) Text = Text[..^1];
                e.Handled = true;
                break;
            case Key.Space:
                Text += " ";
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Escape:
                Closed?.Invoke();
                e.Handled = true;
                break;
        }
    }
}
