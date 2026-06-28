using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pulse1x.App.Models;

namespace Pulse1x.App.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MetricCard));

    public static readonly DependencyProperty PrimaryValueProperty =
        DependencyProperty.Register(nameof(PrimaryValue), typeof(string), typeof(MetricCard));

    public static readonly DependencyProperty SecondaryValueProperty =
        DependencyProperty.Register(nameof(SecondaryValue), typeof(string), typeof(MetricCard));

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(MetricCard), new PropertyMetadata("•"));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(MetricCard),
            new PropertyMetadata(Color.FromRgb(0x78, 0x71, 0x6C), OnAccentColorChanged));

    public static readonly DependencyProperty BadgeBrushProperty =
        DependencyProperty.Register(nameof(BadgeBrush), typeof(Brush), typeof(MetricCard));

    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(nameof(Percent), typeof(double), typeof(MetricCard),
            new PropertyMetadata(0d, OnPercentChanged));

    public static readonly DependencyProperty ShowProgressProperty =
        DependencyProperty.Register(nameof(ShowProgress), typeof(bool), typeof(MetricCard),
            new PropertyMetadata(true, OnShowProgressChanged));

    public static readonly DependencyProperty ProgressVisibilityProperty =
        DependencyProperty.Register(nameof(ProgressVisibility), typeof(Visibility), typeof(MetricCard),
            new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(UsageLevel), typeof(MetricCard),
            new PropertyMetadata(UsageLevel.Normal, OnLevelChanged));

    public static readonly DependencyProperty LevelBrushProperty =
        DependencyProperty.Register(nameof(LevelBrush), typeof(Brush), typeof(MetricCard),
            new PropertyMetadata(NormalBrush));

    public static readonly DependencyProperty DetailKindProperty =
        DependencyProperty.Register(nameof(DetailKind), typeof(string), typeof(MetricCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DetailIdProperty =
        DependencyProperty.Register(nameof(DetailId), typeof(string), typeof(MetricCard), new PropertyMetadata(""));

    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush ElevatedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly Brush CriticalBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string PrimaryValue
    {
        get => (string)GetValue(PrimaryValueProperty);
        set => SetValue(PrimaryValueProperty, value);
    }

    public string SecondaryValue
    {
        get => (string)GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    /// <summary>Texto curto (2-4 caracteres) exibido no selo de identidade do card.</summary>
    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    /// <summary>Cor de identidade do tipo de métrica (estável, independente do nível de uso).</summary>
    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public Brush BadgeBrush
    {
        get => (Brush)GetValue(BadgeBrushProperty);
        private set => SetValue(BadgeBrushProperty, value);
    }

    /// <summary>Percentual (0-100) exibido na barra de progresso, quando <see cref="ShowProgress"/> é true.</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public Visibility ProgressVisibility
    {
        get => (Visibility)GetValue(ProgressVisibilityProperty);
        private set => SetValue(ProgressVisibilityProperty, value);
    }

    public UsageLevel Level
    {
        get => (UsageLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Brush LevelBrush
    {
        get => (Brush)GetValue(LevelBrushProperty);
        set => SetValue(LevelBrushProperty, value);
    }

    /// <summary>Categoria do componente representado pelo card: "cpu", "gpu", "ram", "disk", "net".</summary>
    public string DetailKind
    {
        get => (string)GetValue(DetailKindProperty);
        set => SetValue(DetailKindProperty, value);
    }

    /// <summary>Identificador da instância quando há mais de uma (nome da GPU, letra do disco).</summary>
    public string DetailId
    {
        get => (string)GetValue(DetailIdProperty);
        set => SetValue(DetailIdProperty, value);
    }

    /// <summary>Disparado quando o usuário clica no card para abrir os detalhes do componente.</summary>
    public event EventHandler? Clicked;

    public MetricCard()
    {
        InitializeComponent();
        BadgeBrush = new SolidColorBrush(AccentColor);
    }

    private void CardBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        card.LevelBrush = (UsageLevel)e.NewValue switch
        {
            UsageLevel.Critico => CriticalBrush,
            UsageLevel.Elevado => ElevatedBrush,
            _ => NormalBrush
        };
        card.UpdateProgressFill();
    }

    private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MetricCard)d).BadgeBrush = new SolidColorBrush((Color)e.NewValue);
    }

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MetricCard)d).UpdateProgressFill();
    }

    private static void OnShowProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        card.ProgressVisibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateProgressFill();

    private void UpdateProgressFill()
    {
        double clamped = Math.Clamp(Percent, 0, 100);
        ProgressFill.Width = ProgressTrack.ActualWidth * clamped / 100.0;
    }
}
