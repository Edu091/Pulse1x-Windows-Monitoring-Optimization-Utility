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

    /// <summary>
    /// Intensidade das animações (0 a 1), definida na personalização visual. Multiplica a duração e
    /// o deslocamento de cada animação: 1 é o comportamento original, 0,5 deixa tudo mais discreto e
    /// rápido, e 0 equivale a desligar. Lido em tempo real, como <see cref="Enabled"/>.
    /// </summary>
    public static double Intensity { get; set; } = 1.0;

    /// <summary>Escala uma duração pela intensidade, com um mínimo para nunca virar um salto seco.</summary>
    public static Duration Scale(Duration duration)
    {
        if (!duration.HasTimeSpan) return duration;
        double factor = Math.Clamp(Intensity, 0.2, 1.5);
        return new Duration(TimeSpan.FromMilliseconds(duration.TimeSpan.TotalMilliseconds * factor));
    }

    /// <summary>Escala um deslocamento (em pixels) pela intensidade.</summary>
    public static double ScaleOffset(double offset) => offset * Math.Clamp(Intensity, 0, 1.5);
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
    /// <remarks>
    /// A opacidade termina no valor PRÓPRIO do elemento (ex.: um rótulo com Opacity="0.55"), não em
    /// 1: antes, todo texto secundário que surgia com animação ficava aceso, com opacidade total,
    /// até a página ser recriada. A animação é removida ao terminar, para que bindings de Opacity
    /// voltem a valer.
    /// </remarks>
    public static void FadeSlideIn(UIElement element, double fromOffsetY = 14, Duration? duration = null)
    {
        if (element is null) return;

        if (!AnimationSettings.Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform t) t.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }

        var dur = AnimationSettings.Scale(duration ?? Normal);
        fromOffsetY = AnimationSettings.ScaleOffset(fromOffsetY);

        AnimateOpacityIn(element, dur, TimeSpan.Zero);
        var tt = EnsureTranslate(element);
        tt?.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromOffsetY, 0, dur) { EasingFunction = EaseOut });
    }

    /// <summary>
    /// Entrada de página: os blocos de primeiro nível do conteúdo (cabeçalho, cartões) surgem em
    /// cascata. Páginas sem um painel reconhecível entram inteiras com <see cref="FadeSlideIn"/>.
    /// </summary>
    public static void PageEnter(UIElement page)
    {
        if (page is null) return;
        var panel = FindStaggerPanel(page);
        if (panel is null || !AnimationSettings.Enabled)
        {
            FadeSlideIn(page);
            return;
        }

        StaggerIn(panel.Children.OfType<UIElement>(), offsetY: 16, stepMs: 40);
    }

    /// <summary>
    /// Faz uma lista de elementos surgir em cascata (fade + deslize), cada um um pouco depois do
    /// anterior. A partir de <paramref name="maxItems"/>, os demais entram junto com o último.
    /// </summary>
    public static void StaggerIn(IEnumerable<UIElement> elements, double offsetY = 12, int stepMs = 35, int maxItems = 12)
    {
        if (!AnimationSettings.Enabled) return;

        var dur = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(320)));
        double offset = AnimationSettings.ScaleOffset(offsetY);
        double step = stepMs * Math.Clamp(AnimationSettings.Intensity, 0.2, 1.5);
        int index = 0;

        foreach (var element in elements)
        {
            if (element.Visibility != Visibility.Visible) continue;

            var delay = TimeSpan.FromMilliseconds(Math.Min(index, maxItems) * step);
            AnimateOpacityIn(element, dur, delay);

            var tt = EnsureTranslate(element);
            if (tt is not null)
            {
                var slide = new DoubleAnimationUsingKeyFrames { Duration = dur.TimeSpan + delay, FillBehavior = FillBehavior.Stop };
                slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(offset, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(offset, KeyTime.FromTimeSpan(delay)));
                slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + dur.TimeSpan), new QuinticEase { EasingMode = EasingMode.EaseOut }));
                tt.BeginAnimation(TranslateTransform.YProperty, slide);
            }
            index++;
        }
    }

    /// <summary>
    /// Desliza a barra de acento da navegação até <paramref name="y"/>, esticando-a no meio do
    /// caminho (efeito elástico discreto). Sem animação, apenas posiciona.
    /// </summary>
    public static void SlideIndicator(FrameworkElement pill, TranslateTransform transform, double y, bool animate)
    {
        const double restHeight = 18;
        bool wasHidden = pill.Opacity < 0.5;
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        pill.Opacity = 1;

        if (!animate || wasHidden || !AnimationSettings.Enabled)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            pill.BeginAnimation(FrameworkElement.HeightProperty, null);
            transform.Y = y;
            pill.Height = restHeight;
            return;
        }

        var dur = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(380)));
        // Fixa a posição de destino como valor base: assim um SizeChanged no meio do caminho
        // reposiciona sem "voltar" para o item anterior.
        double from = transform.Y;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = y;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, y, dur) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });

        var stretch = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(restHeight + 14, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(restHeight, KeyTime.FromPercent(1.0), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        pill.Height = restHeight;
        pill.BeginAnimation(FrameworkElement.HeightProperty, stretch);
    }

    // Anima a opacidade de 0 até o valor base do elemento, começando após 'delay'. O quadro inicial
    // em 0 evita que o elemento apareça por um instante antes de o atraso terminar.
    private static void AnimateOpacityIn(UIElement element, Duration dur, TimeSpan delay)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        double target = element.Opacity;
        if (target <= 0.01) return;

        var fade = new DoubleAnimationUsingKeyFrames { Duration = dur.TimeSpan + delay, FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromTimeSpan(delay + dur.TimeSpan), EaseOut));
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // Garante um TranslateTransform no elemento sem destruir outra transformação que ele já use
    // (escala do clique, rotação do chevron). Nesse caso devolve null e só a opacidade anima.
    private static TranslateTransform? EnsureTranslate(UIElement element)
    {
        switch (element.RenderTransform)
        {
            case TranslateTransform existing when !existing.IsFrozen:
                return existing;
            case null:
            case MatrixTransform { IsFrozen: true } m when m.Matrix.IsIdentity:
            case Transform t when ReferenceEquals(t, Transform.Identity):
                var tt = new TranslateTransform();
                element.RenderTransform = tt;
                return tt;
            default:
                return null;
        }
    }

    // Desce do conteúdo da página (ScrollViewer → Border/Grid → StackPanel) até o painel cujos
    // filhos são os blocos da página.
    private static System.Windows.Controls.Panel? FindStaggerPanel(UIElement page)
    {
        object? node = page is System.Windows.Controls.Page p ? p.Content : page;
        for (int depth = 0; depth < 5 && node is not null; depth++)
        {
            switch (node)
            {
                case System.Windows.Controls.StackPanel stack when stack.Children.Count >= 2:
                    return stack;
                case System.Windows.Controls.ScrollViewer sv:
                    node = sv.Content;
                    break;
                case System.Windows.Controls.Decorator d:
                    node = d.Child;
                    break;
                case System.Windows.Controls.Grid g when g.Children.Count == 1:
                    node = g.Children[0];
                    break;
                case System.Windows.Controls.Grid g:
                    // Grade com cabeçalho fixo + rolagem: procura o ScrollViewer dela.
                    node = g.Children.OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault();
                    break;
                default:
                    return null;
            }
        }
        return null;
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

        var dur = AnimationSettings.Scale(new Duration(TimeSpan.FromMilliseconds(340)));
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
            new DoubleAnimation(to, AnimationSettings.Scale(duration ?? Fast)) { EasingFunction = EaseOut });
    }
}
