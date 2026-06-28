using System.ComponentModel;

namespace Pulse1x.App.Localization;

/// <summary>
/// Idiomas suportados pela interface do Pulse1x.
/// </summary>
public enum AppLanguage { Portuguese, English }

/// <summary>
/// Motor de localização da interface. Funciona como fonte de binding para o XAML:
/// <c>{Binding [Chave], Source={x:Static loc:Loc.Instance}}</c>. Ao trocar o idioma,
/// dispara <see cref="INotifyPropertyChanged"/> para o indexador ("Item[]"), o que faz o
/// WPF reavaliar todos os textos na hora — sem reiniciar o app. ViewModels e code-behind
/// que montam textos dinamicamente podem ouvir <see cref="LanguageChanged"/> e reler via
/// <see cref="T"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private AppLanguage _language = AppLanguage.Portuguese;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Disparado depois que o idioma muda, para quem monta textos em código.</summary>
    public event Action? LanguageChanged;

    public AppLanguage Language => _language;

    /// <summary>Código curto do idioma atual ("pt"/"en"), usado para persistência.</summary>
    public string LanguageCode => _language == AppLanguage.English ? "en" : "pt";

    /// <summary>Texto localizado para a chave; cai no Português e, em último caso, na própria chave.</summary>
    public string this[string key]
    {
        get
        {
            var dict = _language == AppLanguage.English ? LocalizationStrings.En : LocalizationStrings.Pt;
            if (dict.TryGetValue(key, out var value)) return value;
            return LocalizationStrings.Pt.TryGetValue(key, out var pt) ? pt : key;
        }
    }

    /// <summary>Acesso ao texto localizado a partir de código (ViewModels, diálogos).</summary>
    public string T(string key) => this[key];

    /// <summary>Versão estática conveniente para chamadas curtas em código.</summary>
    public static string S(string key) => Instance[key];

    /// <summary>Texto localizado com placeholders ({0}, {1}...), formatado com <paramref name="args"/>.</summary>
    public static string F(string key, params object?[] args) => string.Format(Instance[key], args);

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language) return;
        _language = language;
        // "Item[]" é o nome especial que faz o WPF reavaliar todos os bindings de indexador.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
        LanguageChanged?.Invoke();
    }

    public static AppLanguage FromCode(string? code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Portuguese;
}
