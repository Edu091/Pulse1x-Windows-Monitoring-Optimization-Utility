using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>
/// Um cartão da biblioteca. Envolve o <see cref="GameEntry"/> guardado em disco e cuida da parte
/// visual: carrega a capa sob demanda (um cartão que nunca apareceu na tela não gasta memória) e
/// expõe os textos já formatados e traduzidos.
/// </summary>
public partial class GameCardViewModel : ObservableObject
{
    private readonly GameArtService _art;

    public GameEntry Entry { get; }

    [ObservableProperty] private BitmapImage? cover;
    [ObservableProperty] private bool isSelected;

    private bool _coverRequested;

    public GameCardViewModel(GameEntry entry, GameArtService art)
    {
        Entry = entry;
        _art = art;
    }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public bool IsFavorite => Entry.IsFavorite;
    public string Category => Entry.Category;
    public string PlaytimeText => Entry.PlaytimeText;

    public string PlatformText => Entry.Launcher switch
    {
        LauncherKind.Steam => "Steam",
        LauncherKind.Epic => "Epic Games",
        LauncherKind.Gog => "GOG",
        LauncherKind.Emulator => string.IsNullOrEmpty(Entry.Category) ? Loc.S("GH_PlatformEmulator") : Entry.Category,
        LauncherKind.Shortcut => Loc.S("GH_PlatformShortcut"),
        LauncherKind.Application => Loc.S("GH_PlatformApp"),
        _ => Loc.S("GH_PlatformManual"),
    };

    public string LastPlayedText => Entry.LastPlayed is DateTime played
        ? played.Date == DateTime.Today
            ? Loc.S("GH_Today")
            : played.Date == DateTime.Today.AddDays(-1)
                ? Loc.S("GH_Yesterday")
                : played.ToString("dd/MM/yyyy")
        : Loc.S("GH_NeverPlayed");

    /// <summary>
    /// Pede a capa. A decodificação é limitada à largura de exibição, o que reduz bastante a
    /// memória numa biblioteca grande — uma capa de 600x900 guardada em tamanho cheio custa ~2 MB,
    /// e na tela ela nunca passa de 200 px de largura.
    /// </summary>
    public void RequestCover(int decodeWidth = 220)
    {
        if (_coverRequested) return;
        _coverRequested = true;
        Cover = GameArtService.LoadBitmap(Entry.CoverPath ?? Entry.IconPath, decodeWidth);
    }

    /// <summary>Libera o bitmap (usado pelo Modo Gaming ao liberar memória).</summary>
    public void ReleaseCover()
    {
        Cover = null;
        _coverRequested = false;
    }

    /// <summary>Avisa a interface de que os dados do item mudaram (favorito, perfil, tempo jogado).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(PlatformText));
        OnPropertyChanged(nameof(LastPlayedText));
    }
}
