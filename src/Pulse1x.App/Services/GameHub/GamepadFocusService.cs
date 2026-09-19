using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Navegação por controle em QUALQUER tela do Pulse1x, não só na grade de jogos.
///
/// A ideia é reaproveitar o foco do próprio WPF em vez de inventar um sistema paralelo: o direcional
/// move o foco na direção correspondente (<see cref="FrameworkElement.MoveFocus"/> com direção
/// espacial), e A/B acionam ou cancelam o elemento focado. Isso faz o controle funcionar em botões,
/// listas, caixas de texto, interruptores e deslizantes sem que cada tela precise saber que existe
/// um controle conectado.
///
/// Onde o comportamento natural do foco não basta — deslizantes, que devem variar o valor em vez de
/// pular para o próximo campo — há tratamento específico.
/// </summary>
public static class GamepadFocusService
{
    /// <summary>
    /// Move o foco a partir do elemento atual na direção pedida. Devolve false quando não havia
    /// para onde ir, para quem chamou decidir o que fazer (por exemplo, voltar para a grade).
    /// </summary>
    public static bool Move(GamepadDirection direction)
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;

        // Sem foco em lugar nenhum: entrega o foco ao primeiro elemento focável da janela ativa.
        if (focused is null)
        {
            var window = ActiveWindow();
            if (window is null) return false;
            FocusFirst(window);
            return true;
        }

        // Deslizante: esquerda/direita mudam o valor; cima/baixo saem do controle.
        if (focused is Slider slider && direction is GamepadDirection.Left or GamepadDirection.Right)
        {
            double step = Math.Max(slider.SmallChange, (slider.Maximum - slider.Minimum) / 20);
            slider.Value = Math.Clamp(
                slider.Value + (direction == GamepadDirection.Right ? step : -step),
                slider.Minimum, slider.Maximum);
            return true;
        }

        // ComboBox fechada: esquerda/direita trocam o item selecionado, como num menu de console.
        if (focused is ComboBox { IsDropDownOpen: false } combo &&
            direction is GamepadDirection.Left or GamepadDirection.Right)
        {
            int next = combo.SelectedIndex + (direction == GamepadDirection.Right ? 1 : -1);
            if (next >= 0 && next < combo.Items.Count) combo.SelectedIndex = next;
            return true;
        }

        var wpfDirection = direction switch
        {
            GamepadDirection.Up => FocusNavigationDirection.Up,
            GamepadDirection.Down => FocusNavigationDirection.Down,
            GamepadDirection.Left => FocusNavigationDirection.Left,
            _ => FocusNavigationDirection.Right,
        };

        return focused.MoveFocus(new TraversalRequest(wpfDirection));
    }

    /// <summary>Janela que está recebendo a entrada agora (um diálogo aberto, se houver).</summary>
    public static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    /// <summary>
    /// Há um diálogo aberto por cima da janela principal? Nesse caso a entrada do controle deve ir
    /// para ele — é o que faz o gamepad funcionar também em "Adicionar jogos", "Perfil" e afins.
    /// </summary>
    public static bool IsDialogActive()
    {
        var active = ActiveWindow();
        return active is not null && !ReferenceEquals(active, Application.Current?.MainWindow);
    }

    /// <summary>
    /// Garante que exista foco em algum lugar da janela ativa. Chamado antes de navegar: sem isso,
    /// uma janela recém-aberta não teria onde receber o direcional.
    /// </summary>
    public static void EnsureFocusInActiveWindow()
    {
        if (Keyboard.FocusedElement is FrameworkElement { IsVisible: true }) return;
        var window = ActiveWindow();
        if (window is not null) FocusFirst(window);
    }

    /// <summary>
    /// Leva o seletor para os botões de ação de um diálogo (Salvar / Cancelar / Fechar).
    ///
    /// É o que o botão B faz numa tela de configuração: em vez de fechar a janela na hora — e com
    /// isso descartar em silêncio o que a pessoa acabou de ajustar — o foco pula para a barra de
    /// ações, onde ela vê e escolhe. Apertar B de novo, já estando lá, aí sim fecha.
    ///
    /// Os botões são reconhecidos pelas propriedades padrão do WPF: <c>IsDefault</c> marca o botão
    /// primário (Salvar/Aplicar) e <c>IsCancel</c> o secundário (Cancelar/Fechar). Usar o que o
    /// framework já tem evita uma convenção própria e, de brinde, faz Enter e Esc funcionarem.
    ///
    /// Devolve:
    ///   • <c>true</c>  — o foco foi movido para a barra de ações;
    ///   • <c>false</c> — já estava lá (ou a janela não tem barra de ações), então quem chamou
    ///                    deve seguir com o fechamento.
    /// </summary>
    public static bool FocusDialogActions(Window window)
    {
        var actions = FindDescendants<Button>(window)
            .Where(b => (b.IsDefault || b.IsCancel) && b.IsVisible && b.IsEnabled)
            .ToList();

        if (actions.Count == 0) return false;

        // Já está na barra de ações? Então B significa "sair" de verdade.
        if (actions.Any(b => b.IsKeyboardFocused)) return false;

        // Preferimos o primário: numa tela de configuração, salvar é o desfecho esperado, e o
        // usuário ainda pode andar para o lado até Cancelar.
        var target = actions.FirstOrDefault(b => b.IsDefault) ?? actions[0];
        return target.Focus();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>Aciona o elemento focado (equivale ao clique) — botão A.</summary>
    public static bool Accept()
    {
        switch (Keyboard.FocusedElement)
        {
            // Marcáveis (abas, interruptores) alternam; o resto é acionado como um clique.
            case ToggleButton toggle when toggle.IsEnabled:
                toggle.IsChecked = toggle is RadioButton || toggle.IsChecked != true;
                return true;

            case Button button when button.IsEnabled:
                // OnClick é protegido, então usamos o mesmo caminho da automação de interface que o
                // Windows usa — respeita comando, IsEnabled e o evento Click da tela.
                var invoke = new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                    as System.Windows.Automation.Provider.IInvokeProvider;
                invoke?.Invoke();
                return true;

            case ComboBox combo:
                combo.IsDropDownOpen = !combo.IsDropDownOpen;
                return true;

            case TextBox:
                // Caixa de texto é tratada por quem abre o teclado virtual.
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Dá o foco ao primeiro elemento focável de um contêiner.
    ///
    /// Tenta de novo nos próximos ciclos de layout quando não encontra nada: listas com
    /// DataTemplate só criam os controles quando o WPF gera os containers, e isso costuma
    /// acontecer DEPOIS do quadro em que o painel ficou visível. Sem a repetição, o foco não tinha
    /// onde pousar e a entrada continuava indo para a tela de trás.
    /// </summary>
    public static void FocusFirst(DependencyObject root, int attempts = 4)
    {
        if (TryFocusFirst(root)) return;
        if (attempts <= 0) return;

        if (root is DispatcherObject dispatcherObject)
        {
            dispatcherObject.Dispatcher.BeginInvoke(
                new Action(() => FocusFirst(root, attempts - 1)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Uma tentativa única de focar o primeiro elemento focável.</summary>
    public static bool TryFocusFirst(DependencyObject root)
    {
        var first = FindFocusable(root);
        return first is not null && first.Focus();
    }

    /// <summary>O foco atual está dentro deste contêiner?</summary>
    public static bool IsFocusInside(DependencyObject container)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused) return false;

        var node = focused;
        while (node is not null)
        {
            if (ReferenceEquals(node, container)) return true;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private static FrameworkElement? FindFocusable(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            // ScrollViewer e ContentPresenter são "focáveis" tecnicamente mas não são o que o
            // usuário quer alcançar; descemos até um controle de verdade.
            if (child is FrameworkElement { Focusable: true, IsEnabled: true, IsVisible: true } element &&
                element is not ScrollViewer and not ItemsControl)
                return element;

            var nested = FindFocusable(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
