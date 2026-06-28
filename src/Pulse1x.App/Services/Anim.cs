using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pulse1x.App.Services;

/// <summary>
/// Propriedades anexadas (attached properties) de animação, usadas direto no XAML para deixar a
/// interface fluida sem mexer na lógica existente:
///
/// • <c>anim:Anim.AnimateVisibility="True"</c> — quando o elemento PASSA a ficar visível (ex.: ao
///   expandir um painel), ele entra com fade + leve deslize. Aditivo: mantém o binding de
///   <c>Visibility</c> que já existe.
/// • <c>anim:Anim.Spin="{Binding IsExpanded}"</c> — gira o elemento 180° (ex.: o chevron ⌄→⌃)
///   conforme o booleano.
/// • <c>anim:Anim.PopOnLoad="True"</c> — o elemento "surge" (fade + deslize) quando é carregado;
///   ótimo no item-template de listas que se populam por ação do usuário.
///
/// Tudo respeita <see cref="AnimationSettings.Enabled"/> (toggle nas Configurações): desligado,
/// os elementos aparecem instantaneamente, sem animar.
/// </summary>
public static class Anim
{
    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    // ===================== AnimateVisibility =====================

    public static readonly DependencyProperty AnimateVisibilityProperty =
        DependencyProperty.RegisterAttached("AnimateVisibility", typeof(bool), typeof(Anim),
            new PropertyMetadata(false, OnAnimateVisibilityChanged));

    public static void SetAnimateVisibility(DependencyObject o, bool value) => o.SetValue(AnimateVisibilityProperty, value);
    public static bool GetAnimateVisibility(DependencyObject o) => (bool)o.GetValue(AnimateVisibilityProperty);

    private static void OnAnimateVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue) fe.IsVisibleChanged += OnIsVisibleChanged;
        else fe.IsVisibleChanged -= OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement fe && e.NewValue is true)
            Animations.FadeSlideIn(fe, fromOffsetY: 10, duration: new Duration(TimeSpan.FromMilliseconds(240)));
    }

    // ===================== PopOnLoad =====================

    public static readonly DependencyProperty PopOnLoadProperty =
        DependencyProperty.RegisterAttached("PopOnLoad", typeof(bool), typeof(Anim),
            new PropertyMetadata(false, OnPopOnLoadChanged));

    public static void SetPopOnLoad(DependencyObject o, bool value) => o.SetValue(PopOnLoadProperty, value);
    public static bool GetPopOnLoad(DependencyObject o) => (bool)o.GetValue(PopOnLoadProperty);

    private static void OnPopOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.NewValue is not true) return;
        if (fe.IsLoaded) Animations.FadeSlideIn(fe, fromOffsetY: 8, duration: new Duration(TimeSpan.FromMilliseconds(220)));
        else fe.Loaded += PopOnLoadHandler;
    }

    private static void PopOnLoadHandler(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
            Animations.FadeSlideIn(fe, fromOffsetY: 8, duration: new Duration(TimeSpan.FromMilliseconds(220)));
    }

    // ===================== Spin (rotação do chevron) =====================

    public static readonly DependencyProperty SpinProperty =
        DependencyProperty.RegisterAttached("Spin", typeof(bool), typeof(Anim),
            new PropertyMetadata(false, OnSpinChanged));

    public static void SetSpin(DependencyObject o, bool value) => o.SetValue(SpinProperty, value);
    public static bool GetSpin(DependencyObject o) => (bool)o.GetValue(SpinProperty);

    private static void OnSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        double target = (bool)e.NewValue ? 180 : 0;

        if (fe.RenderTransform is not RotateTransform rt)
        {
            rt = new RotateTransform(0);
            fe.RenderTransform = rt;
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if (!AnimationSettings.Enabled)
        {
            rt.BeginAnimation(RotateTransform.AngleProperty, null);
            rt.Angle = target;
            return;
        }

        rt.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = EaseOut });
    }
}
