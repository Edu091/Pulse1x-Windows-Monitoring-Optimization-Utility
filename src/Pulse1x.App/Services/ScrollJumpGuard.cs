using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Pulse1x.App.Services;

/// <summary>
/// Impede que a página "pule para cima" quando um botão é desabilitado ou escondido enquanto
/// trabalha (Aplicar, Reverter, Testar). O WPF tira o foco do botão nesse momento e o entrega a
/// um contêiner grande; ao receber o foco, o contêiner pede para ser trazido à vista e o
/// ScrollViewer rola até o topo dele. Aqui, nesse instante, os pedidos de rolagem são ignorados e
/// a posição de antes é mantida. Rolagens feitas pelo usuário (roda do mouse, teclado, controle)
/// continuam normais.
/// </summary>
public static class ScrollJumpGuard
{
    public static void Attach(ScrollViewer scroller)
    {
        bool guarding = false;
        double offset = 0;

        scroller.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            // Só quando o foco saiu porque o elemento ficou desabilitado/invisível, não por um clique
            // ou navegação do usuário para outro controle.
            if (e.OldFocus is not UIElement old || (old.IsEnabled && old.IsVisible)) return;
            if (!guarding) offset = scroller.VerticalOffset;
            guarding = true;
            scroller.Dispatcher.BeginInvoke(() => guarding = false, DispatcherPriority.ContextIdle);
        }), handledEventsToo: true);

        // O pedido de "trazer à vista" sobe do elemento que recebeu o foco até o ScrollViewer;
        // marcado como tratado no conteúdo, o ScrollViewer não rola.
        if (scroller.Content is UIElement content)
            content.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) =>
            {
                if (guarding) e.Handled = true;
            }), handledEventsToo: true);

        // Rede de segurança: se algo ainda rolar nesse intervalo, volta para onde estava.
        scroller.ScrollChanged += (_, e) =>
        {
            if (guarding && e.VerticalChange != 0 && Math.Abs(scroller.VerticalOffset - offset) > 0.5)
                scroller.ScrollToVerticalOffset(offset);
        };
    }
}
