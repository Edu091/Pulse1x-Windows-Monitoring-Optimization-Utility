using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulse1x.App.Services;

/// <summary>
/// Extrai a cor predominante de uma capa/background para adaptar os detalhes da interface ao jogo
/// selecionado.
///
/// Não basta pegar a cor mais frequente: em quase toda capa, a cor mais comum é um preto ou um
/// cinza de fundo, que daria um acento sem graça. Por isso a imagem é reduzida, as cores são
/// agrupadas em faixas e a escolha pondera frequência e vivacidade, descartando o que é escuro
/// demais, claro demais ou sem saturação. O resultado é ajustado para ter contraste garantido
/// sobre o tema escuro.
/// </summary>
public static class ColorExtractor
{
    /// <summary>Tamanho da miniatura analisada — suficiente para a cor e barato de decodificar.</summary>
    private const int SampleSize = 64;

    private static readonly Dictionary<string, Color?> Cache = new();

    /// <summary>Cor de acento derivada da imagem (null quando não dá para ler o arquivo).</summary>
    public static Color? DominantAccent(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

        lock (Cache)
            if (Cache.TryGetValue(imagePath, out var cached)) return cached;

        Color? result = Compute(imagePath);

        lock (Cache)
        {
            // Um limite simples evita o cache crescer sem fim numa biblioteca grande.
            if (Cache.Count > 300) Cache.Clear();
            Cache[imagePath] = result;
        }

        return result;
    }

    private static Color? Compute(string imagePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = SampleSize;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            if (width == 0 || height == 0) return null;

            int stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            // Agrupa em faixas de 32 níveis por canal: junta tons próximos sem perder a identidade.
            var buckets = new Dictionary<int, (long count, long r, long g, long b)>();

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                if (a < 128) continue;

                var (_, saturation, lightness) = ToHsl(r, g, b);
                // Descarta o que não serviria como acento: quase preto, quase branco e cinzas.
                if (lightness < 0.12 || lightness > 0.92 || saturation < 0.18) continue;

                int key = (r / 32 << 10) | (g / 32 << 5) | (b / 32);
                var current = buckets.TryGetValue(key, out var existing) ? existing : default;
                buckets[key] = (current.count + 1, current.r + r, current.g + g, current.b + b);
            }

            if (buckets.Count == 0) return null;

            // Pontuação: quanto a cor aparece, valorizada pela vivacidade. Assim uma cor viva que
            // ocupa uma parte razoável da capa ganha de um tom apagado que ocupa um pouco mais.
            (long count, long r, long g, long b) best = default;
            double bestScore = -1;

            foreach (var bucket in buckets.Values)
            {
                byte r = (byte)(bucket.r / bucket.count);
                byte g = (byte)(bucket.g / bucket.count);
                byte b = (byte)(bucket.b / bucket.count);
                var (_, saturation, lightness) = ToHsl(r, g, b);

                double score = bucket.count * (0.45 + saturation) * (1.0 - Math.Abs(lightness - 0.55));
                if (score > bestScore) { bestScore = score; best = bucket; }
            }

            if (best.count == 0) return null;

            var color = Color.FromRgb(
                (byte)(best.r / best.count), (byte)(best.g / best.count), (byte)(best.b / best.count));

            return EnsureReadable(color);
        }
        catch { return null; }
    }

    /// <summary>
    /// Garante que a cor funcione como acento: satura um pouco o que estiver apagado e leva a
    /// luminosidade para uma faixa que tem contraste tanto no tema escuro quanto no claro.
    /// </summary>
    private static Color EnsureReadable(Color color)
    {
        var (hue, saturation, lightness) = ToHsl(color.R, color.G, color.B);
        saturation = Math.Clamp(Math.Max(saturation, 0.45), 0, 0.95);
        lightness = Math.Clamp(lightness, 0.42, 0.62);
        return FromHsl(hue, saturation, lightness);
    }

    private static (double hue, double saturation, double lightness) ToHsl(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;
        double delta = max - min;

        if (delta < 0.0001) return (0, 0, lightness);

        double saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);

        double hue;
        if (Math.Abs(max - red) < 0.0001) hue = (green - blue) / delta % 6;
        else if (Math.Abs(max - green) < 0.0001) hue = (blue - red) / delta + 2;
        else hue = (red - green) / delta + 4;

        hue *= 60;
        if (hue < 0) hue += 360;
        return (hue, saturation, lightness);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - c / 2;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
