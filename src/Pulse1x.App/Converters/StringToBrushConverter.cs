using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse1x.App.Converters;

/// <summary>Converte uma string de cor hexadecimal (ex.: "#DC2626") em um SolidColorBrush.
/// Usado para colorir notas, severidades e níveis de risco do Diagnóstico Inteligente.</summary>
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* cor inválida — cai no padrão */ }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
