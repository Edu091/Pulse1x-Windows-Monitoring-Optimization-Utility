using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse1x.App.Converters;

/// <summary>Visível quando o valor NÃO é nulo (ex.: esconder o painel de destaque sem seleção).</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is not null;
        if (parameter as string == "invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Compara o valor com o parâmetro (nome do membro de enum ou texto) e devolve true quando são
/// iguais. Usado nos grupos de opção da personalização — um RadioButton por modo de fundo.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    /// <summary>Só o botão que foi marcado devolve valor; os desmarcados não alteram a propriedade.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        try { return Enum.Parse(targetType, parameter.ToString()!); }
        catch { return Binding.DoNothing; }
    }
}

/// <summary>Converte o texto hexadecimal de uma cor num pincel (pré-visualização nas Configurações).</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch { }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Multiplica um número por um fator (passado no parâmetro). Serve para derivar medidas da interface
/// de um único ajuste — por exemplo, o raio do blur a partir da intensidade escolhida pelo usuário.
/// </summary>
public class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double number = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        double factor = parameter is null ? 1 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return number * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte o modo de preenchimento do fundo no <see cref="Stretch"/> correspondente.</summary>
public class BackgroundFitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Fit" => Stretch.Uniform,
            "Stretch" => Stretch.Fill,
            "Center" or "Tile" => Stretch.None,
            _ => Stretch.UniformToFill,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Habilita os controles de uma seção do perfil apenas no modo "Personalizado" — nos modos
/// "Não alterar" e "Automático" os valores não são usados, então mostrá-los editáveis só
/// confundiria. Recebe a opção selecionada no seletor de modo.
/// </summary>
public class ModeIsCustomConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A opção é um ModeOption; comparamos pelo nome do modo para não acoplar os conversores
        // à camada de ViewModels.
        var mode = value?.GetType().GetProperty("Mode")?.GetValue(value)?.ToString();
        bool isCustom = mode == "Custom";
        return parameter as string == "invert" ? !isCustom : isCustom;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visível quando a seção está em modo "Personalizado".</summary>
public class ModeIsCustomVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mode = value?.GetType().GetProperty("Mode")?.GetValue(value)?.ToString();
        bool isCustom = mode == "Custom";
        if (parameter as string == "invert") isCustom = !isCustom;
        return isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Traduz uma chave de localização vinda dos dados (por exemplo, o rótulo de um modo do fabricante,
/// que é decidido em tempo de execução conforme o notebook detectado).
/// </summary>
public class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && key.Length > 0 ? Localization.Loc.S(key) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Altura de uma barra do gráfico: proporção (0 a 1) vezes a altura disponível. Feito como
/// conversor porque a altura real só é conhecida em tempo de layout.
/// </summary>
public class RatioToHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 3.0;
        double ratio = System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture);
        double available = System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture);

        // Reserva espaço para o rótulo do dia embaixo e garante um mínimo visível.
        double usable = Math.Max(0, available - 22);
        return Math.Max(3, ratio * usable);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
