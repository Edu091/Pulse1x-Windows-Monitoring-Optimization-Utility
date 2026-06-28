using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pulse1x.App.Converters;

/// <summary>Visible se QUALQUER valor booleano de entrada for true. Usado para manter o
/// cabeçalho de uma seção visível enquanto o usuário está personalizando o layout, mesmo
/// que a seção esteja marcada como oculta.</summary>
public class BoolOrVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Any(v => v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte bool em Visibility. Com ConverterParameter="invert", inverte o
/// resultado (true → Collapsed, false → Visible). Útil para alternar dois elementos
/// conforme um único booleano.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible quando a string não está vazia. Útil para mensagens de status.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverte um booleano (true → false). Útil para IsEnabled = !IsBusy.</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>Visible apenas quando o modo de personalização está ativo E a seção está
/// marcada como oculta — mostra um aviso no lugar do conteúdo escondido.</summary>
public class HiddenSectionPlaceholderVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool isCustomizing = values.Length > 0 && values[0] is true;
        bool isVisible = values.Length > 1 && values[1] is true;
        return isCustomizing && !isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
