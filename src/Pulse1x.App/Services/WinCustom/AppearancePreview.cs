using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Traduz uma <see cref="ComponentAppearance"/> em pincéis do WPF, para a seção mostrar como a
/// escolha vai ficar ANTES de aplicá-la ao Windows.
///
/// O preview reproduz o resultado com a mesma matemática que o <c>WindowComposition</c> usa para
/// montar a cor do accent (tint, saturação, luminosidade, alfa), de modo que o que aparece na
/// tela corresponda ao que o Windows vai desenhar. Onde o efeito é do sistema (blur/acrylic sobre
/// o papel de parede real), o preview usa uma aproximação — é a única parte que não pode ser
/// idêntica, porque depende do que estiver atrás da janela no momento.
/// </summary>
internal static class AppearancePreview
{
    /// <summary>
    /// Pincel que representa a aparência. <paramref name="backdrop"/> é o que está "atrás"
    /// (o papel de parede simulado), usado para os efeitos translúcidos.
    /// </summary>
    public static Brush BuildBrush(ComponentAppearance appearance)
    {
        var tint = Adjust(Parse(appearance.TintColor), appearance);

        switch (appearance.Kind)
        {
            case AppearanceKind.WindowsDefault:
                // Cinza neutro do Windows, opaco — é o "sem personalização".
                return new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));

            case AppearanceKind.Transparent:
                return Brushes.Transparent;

            case AppearanceKind.Translucent:
                return new SolidColorBrush(WithAlpha(tint,
                    appearance.Opacity * (1 - appearance.Transparency)));

            case AppearanceKind.Blur:
                return new SolidColorBrush(WithAlpha(tint,
                    appearance.TintIntensity * Clamp01(appearance.EffectIntensity)));

            case AppearanceKind.Glass:
                return new SolidColorBrush(WithAlpha(tint,
                    appearance.TintIntensity * 0.55 * Clamp01(appearance.EffectIntensity)));

            case AppearanceKind.Acrylic:
                return new SolidColorBrush(WithAlpha(tint,
                    appearance.TintIntensity * Clamp01(appearance.EffectIntensity)));

            case AppearanceKind.Mica:
                return new SolidColorBrush(WithAlpha(tint,
                    Math.Max(0.6, appearance.TintIntensity)));

            case AppearanceKind.SolidColor:
                return new SolidColorBrush(WithAlpha(tint, appearance.Opacity));

            case AppearanceKind.Gradient:
            {
                var start = Adjust(Parse(appearance.GradientStart), appearance);
                var end = Adjust(Parse(appearance.GradientEnd), appearance);
                var (from, to) = GradientPoints(appearance.GradientDirection);
                return new LinearGradientBrush(
                    WithAlpha(start, appearance.Opacity),
                    WithAlpha(end, appearance.Opacity),
                    from, to);
            }

            case AppearanceKind.Image:
                return (Brush?)BuildImageBrush(appearance) ?? new SolidColorBrush(WithAlpha(tint, appearance.Opacity));

            default:
                return Brushes.Transparent;
        }
    }

    /// <summary>
    /// Monta o pincel de imagem já com ajuste, posicionamento e escala. Devolve null se a imagem
    /// não puder ser lida, para quem chama cair no fundo de cor.
    /// </summary>
    private static ImageBrush? BuildImageBrush(ComponentAppearance appearance)
    {
        string? path = appearance.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // Limitar a largura decodificada mantém o preview leve mesmo com fotos de 4000 px.
            bitmap.DecodePixelWidth = 640;
            bitmap.EndInit();
            bitmap.Freeze();

            var brush = new ImageBrush(bitmap)
            {
                Opacity = Clamp01(appearance.ImageOpacity),
                Stretch = appearance.ImageFit switch
                {
                    ImageFit.Fit => Stretch.Uniform,
                    ImageFit.Stretch => Stretch.Fill,
                    ImageFit.Center or ImageFit.Tile => Stretch.None,
                    _ => Stretch.UniformToFill,
                },
            };

            if (appearance.ImageFit == ImageFit.Tile)
            {
                brush.TileMode = TileMode.Tile;
                brush.Viewport = new Rect(0, 0, 0.25, 0.25);
                brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
            }
            else
            {
                // Deslocamento e escala: o alinhamento relativo move a imagem dentro da região,
                // que é o "ajuste e posicionamento" pedido (e o crop manual).
                double scale = Math.Max(0.1, appearance.ImageScale);
                brush.Viewport = new Rect(
                    appearance.ImageOffsetX * 0.5,
                    appearance.ImageOffsetY * 0.5,
                    scale, scale);
                brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
            }

            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Raio de desfoque a aplicar no preview — zero quando o efeito não desfoca.</summary>
    public static double BlurRadiusFor(ComponentAppearance appearance) =>
        CapabilityMatrix.SupportsBlurRadius(appearance.Kind)
            ? Math.Max(0, appearance.BlurRadius) * Clamp01(appearance.EffectIntensity)
            : 0;

    /// <summary>Opacidade do véu de escurecimento sobre o fundo.</summary>
    public static double DarkenFor(ComponentAppearance appearance) => Clamp01(appearance.Darken);

    // =====================================================================================
    //  Cor
    // =====================================================================================

    private static (Point, Point) GradientPoints(GradientDirection direction) => direction switch
    {
        GradientDirection.Horizontal => (new Point(0, 0.5), new Point(1, 0.5)),
        GradientDirection.DiagonalDown => (new Point(0, 0), new Point(1, 1)),
        GradientDirection.DiagonalUp => (new Point(0, 1), new Point(1, 0)),
        _ => (new Point(0.5, 0), new Point(0.5, 1)),
    };

    /// <summary>Aplica saturação e luminosidade — a mesma ordem usada na camada nativa.</summary>
    private static Color Adjust(Color color, ComponentAppearance appearance)
    {
        color = Luminosity(color, appearance.Luminosity);
        return Saturate(color, appearance.Saturation);
    }

    private static Color Luminosity(Color c, double amount)
    {
        if (Math.Abs(amount) < 0.001) return c;
        if (amount > 0)
            return Color.FromRgb(
                (byte)(c.R + (255 - c.R) * amount),
                (byte)(c.G + (255 - c.G) * amount),
                (byte)(c.B + (255 - c.B) * amount));

        double k = 1 + amount;
        return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    private static Color Saturate(Color c, double saturation)
    {
        if (Math.Abs(saturation - 1.0) < 0.001) return c;
        double gray = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        return Color.FromRgb(
            (byte)Math.Clamp(gray + (c.R - gray) * saturation, 0, 255),
            (byte)Math.Clamp(gray + (c.G - gray) * saturation, 0, 255),
            (byte)Math.Clamp(gray + (c.B - gray) * saturation, 0, 255));
    }

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255.0, 0, 255), c.R, c.G, c.B);

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    public static Color Parse(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.FromRgb(0x1A, 0x1A, 0x24);
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch { return Color.FromRgb(0x1A, 0x1A, 0x24); }
    }
}
