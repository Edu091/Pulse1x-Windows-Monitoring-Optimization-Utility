using System.Windows;
using System.Windows.Controls;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Controls;

/// <summary>
/// Selo de segurança (Seguro / Moderado / Avançado) e painel recolhível "Vantagens e desvantagens"
/// de uma função do app. Basta informar <see cref="InfoKey"/>: o nível vem de
/// <see cref="FunctionSafety"/> e os textos de "&lt;InfoKey&gt;_Pros" / "&lt;InfoKey&gt;_Cons".
/// Sem esses textos (função ainda não descrita), o controle fica oculto.
/// </summary>
public partial class FunctionInfo : UserControl
{
    public static readonly DependencyProperty InfoKeyProperty = DependencyProperty.Register(
        nameof(InfoKey), typeof(string), typeof(FunctionInfo), new PropertyMetadata(null, (d, _) => ((FunctionInfo)d).Rebuild()));

    public string? InfoKey
    {
        get => (string?)GetValue(InfoKeyProperty);
        set => SetValue(InfoKeyProperty, value);
    }

    private ToolInfoViewModel? _info;

    public FunctionInfo()
    {
        InitializeComponent();
        Loaded += (_, _) => Loc.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => _info?.RefreshTexts();

    private void Rebuild()
    {
        string? key = InfoKey;
        bool described = !string.IsNullOrEmpty(key) && LocalizationStrings.Pt.ContainsKey(key + "_Pros");
        if (!described)
        {
            _info = null;
            RootPanel.DataContext = null;
            Visibility = Visibility.Collapsed;
            return;
        }
        _info = new ToolInfoViewModel(key!, FunctionSafety.Of(key!));
        RootPanel.DataContext = _info;
        Visibility = Visibility.Visible;
    }
}
