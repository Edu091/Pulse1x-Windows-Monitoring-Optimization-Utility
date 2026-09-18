using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulse1x.App.Services;

/// <summary>
/// Ajustes de imagem feitos em pixels, para o plano de fundo do aplicativo.
///
/// O WPF traz desfoque pronto (BlurEffect), mas não saturação — e trocar a saturação sobrepondo
/// camadas semitransparentes só dá um resultado convincente para dessaturar. Como o fundo é
/// carregado uma única vez e depois fica parado, vale processar os pixels de verdade: o resultado é
/// exato nos dois sentidos (acinzentar e realçar) e não custa nada em tempo de execução.
/// </summary>
public static class ImageEffects
{
    /// <summary>
    /// Aplica saturação à imagem. 0 deixa em tons de cinza, 1 mantém o original e valores acima de
    /// 1 realçam as cores. Devolve a própria imagem quando não há o que fazer.
    /// </summary>
    public static BitmapSource ApplySaturation(BitmapSource source, double saturation)
    {
        if (Math.Abs(saturation - 1.0) < 0.01) return source;

        try
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                // Luminância percebida (Rec. 601): é o cinza que o olho enxerga naquela cor.
                double gray = 0.299 * r + 0.587 * g + 0.114 * b;

                pixels[i] = Clamp(gray + (b - gray) * saturation);
                pixels[i + 1] = Clamp(gray + (g - gray) * saturation);
                pixels[i + 2] = Clamp(gray + (r - gray) * saturation);
            }

            var result = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
            result.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
            result.Freeze();
            return result;
        }
        catch
        {
            // Qualquer problema no processamento devolve a imagem original — o fundo continua
            // aparecendo, só sem o ajuste.
            return source;
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);
}
