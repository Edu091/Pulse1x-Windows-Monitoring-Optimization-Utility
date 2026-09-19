using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Temas de fábrica — os pontos de partida que aparecem já na primeira abertura da seção.
///
/// Todos usam APENAS aparências que funcionam sem o motor de injeção (composição por HWND), de
/// modo que qualquer um deles produza um resultado visível assim que aplicado, sem exigir que o
/// usuário ligue a parte beta. Os alvos que dependem de injeção ficam no padrão do Windows.
/// </summary>
public static class WinThemePresets
{
    public static List<WinTheme> BuildAll() => new()
    {
        WindowsDefault(),
        PulseGlass(),
        Transparent(),
        DarkGlass(),
        Fluent(),
        Custom(),
    };

    /// <summary>Tudo no padrão: aplicar este tema equivale a restaurar o visual original.</summary>
    private static WinTheme WindowsDefault() => new()
    {
        Id = "builtin-windows-default",
        Name = "Windows Default",
        IsBuiltIn = true,
    };

    /// <summary>A identidade do Pulse: acrylic com o vermelho da marca, discreto.</summary>
    private static WinTheme PulseGlass()
    {
        var theme = new WinTheme { Id = "builtin-pulse-glass", Name = "Pulse Glass", IsBuiltIn = true };

        theme.Components[WinComponent.Taskbar] = new ComponentAppearance
        {
            Kind = AppearanceKind.Acrylic,
            TintColor = "#DC2626",
            TintIntensity = 0.18,
            Opacity = 0.85,
            BlurRadius = 24,
            EffectIntensity = 1.0,
            Luminosity = -0.35,
        };

        theme.Explorer[ExplorerRegion.Window] = new ComponentAppearance
        {
            Kind = AppearanceKind.Acrylic,
            TintColor = "#1A0B0F",
            TintIntensity = 0.32,
            Opacity = 0.9,
            BlurRadius = 22,
        };

        return theme;
    }

    /// <summary>Transparência máxima — a barra de tarefas some sobre o papel de parede.</summary>
    private static WinTheme Transparent()
    {
        var theme = new WinTheme { Id = "builtin-transparent", Name = "Transparent", IsBuiltIn = true };

        theme.Components[WinComponent.Taskbar] = new ComponentAppearance
        {
            Kind = AppearanceKind.Transparent,
        };

        return theme;
    }

    /// <summary>Vidro escuro, neutro — combina com qualquer papel de parede.</summary>
    private static WinTheme DarkGlass()
    {
        var theme = new WinTheme { Id = "builtin-dark-glass", Name = "Dark Glass", IsBuiltIn = true };

        theme.Components[WinComponent.Taskbar] = new ComponentAppearance
        {
            Kind = AppearanceKind.Glass,
            TintColor = "#000000",
            TintIntensity = 0.55,
            Opacity = 0.9,
            BlurRadius = 30,
        };

        theme.Explorer[ExplorerRegion.Window] = new ComponentAppearance
        {
            Kind = AppearanceKind.Glass,
            TintColor = "#0B0B0F",
            TintIntensity = 0.5,
            Opacity = 0.92,
            BlurRadius = 28,
        };

        return theme;
    }

    /// <summary>Blur suave no espírito do Fluent Design da Microsoft.</summary>
    private static WinTheme Fluent()
    {
        var theme = new WinTheme { Id = "builtin-fluent", Name = "Fluent", IsBuiltIn = true };

        theme.Components[WinComponent.Taskbar] = new ComponentAppearance
        {
            Kind = AppearanceKind.Blur,
            TintColor = "#202020",
            TintIntensity = 0.4,
            Opacity = 0.85,
            BlurRadius = 18,
        };

        theme.Explorer[ExplorerRegion.Window] = new ComponentAppearance
        {
            Kind = AppearanceKind.Mica,
            TintColor = "#202020",
            TintIntensity = 0.3,
            Opacity = 0.9,
        };

        return theme;
    }

    /// <summary>
    /// Ponto de partida editável: translúcido neutro, pensado para o usuário ajustar sem começar
    /// de uma tela em branco. Continua marcado como de fábrica (a interface oferece duplicar).
    /// </summary>
    private static WinTheme Custom()
    {
        var theme = new WinTheme { Id = "builtin-custom", Name = "Custom", IsBuiltIn = true };

        theme.Components[WinComponent.Taskbar] = new ComponentAppearance
        {
            Kind = AppearanceKind.Translucent,
            TintColor = "#1A1A24",
            Opacity = 0.7,
            Transparency = 0.2,
        };

        return theme;
    }
}
