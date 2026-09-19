using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace Pulse1x.App.Services;

/// <summary>De onde vem o plano de fundo do Pulse1x inteiro.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundMode
{
    /// <summary>Fundo padrão do Pulse1x (Mica do Windows, como sempre foi).</summary>
    Default,
    SolidColor,
    Gradient,
    Image,
    /// <summary>Usa o background do jogo selecionado no GameHub.</summary>
    GameBased,
}

/// <summary>Como a imagem de fundo preenche a janela.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundFit { Fill, Fit, Stretch, Center, Tile }

/// <summary>
/// Personalização visual de todo o Pulse1x (não só do GameHub): cores, transparência, intensidade
/// do blur e das animações, e o plano de fundo do aplicativo. É persistida junto com o restante das
/// configurações, em settings.json.
/// </summary>
public class AppearanceSettings
{
    // ---- Cores ----
    /// <summary>Cor principal — botões, destaques e a identidade do app (padrão: o vermelho Pulse1x).</summary>
    public string PrimaryColor { get; set; } = "#DC2626";
    /// <summary>Cor secundária — estados de hover/pressionado e elementos de apoio.</summary>
    public string SecondaryColor { get; set; } = "#B91C1C";
    /// <summary>Cor de destaque — indicadores, seleção e foco.</summary>
    public string AccentColor { get; set; } = "#F87171";

    // ---- Intensidades (0 a 1) ----
    /// <summary>Transparência dos painéis sobre o fundo. 0 = opaco.</summary>
    public double Transparency { get; set; }
    /// <summary>Intensidade do desfoque dos painéis e do fundo.</summary>
    public double BlurIntensity { get; set; } = 0.35;
    /// <summary>Intensidade (e velocidade) das animações. 0 = sem animação.</summary>
    public double AnimationIntensity { get; set; } = 1.0;

    // ---- Plano de fundo ----
    public BackgroundMode Background { get; set; } = BackgroundMode.Default;
    public string? BackgroundImagePath { get; set; }
    public string BackgroundColor { get; set; } = "#0B0B0F";
    public string GradientStart { get; set; } = "#1A1A24";
    public string GradientEnd { get; set; } = "#0B0B0F";
    public BackgroundFit BackgroundFit { get; set; } = BackgroundFit.Fill;

    /// <summary>Opacidade da imagem de fundo (0 a 1).</summary>
    public double BackgroundOpacity { get; set; } = 0.55;
    /// <summary>Desfoque aplicado à imagem de fundo, em pixels.</summary>
    public double BackgroundBlur { get; set; } = 18;
    /// <summary>Escurecimento sobre a imagem (0 a 1) — garante a leitura dos textos.</summary>
    public double BackgroundDarken { get; set; } = 0.45;
    /// <summary>Saturação da imagem (0 = cinza, 1 = original, até 2 = mais viva).</summary>
    public double BackgroundSaturation { get; set; } = 1.0;

    // ---- GameHub ----
    /// <summary>Extrai as cores predominantes da capa do jogo selecionado e adapta a interface.</summary>
    public bool AdaptColorsToGame { get; set; }
}

/// <summary>
/// Aplica a personalização visual ao aplicativo inteiro, em tempo real.
///
/// A estratégia é trocar a COR dentro dos pincéis que já existem em App.xaml, em vez de substituir
/// os recursos. Isso faz a mudança chegar tanto a quem usa DynamicResource quanto a quem usa
/// StaticResource — ou seja, a todas as telas do Pulse1x de uma vez, sem reiniciar e sem precisar
/// alterar cada página.
/// </summary>
public class ThemeService
{
    private readonly SettingsService _settings;

    /// <summary>Cores derivadas do jogo selecionado, quando a adaptação automática está ligada.</summary>
    private Color? _gameAccent;

    /// <summary>Disparado quando a aparência muda, para as telas que desenham o fundo se atualizarem.</summary>
    public event Action? AppearanceChanged;

    public ThemeService(SettingsService settings) => _settings = settings;

    public AppearanceSettings Appearance => _settings.Current.Appearance;

    /// <summary>Cor principal em vigor — a do jogo, quando a adaptação está ligada, ou a do usuário.</summary>
    public Color EffectivePrimary =>
        Appearance.AdaptColorsToGame && _gameAccent is Color game ? game : Parse(Appearance.PrimaryColor);

    // =====================================================================================
    //  Aplicação
    // =====================================================================================

    /// <summary>Aplica tudo: cores de marca, transparência, intensidade das animações e o fundo.</summary>
    public void Apply()
    {
        ApplyColors();
        ApplyTransparency();
        ApplyAnimationIntensity();
        AppearanceChanged?.Invoke();
    }

    /// <summary>
    /// Raio de desfoque efetivo de um efeito, já ponderado pela intensidade global escolhida pelo
    /// usuário. Quem desenha blur (o fundo do app, o destaque do GameHub) passa o valor de projeto
    /// por aqui, de modo que um único ajuste governe o desfoque do Pulse1x inteiro.
    /// </summary>
    public double EffectiveBlur(double baseRadius) =>
        baseRadius * Math.Clamp(Appearance.BlurIntensity, 0, 1);

    /// <summary>
    /// Cores originais dos pincéis de painel, guardadas na primeira aplicação. Sem isso, aplicar a
    /// transparência duas vezes iria comendo o alfa a cada passada, até o painel sumir.
    /// </summary>
    private readonly Dictionary<string, Color> _basePanelColors = new();

    private static readonly string[] PanelBrushKeys =
    {
        "ControlFillColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
        "CardBackgroundFillColorDefaultBrush",
        "LayerFillColorDefaultBrush",
        "SolidBackgroundFillColorBaseBrush",
    };

    /// <summary>
    /// Deixa os painéis translúcidos conforme a transparência escolhida, para o plano de fundo
    /// aparecer através deles. Mexe apenas no canal alfa — a cor do tema (claro/escuro) continua
    /// sendo a do Wpf.Ui.
    /// </summary>
    private void ApplyTransparency()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        double transparency = Math.Clamp(Appearance.Transparency, 0, 1);

        foreach (var key in PanelBrushKeys)
        {
            if (resources[key] is not SolidColorBrush brush || brush.IsFrozen) continue;

            if (!_basePanelColors.TryGetValue(key, out var baseColor))
            {
                baseColor = brush.Color;
                _basePanelColors[key] = baseColor;
            }

            // Guardamos um mínimo de opacidade: painéis totalmente transparentes deixariam o texto
            // ilegível sobre uma imagem de fundo.
            byte alpha = (byte)Math.Clamp(baseColor.A * (1 - transparency * 0.85), 0, 255);
            brush.Color = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }
    }

    /// <summary>
    /// Espalha as três cores pelos pincéis de marca do app. As chaves são as mesmas que o App.xaml
    /// já definia para deixar os controles do Wpf.Ui vermelhos — agora elas seguem a escolha do
    /// usuário.
    /// </summary>
    private void ApplyColors()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        Color primary = EffectivePrimary;
        Color secondary = Appearance.AdaptColorsToGame && _gameAccent is not null
            ? Darken(primary, 0.18)
            : Parse(Appearance.SecondaryColor);
        Color accent = Appearance.AdaptColorsToGame && _gameAccent is not null
            ? Lighten(primary, 0.22)
            : Parse(Appearance.AccentColor);

        // Cor (não pincel) usada pelos gráficos em tempo real, que pintam a linha em código.
        resources["BrandAccentColor"] = primary;

        SetBrush(resources, "BrandAccentBrush", primary);
        SetBrush(resources, "BrandSecondaryBrush", secondary);
        SetBrush(resources, "BrandHighlightBrush", accent);

        SetBrush(resources, "AccentFillColorDefaultBrush", primary);
        SetBrush(resources, "AccentFillColorSecondaryBrush", secondary);
        SetBrush(resources, "AccentFillColorTertiaryBrush", Darken(primary, 0.30));
        SetBrush(resources, "AccentTextFillColorPrimaryBrush", primary);
        SetBrush(resources, "AccentTextFillColorSecondaryBrush", secondary);
        SetBrush(resources, "AccentTextFillColorTertiaryBrush", Darken(primary, 0.30));

        // O dicionário do Wpf.Ui pode ter sido carregado antes de a preferência de tema ser
        // aplicada. Definimos os pincéis de texto explicitamente para que textos sem Foreground
        // próprio nunca permaneçam pretos no modo escuro (em especial nas Configurações).
        bool dark = _settings.Current.DarkTheme;
        SetBrush(resources, "TextFillColorPrimaryBrush", dark ? Color.FromRgb(0xF2, 0xF3, 0xF5) : Color.FromRgb(0x1A, 0x1D, 0x21));
        SetBrush(resources, "TextFillColorSecondaryBrush", dark ? Color.FromRgb(0xC5, 0xC8, 0xD0) : Color.FromRgb(0x52, 0x57, 0x61));
        SetBrush(resources, "TextFillColorTertiaryBrush", dark ? Color.FromRgb(0x99, 0x9E, 0xA8) : Color.FromRgb(0x73, 0x78, 0x82));

        SetBrush(resources, "ScrollBarThumbFill", primary);
        SetBrush(resources, "ScrollBarTrackFillPointerOver", secondary);

        SetBrush(resources, "ToggleSwitchFillOn", primary);
        SetBrush(resources, "ToggleSwitchFillOnPointerOver", secondary);
        SetBrush(resources, "ToggleSwitchFillOnPressed", Darken(primary, 0.30));
        SetBrush(resources, "ToggleSwitchStrokeOn", primary);
        SetBrush(resources, "ToggleSwitchStrokeOnPointerOver", secondary);
        SetBrush(resources, "ToggleSwitchStrokeOnPressed", Darken(primary, 0.30));

        // NÃO chamar ApplicationAccentColorManager.Apply aqui.
        //
        // Ele não troca só o acento: recria os pincéis de acento E de texto do dicionário do
        // Wpf.Ui. Chamado uma segunda vez (o App.xaml.cs já aplica o acento na inicialização),
        // os pincéis recriados vinham com o texto PRETO mesmo no tema escuro — e como as caixas
        // de combinação e de texto herdam esse Foreground, elas apareciam como blocos claros com
        // o texto invisível. Era o que quebrava a janela de perfis do GameHub.
        //
        // Todas as chaves de acento que aquele gerenciador preencheria já são definidas acima por
        // SetBrush, com as cores escolhidas pelo usuário, então a chamada era redundante além de
        // destrutiva. O acento inicial continua sendo aplicado uma única vez em App.xaml.cs.
    }

    /// <summary>
    /// Muda a cor de um pincel existente em vez de trocar o recurso: assim os bindings
    /// StaticResource já resolvidos também mudam de cor na hora.
    /// </summary>
    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }

    private void ApplyAnimationIntensity()
    {
        AnimationSettings.Intensity = Appearance.AnimationIntensity;
        // A intensidade zero equivale a desligar as animações; o interruptor das Configurações
        // continua valendo por cima disso.
        AnimationSettings.Enabled = _settings.Current.AnimationsEnabled && Appearance.AnimationIntensity > 0.01;
    }

    // =====================================================================================
    //  Fundo
    // =====================================================================================

    /// <summary>
    /// Monta o pincel de fundo do aplicativo conforme o modo escolhido. Devolve null no modo padrão,
    /// que é quando o Pulse1x deixa o Mica do Windows aparecer, como sempre fez.
    /// </summary>
    public Brush? BuildBackgroundBrush()
    {
        switch (Appearance.Background)
        {
            case BackgroundMode.SolidColor:
                return new SolidColorBrush(Parse(Appearance.BackgroundColor));

            case BackgroundMode.Gradient:
                return new LinearGradientBrush(
                    Parse(Appearance.GradientStart), Parse(Appearance.GradientEnd),
                    new Point(0, 0), new Point(1, 1));

            default:
                return null;
        }
    }

    /// <summary>Caminho da imagem de fundo em vigor (própria do usuário ou a do jogo selecionado).</summary>
    public string? CurrentBackgroundImage { get; private set; }

    /// <summary>
    /// Informa ao tema qual jogo está selecionado no GameHub. Alimenta o fundo "baseado no jogo" e,
    /// quando a adaptação de cores está ligada, tira o acento da arte dele.
    /// </summary>
    public void SetSelectedGameArt(string? heroPath, string? coverPath)
    {
        if (Appearance.Background == BackgroundMode.GameBased)
            CurrentBackgroundImage = heroPath ?? coverPath;
        else if (Appearance.Background == BackgroundMode.Image)
            CurrentBackgroundImage = Appearance.BackgroundImagePath;
        else
            CurrentBackgroundImage = null;

        if (Appearance.AdaptColorsToGame)
        {
            var source = coverPath ?? heroPath;
            _gameAccent = source is null ? null : ColorExtractor.DominantAccent(source);
            ApplyColors();
        }

        AppearanceChanged?.Invoke();
    }

    /// <summary>Recalcula a imagem de fundo depois de uma troca de modo nas Configurações.</summary>
    public void RefreshBackgroundImage()
    {
        CurrentBackgroundImage = Appearance.Background == BackgroundMode.Image
            ? Appearance.BackgroundImagePath
            : CurrentBackgroundImage;

        if (Appearance.Background is not (BackgroundMode.Image or BackgroundMode.GameBased))
            CurrentBackgroundImage = null;
    }

    public void Save()
    {
        _settings.Save();
        Apply();
    }

    /// <summary>Volta a aparência aos padrões de fábrica do Pulse1x.</summary>
    public void ResetToDefaults()
    {
        _settings.Current.Appearance = new AppearanceSettings();
        _gameAccent = null;
        CurrentBackgroundImage = null;
        Save();
    }

    // =====================================================================================
    //  Utilidades de cor
    // =====================================================================================

    public static Color Parse(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.FromRgb(0xDC, 0x26, 0x26);
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch { return Color.FromRgb(0xDC, 0x26, 0x26); }
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount)));

    public static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount));
}
