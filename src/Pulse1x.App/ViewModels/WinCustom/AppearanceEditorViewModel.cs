using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.WinCustom;
using Pulse1x.App.Services.WinCustom;

namespace Pulse1x.App.ViewModels.WinCustom;

/// <summary>Um tipo de aparência no seletor, já sabendo se pode ser usado nesta máquina.</summary>
public partial class AppearanceOption : ObservableObject
{
    public AppearanceKind Kind { get; }

    [ObservableProperty] private bool isAvailable = true;
    [ObservableProperty] private string unavailableHint = "";

    public AppearanceOption(AppearanceKind kind) => Kind = kind;

    /// <summary>Nome traduzido do tipo de aparência.</summary>
    public string Label => Loc.S("WinCustom_Kind_" + Kind);

    /// <summary>Recalcula os textos quando o idioma muda.</summary>
    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// Editor de UM alvo (a barra de tarefas inteira, ou uma região específica do Explorer, do Menu
/// Iniciar ou das Configurações).
///
/// Cada alvo é independente: é esta classe, instanciada uma vez por região, que permite "fundo
/// principal com imagem, painel lateral com blur, barra superior transparente, abas em Glass" no
/// mesmo tema. Ela também sabe quais ajustes fazem sentido para o tipo escolhido, de modo que a
/// interface só mostre controles que realmente fazem algo.
/// </summary>
public partial class AppearanceEditorViewModel : ObservableObject
{
    private readonly ComponentAppearance _appearance;
    private readonly Func<AppearanceKind, Capability> _capabilityLookup;
    private readonly Action _onChanged;

    /// <summary>Evita salvar/reaplicar enquanto o editor está sendo montado.</summary>
    private bool _loading = true;

    /// <summary>Chave de tradução do nome do alvo ("Barra superior", "Painel lateral"...).</summary>
    public string TitleKey { get; }

    /// <summary>Identificador do alvo no motor ("Taskbar", "Explorer.Sidebar"...).</summary>
    public string TargetId { get; }

    public string Title => Loc.S(TitleKey);

    public AppearanceEditorViewModel(
        string targetId,
        string titleKey,
        ComponentAppearance appearance,
        Func<AppearanceKind, Capability> capabilityLookup,
        Action onChanged)
    {
        TargetId = targetId;
        TitleKey = titleKey;
        _appearance = appearance;
        _capabilityLookup = capabilityLookup;
        _onChanged = onChanged;

        Options = CapabilityMatrix.AllKinds.Select(k => new AppearanceOption(k)).ToList();

        LoadFrom(appearance);
        RefreshAvailability();
        _loading = false;
    }

    /// <summary>O objeto de dados editado — o tema guarda esta mesma instância.</summary>
    public ComponentAppearance Model => _appearance;

    public IReadOnlyList<AppearanceOption> Options { get; }

    // =====================================================================================
    //  Propriedades editáveis
    // =====================================================================================

    [ObservableProperty] private AppearanceKind kind;
    [ObservableProperty] private double opacity;
    [ObservableProperty] private double transparency;
    [ObservableProperty] private double blurRadius;
    [ObservableProperty] private double effectIntensity;
    [ObservableProperty] private string tintColor = "#1A1A24";
    [ObservableProperty] private double tintIntensity;
    [ObservableProperty] private double saturation;
    [ObservableProperty] private double luminosity;
    [ObservableProperty] private double darken;
    [ObservableProperty] private string gradientStart = "#1A1A24";
    [ObservableProperty] private string gradientEnd = "#0B0B0F";
    [ObservableProperty] private GradientDirection gradientDirection;
    [ObservableProperty] private string? imagePath;
    [ObservableProperty] private ImageFit imageFit;
    [ObservableProperty] private double imageOffsetX;
    [ObservableProperty] private double imageOffsetY;
    [ObservableProperty] private double imageScale;
    [ObservableProperty] private double imageOpacity;
    [ObservableProperty] private double cornerRadius;

    /// <summary>Se o tipo escolhido está disponível — controla o aviso na interface.</summary>
    [ObservableProperty] private bool currentKindAvailable = true;
    [ObservableProperty] private string currentKindHint = "";

    /// <summary>Pedido de escolha de imagem, atendido pelo code-behind (diálogo de arquivo).</summary>
    public event Func<string?>? PickImageRequested;

    private void LoadFrom(ComponentAppearance a)
    {
        kind = a.Kind;
        opacity = a.Opacity;
        transparency = a.Transparency;
        blurRadius = a.BlurRadius;
        effectIntensity = a.EffectIntensity;
        tintColor = a.TintColor;
        tintIntensity = a.TintIntensity;
        saturation = a.Saturation;
        luminosity = a.Luminosity;
        darken = a.Darken;
        gradientStart = a.GradientStart;
        gradientEnd = a.GradientEnd;
        gradientDirection = a.GradientDirection;
        imagePath = a.ImagePath;
        imageFit = a.ImageFit;
        imageOffsetX = a.ImageOffsetX;
        imageOffsetY = a.ImageOffsetY;
        imageScale = a.ImageScale;
        imageOpacity = a.ImageOpacity;
        cornerRadius = a.CornerRadius;
    }

    /// <summary>Recarrega a partir do modelo — usado quando a sincronização altera este alvo.</summary>
    public void Reload()
    {
        _loading = true;
        LoadFrom(_appearance);

        OnPropertyChanged(string.Empty);   // reavalia tudo de uma vez
        RefreshAvailability();
        RaiseVisibilityFlags();
        RaisePreview();
        _loading = false;
    }

    // =====================================================================================
    //  Propagação para o modelo
    // =====================================================================================

    partial void OnKindChanged(AppearanceKind value)
    {
        _appearance.Kind = value;
        RefreshAvailability();
        RaiseVisibilityFlags();
        Commit();
    }

    partial void OnOpacityChanged(double value) { _appearance.Opacity = value; Commit(); }
    partial void OnTransparencyChanged(double value) { _appearance.Transparency = value; Commit(); }
    partial void OnBlurRadiusChanged(double value) { _appearance.BlurRadius = value; Commit(); }
    partial void OnEffectIntensityChanged(double value) { _appearance.EffectIntensity = value; Commit(); }
    partial void OnTintColorChanged(string value) { _appearance.TintColor = value; Commit(); }
    partial void OnTintIntensityChanged(double value) { _appearance.TintIntensity = value; Commit(); }
    partial void OnSaturationChanged(double value) { _appearance.Saturation = value; Commit(); }
    partial void OnLuminosityChanged(double value) { _appearance.Luminosity = value; Commit(); }
    partial void OnDarkenChanged(double value) { _appearance.Darken = value; Commit(); }
    partial void OnGradientStartChanged(string value) { _appearance.GradientStart = value; Commit(); }
    partial void OnGradientEndChanged(string value) { _appearance.GradientEnd = value; Commit(); }
    partial void OnGradientDirectionChanged(GradientDirection value) { _appearance.GradientDirection = value; Commit(); }
    partial void OnImagePathChanged(string? value) { _appearance.ImagePath = value; Commit(); }
    partial void OnImageFitChanged(ImageFit value) { _appearance.ImageFit = value; Commit(); }
    partial void OnImageOffsetXChanged(double value) { _appearance.ImageOffsetX = value; Commit(); }
    partial void OnImageOffsetYChanged(double value) { _appearance.ImageOffsetY = value; Commit(); }
    partial void OnImageScaleChanged(double value) { _appearance.ImageScale = value; Commit(); }
    partial void OnImageOpacityChanged(double value) { _appearance.ImageOpacity = value; Commit(); }
    partial void OnCornerRadiusChanged(double value) { _appearance.CornerRadius = value; Commit(); }

    private void Commit()
    {
        if (_loading) return;
        RaisePreview();
        _onChanged();
    }

    // =====================================================================================
    //  Disponibilidade
    // =====================================================================================

    /// <summary>
    /// Reconsulta a matriz de capacidades. Chamado quando o motor de injeção é ligado/desligado,
    /// de modo que os alvos beta se habilitem sem reabrir a tela.
    /// </summary>
    public void RefreshAvailability()
    {
        foreach (var option in Options)
        {
            var capability = _capabilityLookup(option.Kind);
            option.IsAvailable = capability.Available;
            option.UnavailableHint = HintFor(capability.Reason);
        }

        var current = _capabilityLookup(Kind);
        CurrentKindAvailable = current.Available;
        CurrentKindHint = HintFor(current.Reason);
    }

    private static string HintFor(UnavailableReason reason) => reason switch
    {
        UnavailableReason.NeedsInjection => Loc.S("WinCustom_NeedsInjection"),
        UnavailableReason.UnsupportedBuild => Loc.S("WinCustom_UnsupportedBuild"),
        UnavailableReason.NotApplicable => Loc.S("WinCustom_NotApplicable"),
        _ => "",
    };

    // =====================================================================================
    //  Quais controles mostrar
    // =====================================================================================

    public bool ShowOpacity => CapabilityMatrix.SupportsOpacity(Kind);
    public bool ShowBlur => CapabilityMatrix.SupportsBlurRadius(Kind);
    public bool ShowTint => CapabilityMatrix.SupportsTint(Kind);
    public bool ShowSolidColor => CapabilityMatrix.SupportsSolidColor(Kind);
    public bool ShowGradient => CapabilityMatrix.SupportsGradient(Kind);
    public bool ShowImage => CapabilityMatrix.SupportsImage(Kind);
    public bool ShowCornerRadius => CapabilityMatrix.SupportsCornerRadius(Kind);
    public bool ShowTransparency => Kind is AppearanceKind.Translucent or AppearanceKind.SolidColor;
    /// <summary>Cor, saturação e luminosidade valem sempre que há uma cor em jogo.</summary>
    public bool ShowColorAdjust => Kind is not AppearanceKind.WindowsDefault and not AppearanceKind.Transparent;
    public bool IsDefault => Kind == AppearanceKind.WindowsDefault;

    private void RaiseVisibilityFlags()
    {
        OnPropertyChanged(nameof(ShowOpacity));
        OnPropertyChanged(nameof(ShowBlur));
        OnPropertyChanged(nameof(ShowTint));
        OnPropertyChanged(nameof(ShowSolidColor));
        OnPropertyChanged(nameof(ShowGradient));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(ShowCornerRadius));
        OnPropertyChanged(nameof(ShowTransparency));
        OnPropertyChanged(nameof(ShowColorAdjust));
        OnPropertyChanged(nameof(IsDefault));
    }

    // =====================================================================================
    //  Preview
    // =====================================================================================

    /// <summary>Pincel que a tela desenha para mostrar como vai ficar.</summary>
    public Brush PreviewBrush => AppearancePreview.BuildBrush(_appearance);
    public double PreviewBlur => AppearancePreview.BlurRadiusFor(_appearance);
    public double PreviewDarken => AppearancePreview.DarkenFor(_appearance);
    public CornerRadius PreviewCorner => new(Math.Max(0, _appearance.CornerRadius));

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(PreviewBlur));
        OnPropertyChanged(nameof(PreviewDarken));
        OnPropertyChanged(nameof(PreviewCorner));
    }

    // =====================================================================================
    //  Comandos
    // =====================================================================================

    [RelayCommand]
    private void PickImage()
    {
        string? path = PickImageRequested?.Invoke();
        if (!string.IsNullOrWhiteSpace(path)) ImagePath = path;
    }

    [RelayCommand]
    private void ClearImage() => ImagePath = null;

    /// <summary>Devolve este alvo ao padrão do Windows.</summary>
    [RelayCommand]
    private void ResetToDefault() => Kind = AppearanceKind.WindowsDefault;

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var option in Options) option.RefreshLabel();
        RefreshAvailability();
    }
}
