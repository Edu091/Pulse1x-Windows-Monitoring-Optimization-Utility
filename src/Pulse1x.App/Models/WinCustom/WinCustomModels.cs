using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.WinCustom;

/// <summary>
/// Componente do Windows que a Personalização do Windows sabe modificar. Cada um tem
/// configurações independentes (<see cref="ComponentAppearance"/>) dentro de um tema.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WinComponent
{
    Taskbar,
    StartMenu,
    Explorer,
    Settings,
}

/// <summary>
/// Regiões do Explorador de Arquivos que podem receber estilos SEPARADOS (fundo principal com
/// imagem, painel lateral com blur, barra superior transparente, abas em Glass...).
///
/// <see cref="Window"/> é a região "tudo" — a única alcançável sem injeção, porque corresponde à
/// janela (HWND) inteira. As demais são elementos XAML internos do explorer.exe e só existem
/// quando o motor de injeção (beta) está ativo; fora dele aparecem como indisponíveis.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExplorerRegion
{
    /// <summary>A janela inteira — funciona sem injeção.</summary>
    Window,
    /// <summary>Fundo principal, onde aparecem arquivos e pastas.</summary>
    Content,
    /// <summary>Painel lateral (árvore de navegação).</summary>
    Sidebar,
    /// <summary>Barra superior.</summary>
    TopBar,
    /// <summary>Barra de endereço.</summary>
    AddressBar,
    /// <summary>Barra de comandos.</summary>
    CommandBar,
    /// <summary>Caixa de pesquisa.</summary>
    Search,
    /// <summary>Abas do Explorer.</summary>
    Tabs,
    /// <summary>Painel de detalhes.</summary>
    DetailsPane,
}

/// <summary>Regiões do Menu Iniciar que podem ser estilizadas de forma independente.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StartRegion
{
    /// <summary>O menu inteiro.</summary>
    Window,
    /// <summary>Área dos aplicativos fixados.</summary>
    Pinned,
    /// <summary>Área de recomendados.</summary>
    Recommended,
    /// <summary>Barra de pesquisa do menu.</summary>
    Search,
    /// <summary>Rodapé (usuário / energia).</summary>
    Footer,
}

/// <summary>Regiões das Configurações do Windows que podem ser estilizadas.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingsRegion
{
    /// <summary>A janela inteira.</summary>
    Window,
    /// <summary>Fundo principal (painel de conteúdo).</summary>
    Content,
    /// <summary>Barra lateral de navegação.</summary>
    Sidebar,
    /// <summary>Cartões de configuração.</summary>
    Cards,
    /// <summary>Contêineres/agrupamentos.</summary>
    Containers,
    /// <summary>Áreas superiores (cabeçalho).</summary>
    Header,
}

/// <summary>
/// Tipo de aparência aplicada a um componente ou região.
///
/// Os quatro efeitos de composição (<see cref="Transparent"/>, <see cref="Translucent"/>,
/// <see cref="Blur"/>, <see cref="Acrylic"/>) e <see cref="SolidColor"/> são alcançáveis na
/// TASKBAR sem injeção, via SetWindowCompositionAttribute no HWND. <see cref="Glass"/>,
/// <see cref="Mica"/>, <see cref="Gradient"/> e <see cref="Image"/> dependem do motor de injeção
/// para a maioria dos alvos — o <c>CapabilityMatrix</c> é quem decide, por alvo e por build do
/// Windows, o que está realmente disponível.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppearanceKind
{
    /// <summary>Padrão do Windows — o Pulse não desenha nada e não altera o componente.</summary>
    WindowsDefault,
    /// <summary>Fundo totalmente transparente.</summary>
    Transparent,
    /// <summary>Fundo translúcido (transparência parcial, sem desfoque).</summary>
    Translucent,
    Blur,
    Glass,
    Acrylic,
    Mica,
    SolidColor,
    Gradient,
    /// <summary>Imagem personalizada como plano de fundo.</summary>
    Image,
}

/// <summary>Como a imagem de fundo é ajustada dentro da região.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageFit
{
    /// <summary>Preenche a região, cortando o excesso (mantém proporção).</summary>
    Fill,
    /// <summary>Cabe inteira dentro da região (mantém proporção).</summary>
    Fit,
    /// <summary>Estica para preencher, sem manter proporção.</summary>
    Stretch,
    /// <summary>Tamanho original, centralizada.</summary>
    Center,
    /// <summary>Repete lado a lado.</summary>
    Tile,
    /// <summary>Recorte manual, controlado por <see cref="ComponentAppearance.ImageOffsetX"/>.</summary>
    Crop,
}

/// <summary>Direção do gradiente.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GradientDirection
{
    Vertical,
    Horizontal,
    DiagonalDown,
    DiagonalUp,
}

/// <summary>
/// Todos os parâmetros visuais de UM alvo (um componente inteiro ou uma região dele).
///
/// É deliberadamente um objeto só, com todos os ajustes: qual deles vale depende do
/// <see cref="Kind"/> escolhido, e a interface mostra apenas os que fazem sentido. Isso mantém o
/// tema simples de serializar e permite trocar de tipo de aparência sem perder o que já estava
/// ajustado.
/// </summary>
public class ComponentAppearance
{
    public AppearanceKind Kind { get; set; } = AppearanceKind.WindowsDefault;

    // ---- Intensidades gerais (0 a 1, salvo indicação) ----
    /// <summary>Opacidade geral do efeito/fundo.</summary>
    public double Opacity { get; set; } = 0.85;
    /// <summary>Transparência adicional — 0 = nada, 1 = totalmente transparente.</summary>
    public double Transparency { get; set; }
    /// <summary>Raio do desfoque, em pixels.</summary>
    public double BlurRadius { get; set; } = 18;
    /// <summary>Multiplicador global da intensidade dos efeitos.</summary>
    public double EffectIntensity { get; set; } = 1.0;

    // ---- Cor / tint ----
    /// <summary>Cor sólida (modo <see cref="AppearanceKind.SolidColor"/>) e cor do tint nos efeitos.</summary>
    public string TintColor { get; set; } = "#1A1A24";
    /// <summary>Quanto do tint é misturado ao efeito (0 a 1).</summary>
    public double TintIntensity { get; set; } = 0.35;
    /// <summary>Saturação: 0 = cinza, 1 = original, 2 = mais viva.</summary>
    public double Saturation { get; set; } = 1.0;
    /// <summary>Luminosidade: 0 = original, negativo escurece, positivo clareia (-1 a 1).</summary>
    public double Luminosity { get; set; }
    /// <summary>Escurecimento sobreposto (0 a 1) — garante leitura do texto.</summary>
    public double Darken { get; set; }

    // ---- Gradiente ----
    public string GradientStart { get; set; } = "#1A1A24";
    public string GradientEnd { get; set; } = "#0B0B0F";
    public GradientDirection GradientDirection { get; set; } = GradientDirection.Vertical;

    // ---- Imagem ----
    public string? ImagePath { get; set; }
    public ImageFit ImageFit { get; set; } = ImageFit.Fill;
    /// <summary>Deslocamento horizontal da imagem (-1 a 1), para ajuste fino / crop.</summary>
    public double ImageOffsetX { get; set; }
    /// <summary>Deslocamento vertical da imagem (-1 a 1).</summary>
    public double ImageOffsetY { get; set; }
    /// <summary>Escala da imagem (1 = natural).</summary>
    public double ImageScale { get; set; } = 1.0;
    /// <summary>Opacidade própria da imagem.</summary>
    public double ImageOpacity { get; set; } = 1.0;

    // ---- Forma ----
    /// <summary>Raio das bordas, em pixels.</summary>
    public double CornerRadius { get; set; }

    /// <summary>Cópia profunda — usada ao duplicar temas e ao sincronizar aparências.</summary>
    public ComponentAppearance Clone() => (ComponentAppearance)MemberwiseClone();

    /// <summary>
    /// Copia apenas o "estilo" (tipo de aparência e parâmetros visuais) preservando o que é
    /// específico do alvo. Usado pela sincronização entre componentes.
    /// </summary>
    public void CopyStyleFrom(ComponentAppearance other)
    {
        Kind = other.Kind;
        Opacity = other.Opacity;
        Transparency = other.Transparency;
        BlurRadius = other.BlurRadius;
        EffectIntensity = other.EffectIntensity;
        TintColor = other.TintColor;
        TintIntensity = other.TintIntensity;
        Saturation = other.Saturation;
        Luminosity = other.Luminosity;
        Darken = other.Darken;
        GradientStart = other.GradientStart;
        GradientEnd = other.GradientEnd;
        GradientDirection = other.GradientDirection;
        ImagePath = other.ImagePath;
        ImageFit = other.ImageFit;
        ImageOffsetX = other.ImageOffsetX;
        ImageOffsetY = other.ImageOffsetY;
        ImageScale = other.ImageScale;
        ImageOpacity = other.ImageOpacity;
        CornerRadius = other.CornerRadius;
    }
}

/// <summary>
/// Um tema completo de personalização do Windows: guarda a aparência de todos os componentes e
/// de todas as regiões, mais as imagens usadas. É a unidade que se salva, renomeia, duplica,
/// exporta e importa.
/// </summary>
public class WinTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Presets de fábrica não podem ser renomeados nem excluídos — só duplicados.</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    /// <summary>Aparência dos componentes que têm um estilo único (Taskbar).</summary>
    public Dictionary<WinComponent, ComponentAppearance> Components { get; set; } = new();

    /// <summary>Aparência por região do Explorer — permite misturar estilos dentro dele.</summary>
    public Dictionary<ExplorerRegion, ComponentAppearance> Explorer { get; set; } = new();

    /// <summary>Aparência por região do Menu Iniciar.</summary>
    public Dictionary<StartRegion, ComponentAppearance> Start { get; set; } = new();

    /// <summary>Aparência por região das Configurações do Windows.</summary>
    public Dictionary<SettingsRegion, ComponentAppearance> Settings { get; set; } = new();

    /// <summary>Busca (criando sob demanda) a aparência da taskbar.</summary>
    public ComponentAppearance TaskbarAppearance => Get(Components, WinComponent.Taskbar);

    public static ComponentAppearance Get<TKey>(Dictionary<TKey, ComponentAppearance> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var appearance))
        {
            appearance = new ComponentAppearance();
            map[key] = appearance;
        }
        return appearance;
    }

    /// <summary>Cópia independente do tema, com novo Id — base de "Duplicar".</summary>
    public WinTheme Duplicate(string newName)
    {
        var copy = new WinTheme
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = newName,
            IsBuiltIn = false,
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
        };
        foreach (var (k, v) in Components) copy.Components[k] = v.Clone();
        foreach (var (k, v) in Explorer) copy.Explorer[k] = v.Clone();
        foreach (var (k, v) in Start) copy.Start[k] = v.Clone();
        foreach (var (k, v) in Settings) copy.Settings[k] = v.Clone();
        return copy;
    }

    /// <summary>Todas as imagens referenciadas pelo tema — usado ao exportar/importar.</summary>
    public IEnumerable<string> ReferencedImages()
    {
        foreach (var a in AllAppearances())
            if (!string.IsNullOrWhiteSpace(a.ImagePath))
                yield return a.ImagePath!;
    }

    public IEnumerable<ComponentAppearance> AllAppearances()
    {
        foreach (var a in Components.Values) yield return a;
        foreach (var a in Explorer.Values) yield return a;
        foreach (var a in Start.Values) yield return a;
        foreach (var a in Settings.Values) yield return a;
    }
}

/// <summary>
/// Preferências do subsistema de Personalização do Windows — separadas dos temas, porque
/// descrevem COMO a personalização roda, não COMO ela se parece.
/// </summary>
public class WinCustomSettings
{
    /// <summary>Id do tema atualmente aplicado (vazio = nenhum).</summary>
    public string ActiveThemeId { get; set; } = "";

    // Nada abaixo começa ligado. A personalização do Windows só entra em ação depois de um ato
    // explícito do usuário (escolher um tema e clicar em Aplicar) — abrir a seção para dar uma
    // olhada não pode alterar o Windows nem registrar o app na inicialização.

    /// <summary>Personalização ligada. Desligar para temporariamente (Parar) sem perder o tema.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Inicia o host de personalização junto com o Windows. Só passa a valer quando há uma
    /// personalização ativa; ligar sozinho não faria nada além de abrir um processo ocioso.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Reaplica o último tema automaticamente ao iniciar.</summary>
    public bool RestoreLastTheme { get; set; } = true;

    /// <summary>Reaplica a personalização quando o Explorer reinicia.</summary>
    public bool ReapplyOnExplorerRestart { get; set; } = true;

    /// <summary>
    /// Liga o motor de injeção (BETA) — necessário para Menu Iniciar, Configurações e para as
    /// regiões internas do Explorer. Desligado por padrão: é a parte de maior risco.
    /// </summary>
    public bool EnableInjection { get; set; }

    /// <summary>Sincronizar o mesmo estilo entre os componentes.</summary>
    public bool SyncAppearance { get; set; }

    /// <summary>Componente que serve de referência quando a sincronização está ligada.</summary>
    public WinComponent SyncSource { get; set; } = WinComponent.Taskbar;
}
