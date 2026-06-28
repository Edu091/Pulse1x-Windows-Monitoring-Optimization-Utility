using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pulse1x.App.Services;

/// <summary>
/// Flag global, lida em tempo real por todo o código de animação. Definido no startup a partir
/// das configurações e atualizado quando o usuário liga/desliga as animações nas Configurações.
/// </summary>
public static class AnimationSettings
{
    public static bool Enabled { get; set; } = true;
}

/// <summary>
/// Animações fluidas reutilizáveis do app (transições de página, abertura da janela, entradas de
/// conteúdo). Todas respeitam <see cref="AnimationSettings.Enabled"/>: quando desligado, o
/// elemento aparece instantaneamente no estado final, sem animar.
/// </summary>
public static class Animations
{
    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(200));
    public static readonly Duration Normal = new(TimeSpan.FromMilliseconds(300));

    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Entrada suave: fade + leve deslize de baixo para cima. Usada nas transições de página
    /// e em painéis que surgem.</summary>
    public static void FadeSlideIn(UIElement element, double fromOffsetY = 14, Duration? duration = null)
    {
        if (element is null) return;

        if (!AnimationSettings.Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }

        var dur = duration ?? Normal;
        var tt = new TranslateTransform(0, fromOffsetY);
        element.RenderTransform = tt;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, dur) { EasingFunction = EaseOut });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromOffsetY, 0, dur) { EasingFunction = EaseOut });
    }

    /// <summary>Animação de abertura da janela: fade + leve zoom (escala) do conteúdo raiz.</summary>
    public static void OpenWindow(UIElement root)
    {
        if (root is null) return;

        if (!AnimationSettings.Enabled)
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
            root.RenderTransform = Transform.Identity;
            return;
        }

        var dur = new Duration(TimeSpan.FromMilliseconds(340));
        var st = new ScaleTransform(0.985, 0.985);
        root.RenderTransform = st;
        root.RenderTransformOrigin = new Point(0.5, 0.5);

        // Valor BASE = 1 (visível): garante que, se a animação for removida por qualquer motivo,
        // o conteúdo nunca fique preso invisível. A animação parte de 0 só durante o fade-in.
        root.Opacity = 1;
        root.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, dur) { EasingFunction = EaseOut });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, dur) { EasingFunction = EaseOut });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, dur) { EasingFunction = EaseOut });
    }

    /// <summary>Feedback tátil de clique: encolhe levemente o elemento (escala) e volta. Usado no
    /// pressionar/soltar de botões. Não faz nada com as animações desligadas.</summary>
    public static void PressScale(FrameworkElement element, double to)
    {
        if (element is null || !AnimationSettings.Enabled) return;

        if (element.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            element.RenderTransform = st;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var dur = new Duration(TimeSpan.FromMilliseconds(90));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur) { EasingFunction = EaseOut });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur) { EasingFunction = EaseOut });
    }

    /// <summary>Anima a opacidade de um elemento até <paramref name="to"/> (ex.: acender/apagar a
    /// barra de acento da aba ativa). Instantâneo quando as animações estão desligadas.</summary>
    public static void FadeTo(UIElement element, double to, Duration? duration = null)
    {
        if (element is null) return;

        if (!AnimationSettings.Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            return;
        }

        // Anima a partir do valor atual até o alvo (não fixa um valor base antes, para não
        // anular a transição).
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(to, duration ?? Fast) { EasingFunction = EaseOut });
    }
}
