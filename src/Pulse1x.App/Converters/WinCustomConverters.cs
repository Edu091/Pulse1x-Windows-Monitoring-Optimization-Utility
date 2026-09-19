using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Converters;

/// <summary>
/// Nome traduzido de um valor de enum da Personalização do Windows. O parâmetro é o prefixo da
/// chave ("WinCustom_Kind_", "WinCustom_Fit_"), de modo que um único conversor sirva a todos os
/// seletores da seção.
/// </summary>
public class EnumToLocalizedNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "";
        string prefix = parameter as string ?? "";
        return Loc.S(prefix + value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible quando o valor booleano é falso. Usado para mostrar o aviso de indisponibilidade
/// justamente quando a opção NÃO está disponível.
/// </summary>
public class FalseToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converte o raio de desfoque do preview num <see cref="Visibility"/>: o efeito de blur do WPF
/// custa caro, então a camada só é montada quando há desfoque de fato.
/// </summary>
public class BlurToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && d > 0.5 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Multiplica um double por um fator passado como parâmetro (ex.: opacidade → 0-100).</summary>
public class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return 0d;
        double factor = 1;
        if (parameter is string s) double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out factor);
        return d * factor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
