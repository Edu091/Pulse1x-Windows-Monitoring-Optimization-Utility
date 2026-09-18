using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulse1x.App.Controls;

/// <summary>
/// Seletor de cor visual, usado na personalização do Pulse1x.
///
/// O usuário escolhe a cor apontando: matiz na faixa colorida, saturação e brilho no quadrado. O
/// campo hexadecimal continua disponível para quem quer colar um código exato, mas deixou de ser
/// o único caminho — era isso que tornava a personalização trabalhosa.
///
/// A cor é exposta em <see cref="SelectedColor"/> (um <see cref="Color"/>, para ligar direto em
/// pincéis) e em <see cref="SelectedHex"/> (texto "#RRGGBB", que é como as configurações guardam).
/// </summary>
public partial class ColorPicker : UserControl
{
    /// <summary>Evita que a atualização da interface dispare de volta uma nova alteração.</summary>
    private bool _updating;

    private double _hue;          // 0..360
    private double _saturation;   // 0..1
    private double _value;        // 0..1

    public ColorPicker()
    {
        InitializeComponent();

        PresetList.ItemsSource = new[]
        {
            "#DC2626", "#EA580C", "#D97706", "#CA8A04", "#16A34A", "#059669",
            "#0891B2", "#2563EB", "#4F46E5", "#7C3AED", "#DB2777", "#E11D48",
            "#64748B", "#334155", "#0F172A", "#F8FAFC",
        }.Select(hex => (Color)ColorConverter.ConvertFromString(hex)!).ToList();

        Loaded += (_, _) => SyncFromColor(SelectedColor);
        SizeChanged += (_, _) => UpdateCursors();
    }

    // =====================================================================================
    //  Propriedades
    // =====================================================================================

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
            new FrameworkPropertyMetadata(Color.FromRgb(0xDC, 0x26, 0x26),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>A mesma cor em texto ("#RRGGBB") — é o formato que o settings.json guarda.</summary>
    public static readonly DependencyProperty SelectedHexProperty =
        DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorPicker),
            new FrameworkPropertyMetadata("#DC2626",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedHexChanged));

    public string SelectedHex
    {
        get => (string)GetValue(SelectedHexProperty);
        set => SetValue(SelectedHexProperty, value);
    }

    /// <summary>Matiz pura da faixa, usada como ponto final do gradiente do quadrado.</summary>
    public static readonly DependencyProperty HueColorProperty =
        DependencyProperty.Register(nameof(HueColor), typeof(Color), typeof(ColorPicker),
            new PropertyMetadata(Color.FromRgb(0xFF, 0x00, 0x00)));

    public Color HueColor
    {
        get => (Color)GetValue(HueColorProperty);
        private set => SetValue(HueColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._updating) return;
        picker.SyncFromColor((Color)e.NewValue);
    }

    private static void OnSelectedHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._updating) return;

        try
        {
            if (e.NewValue is string hex && !string.IsNullOrWhiteSpace(hex))
                picker.SyncFromColor((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch { /* texto incompleto enquanto o usuário digita — ignora até virar uma cor válida */ }
    }

    /// <summary>Recalcula matiz/saturação/brilho a partir de uma cor e atualiza toda a interface.</summary>
    private void SyncFromColor(Color color)
    {
        (_hue, _saturation, _value) = ToHsv(color);
        _updating = true;
        try
        {
            HueColor = FromHsv(_hue, 1, 1);
            SelectedColor = color;
            SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            HexBox.Text = SelectedHex;
        }
        finally { _updating = false; }

        UpdateCursors();
    }

    /// <summary>Publica a cor montada a partir dos valores atuais de matiz/saturação/brilho.</summary>
    private void PushColor()
    {
        var color = FromHsv(_hue, _saturation, _value);
        _updating = true;
        try
        {
            HueColor = FromHsv(_hue, 1, 1);
            SelectedColor = color;
            SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            HexBox.Text = SelectedHex;
        }
        finally { _updating = false; }

        UpdateCursors();
    }

    /// <summary>Recoloca os dois cursores conforme os valores atuais.</summary>
    private void UpdateCursors()
    {
        if (ShadeArea.ActualWidth > 0)
        {
            ShadeCursorPosition.X = _saturation * ShadeArea.ActualWidth - ShadeCursor.Width / 2;
            ShadeCursorPosition.Y = (1 - _value) * ShadeArea.ActualHeight - ShadeCursor.Height / 2;
        }

        if (HueArea.ActualWidth > 0)
            HueCursorPosition.X = _hue / 360.0 * HueArea.ActualWidth - HueCursor.Width / 2;
    }

    // =====================================================================================
    //  Interação
    // =====================================================================================

    private void ShadeArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ShadeArea.CaptureMouse();
        PickShade(e.GetPosition(ShadeArea));
    }

    private void ShadeArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ShadeArea.IsMouseCaptured)
            PickShade(e.GetPosition(ShadeArea));
    }

    private void ShadeArea_MouseUp(object sender, MouseButtonEventArgs e) => ShadeArea.ReleaseMouseCapture();

    private void PickShade(Point position)
    {
        if (ShadeArea.ActualWidth <= 0 || ShadeArea.ActualHeight <= 0) return;
        _saturation = Math.Clamp(position.X / ShadeArea.ActualWidth, 0, 1);
        _value = Math.Clamp(1 - position.Y / ShadeArea.ActualHeight, 0, 1);
        PushColor();
    }

    private void HueArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        PickHue(e.GetPosition(HueArea));
    }

    private void HueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && HueArea.IsMouseCaptured)
            PickHue(e.GetPosition(HueArea));
    }

    private void HueArea_MouseUp(object sender, MouseButtonEventArgs e) => HueArea.ReleaseMouseCapture();

    private void PickHue(Point position)
    {
        if (HueArea.ActualWidth <= 0) return;
        _hue = Math.Clamp(position.X / HueArea.ActualWidth, 0, 1) * 360;
        PushColor();
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHexBox();

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHexBox();
    }

    private void ApplyHexBox()
    {
        try
        {
            string text = HexBox.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            SyncFromColor((Color)ColorConverter.ConvertFromString(text)!);
        }
        catch
        {
            // Texto inválido: devolve o que estava, para o campo nunca ficar num estado impossível.
            HexBox.Text = SelectedHex;
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Color color }) SyncFromColor(color);
    }

    // =====================================================================================
    //  Conversões
    // =====================================================================================

    private static (double hue, double saturation, double value) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0.0001)
        {
            if (Math.Abs(max - r) < 0.0001) hue = (g - b) / delta % 6;
            else if (Math.Abs(max - g) < 0.0001) hue = (b - r) / delta + 2;
            else hue = (r - g) / delta + 4;

            hue *= 60;
            if (hue < 0) hue += 360;
        }

        double saturation = max <= 0.0001 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
