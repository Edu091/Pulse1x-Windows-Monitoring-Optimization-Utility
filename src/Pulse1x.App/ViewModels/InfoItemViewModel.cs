using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Localization;

namespace Pulse1x.App.ViewModels;

public partial class InfoItemViewModel : ObservableObject
{
    private readonly string _labelKey;
    public string Label => Loc.Instance[_labelKey];

    [ObservableProperty] private string value;

    public InfoItemViewModel(string labelKey, string value)
    {
        _labelKey = labelKey;
        this.value = value;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Label));
    }
}
