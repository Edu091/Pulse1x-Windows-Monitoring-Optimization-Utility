using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>Por que uma aparência não está disponível — vira a explicação mostrada na tela.</summary>
public enum UnavailableReason
{
    /// <summary>Disponível.</summary>
    None,
    /// <summary>Exige o motor de injeção (beta), que está desligado.</summary>
    NeedsInjection,
    /// <summary>A build do Windows atual não oferece o efeito.</summary>
    UnsupportedBuild,
    /// <summary>O efeito não existe para este alvo, em nenhuma configuração.</summary>
    NotApplicable,
}

/// <summary>Resultado da consulta de capacidade: se dá para aplicar e, se não, por quê.</summary>
public readonly record struct Capability(bool Available, UnavailableReason Reason)
{
    public static readonly Capability Yes = new(true, UnavailableReason.None);
    public static Capability No(UnavailableReason reason) => new(false, reason);
}

/// <summary>
/// Decide, para cada alvo e cada tipo de aparência, o que REALMENTE pode ser aplicado nesta
/// máquina — considerando a build do Windows e se o motor de injeção está ligado.
///
/// Existe para cumprir dois requisitos ao mesmo tempo: "toda opção exibida como funcional precisa
/// ter backend real" e "se algo não for compatível, informe em vez de tentar à força". A interface
/// consulta esta matriz antes de habilitar qualquer controle, e o motor consulta de novo antes de
/// aplicar — nada é tentado às cegas.
///
/// O que é alcançável SEM injeção:
///   • Taskbar: transparente, translúcido, blur, acrylic, cor sólida — via
///     SetWindowCompositionAttribute no HWND Shell_TrayWnd (a mesma técnica do TranslucentTB).
///   • Explorer (janela inteira): os mesmos efeitos de composição, por HWND.
/// O que exige injeção (beta): gradiente, imagem, mica e raio de borda em qualquer alvo; e
/// TUDO no Menu Iniciar, nas Configurações e nas regiões internas do Explorer, porque são
/// elementos XAML dentro de outros processos.
/// </summary>
public class CapabilityMatrix
{
    /// <summary>Build mínima do Windows 11 (21H2). Abaixo disso o subsistema fica inteiro fora.</summary>
    private const int MinimumWindows11Build = 22000;

    /// <summary>Mica em janelas de terceiros passou a existir de forma estável no 22H2.</summary>
    private const int MicaBuild = 22621;

    private readonly int _build;

    /// <summary>Se o motor de injeção (beta) está ativo nesta sessão.</summary>
    public bool InjectionActive { get; set; }

    public CapabilityMatrix(int? buildOverride = null)
    {
        _build = buildOverride ?? Environment.OSVersion.Version.Build;
    }

    public int Build => _build;

    /// <summary>Windows 11 ou mais novo — pré-requisito de todo o subsistema.</summary>
    public bool IsWindows11 => _build >= MinimumWindows11Build;

    /// <summary>
    /// Efeitos que o gerenciador de janelas aplica a um HWND de fora do processo. É o conjunto
    /// que funciona sem injeção.
    /// </summary>
    /// <remarks>
    /// Glass entra aqui porque é realizável de verdade sem injeção: ele usa o mesmo backdrop
    /// acrílico do sistema, apenas com um tint mais leve e mais claro (ver
    /// <c>WindowComposition.Translate</c>), que é o que lhe dá a aparência de vidro em vez de
    /// material. Só gradiente e imagem exigem uma superfície própria.
    /// </remarks>
    private static bool IsCompositionEffect(AppearanceKind kind) => kind is
        AppearanceKind.Transparent or
        AppearanceKind.Translucent or
        AppearanceKind.Blur or
        AppearanceKind.Acrylic or
        AppearanceKind.Glass or
        AppearanceKind.SolidColor;

    /// <summary>
    /// Efeitos que exigem desenhar DENTRO do processo alvo (pintar uma superfície própria atrás
    /// do XAML), portanto só com injeção.
    /// </summary>
    private static bool NeedsOwnSurface(AppearanceKind kind) => kind is
        AppearanceKind.Gradient or
        AppearanceKind.Image;

    /// <summary>Capacidade da taskbar (componente de melhor suporte nativo).</summary>
    public Capability ForTaskbar(AppearanceKind kind)
    {
        if (kind == AppearanceKind.WindowsDefault) return Capability.Yes;
        if (!IsWindows11) return Capability.No(UnavailableReason.UnsupportedBuild);

        if (IsCompositionEffect(kind)) return Capability.Yes;

        // Mica não se aplica à barra de tarefas: o Shell não a trata como superfície Mica.
        if (kind == AppearanceKind.Mica) return Capability.No(UnavailableReason.NotApplicable);

        if (NeedsOwnSurface(kind))
            return InjectionActive ? Capability.Yes : Capability.No(UnavailableReason.NeedsInjection);

        return Capability.No(UnavailableReason.NotApplicable);
    }

    /// <summary>Capacidade de uma região do Explorer.</summary>
    public Capability ForExplorer(ExplorerRegion region, AppearanceKind kind)
    {
        if (kind == AppearanceKind.WindowsDefault) return Capability.Yes;
        if (!IsWindows11) return Capability.No(UnavailableReason.UnsupportedBuild);

        // A janela inteira é um HWND: efeitos de composição funcionam sem injeção.
        if (region == ExplorerRegion.Window)
        {
            if (IsCompositionEffect(kind)) return Capability.Yes;
            if (kind == AppearanceKind.Mica)
                return _build >= MicaBuild ? Capability.Yes : Capability.No(UnavailableReason.UnsupportedBuild);
            if (NeedsOwnSurface(kind))
                return InjectionActive ? Capability.Yes : Capability.No(UnavailableReason.NeedsInjection);
            return Capability.No(UnavailableReason.NotApplicable);
        }

        // Regiões internas são elementos XAML dentro do explorer.exe — sempre injeção.
        return InjectionActive ? Capability.Yes : Capability.No(UnavailableReason.NeedsInjection);
    }

    /// <summary>
    /// Capacidade de uma região do Menu Iniciar. O menu é um host XAML num processo protegido
    /// (StartMenuExperienceHost.exe) que ignora atributos de composição vindos de fora, então
    /// NADA aqui funciona sem injeção.
    /// </summary>
    public Capability ForStart(StartRegion region, AppearanceKind kind)
    {
        if (kind == AppearanceKind.WindowsDefault) return Capability.Yes;
        if (!IsWindows11) return Capability.No(UnavailableReason.UnsupportedBuild);
        return InjectionActive ? Capability.Yes : Capability.No(UnavailableReason.NeedsInjection);
    }

    /// <summary>
    /// Capacidade de uma região das Configurações. Mesmo caso do Menu Iniciar: o
    /// SystemSettings.exe é um app empacotado e só a injeção alcança suas superfícies.
    /// </summary>
    public Capability ForSettings(SettingsRegion region, AppearanceKind kind)
    {
        if (kind == AppearanceKind.WindowsDefault) return Capability.Yes;
        if (!IsWindows11) return Capability.No(UnavailableReason.UnsupportedBuild);
        return InjectionActive ? Capability.Yes : Capability.No(UnavailableReason.NeedsInjection);
    }

    /// <summary>
    /// Ajustes finos que fazem sentido para um dado tipo de aparência — a interface usa isto para
    /// mostrar só os controles úteis em vez de uma lista de sliders inertes.
    /// </summary>
    public static bool SupportsBlurRadius(AppearanceKind kind) =>
        kind is AppearanceKind.Blur or AppearanceKind.Acrylic or AppearanceKind.Glass or AppearanceKind.Image;

    public static bool SupportsTint(AppearanceKind kind) =>
        kind is AppearanceKind.Blur or AppearanceKind.Acrylic or AppearanceKind.Glass
             or AppearanceKind.Translucent or AppearanceKind.Mica;

    public static bool SupportsImage(AppearanceKind kind) => kind == AppearanceKind.Image;

    public static bool SupportsGradient(AppearanceKind kind) => kind == AppearanceKind.Gradient;

    public static bool SupportsSolidColor(AppearanceKind kind) => kind == AppearanceKind.SolidColor;

    /// <summary>Opacidade só não faz sentido no modo padrão e no totalmente transparente.</summary>
    public static bool SupportsOpacity(AppearanceKind kind) =>
        kind is not (AppearanceKind.WindowsDefault or AppearanceKind.Transparent);

    /// <summary>
    /// Raio de borda depende de redesenhar o recorte da superfície — só com injeção, e apenas
    /// onde há uma superfície própria.
    /// </summary>
    public static bool SupportsCornerRadius(AppearanceKind kind) =>
        kind is not AppearanceKind.WindowsDefault;

    /// <summary>Todos os tipos de aparência, na ordem em que aparecem na interface.</summary>
    public static readonly AppearanceKind[] AllKinds =
    {
        AppearanceKind.WindowsDefault,
        AppearanceKind.Transparent,
        AppearanceKind.Translucent,
        AppearanceKind.Blur,
        AppearanceKind.Glass,
        AppearanceKind.Acrylic,
        AppearanceKind.Mica,
        AppearanceKind.SolidColor,
        AppearanceKind.Gradient,
        AppearanceKind.Image,
    };
}
