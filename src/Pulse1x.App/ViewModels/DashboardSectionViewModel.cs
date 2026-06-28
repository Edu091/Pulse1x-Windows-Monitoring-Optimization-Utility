using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Localization;
using Pulse1x.App.Models;

namespace Pulse1x.App.ViewModels;

public partial class DashboardSectionViewModel : ObservableObject
{
    private readonly string _titleKey;

    public DashboardSectionKind Kind { get; }

    // Título lido do dicionário de localização e reemitido quando o idioma muda (troca ao vivo).
    public string Title => Loc.Instance[_titleKey];

    [ObservableProperty] private bool isVisible = true;

    public DashboardSectionViewModel(DashboardSectionKind kind, string titleKey, bool isVisible)
    {
        Kind = kind;
        _titleKey = titleKey;
        this.isVisible = isVisible;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Title));
    }
}
