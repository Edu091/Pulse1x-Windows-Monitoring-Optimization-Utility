using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.ViewModels.GameHub;

/// <summary>Uma capa sugerida, já com a miniatura carregada para escolha visual.</summary>
public partial class CoverCandidateViewModel : ObservableObject
{
    public ArtCandidate Candidate { get; }
    [ObservableProperty] private BitmapImage? preview;
    [ObservableProperty] private bool isLoading = true;

    public string Title => Candidate.Title;

    public CoverCandidateViewModel(ArtCandidate candidate) => Candidate = candidate;
}

/// <summary>
/// Escolha da capa de um item. Mostra o que a busca online encontrou pelo nome, deixa refinar o
/// termo (útil quando o nome na biblioteca não bate com o nome comercial) e também aceita uma
/// imagem do computador.
///
/// A busca automática já escolhe sozinha; esta tela existe para os casos em que ela erra — nomes
/// parecidos, coletâneas, remasters — e para quem simplesmente prefere outra arte.
/// </summary>
public partial class CoverPickerViewModel : ObservableObject
{
    private readonly GameArtService _art;
    private readonly GameLibraryService _library;
    private readonly GameEntry _game;

    private CancellationTokenSource? _searchCts;

    public event Action<bool>? CloseRequested;
    public event Func<string, string, string?>? PickFileRequested;

    public ObservableCollection<CoverCandidateViewModel> Candidates { get; } = new();

    [ObservableProperty] private string searchTerm = "";
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private CoverCandidateViewModel? selectedCandidate;

    public string GameName => _game.Name;

    public CoverPickerViewModel(GameEntry game, GameArtService art, GameLibraryService library)
    {
        _game = game;
        _art = art;
        _library = library;

        // Começa com o nome já limpo das marcações que atrapalham a busca.
        searchTerm = OnlineArtService.CleanTitle(game.Name);
        _ = SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        StatusMessage = Loc.S("GH_SearchingCovers");
        Candidates.Clear();

        try
        {
            var results = await _art.SearchOnlineAsync(_game, SearchTerm, token);
            if (token.IsCancellationRequested) return;

            if (results.Count == 0)
            {
                StatusMessage = Loc.S("GH_NoCoversFound");
                return;
            }

            StatusMessage = "";
            foreach (var candidate in results)
            {
                var item = new CoverCandidateViewModel(candidate);
                Candidates.Add(item);
                _ = LoadPreviewAsync(item, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = Loc.F("GH_ScanFailed", ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested) IsSearching = false;
        }
    }

    /// <summary>Baixa a miniatura de uma sugestão para o cache temporário e a exibe.</summary>
    private async Task LoadPreviewAsync(CoverCandidateViewModel item, CancellationToken token)
    {
        try
        {
            string temp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"pulse1x_cover_{item.Candidate.AppId}.jpg");

            if (!System.IO.File.Exists(temp) && _art.Online is not null)
                await _art.Online.DownloadAsync(item.Candidate.CoverUrl, temp, token);

            if (token.IsCancellationRequested) return;

            var bitmap = GameArtService.LoadBitmap(temp, 200);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                item.Preview = bitmap;
                item.IsLoading = false;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => item.IsLoading = false);
        }
    }

    /// <summary>Aplica a sugestão escolhida como capa (e hero, quando existir) do item.</summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedCandidate is null) return;

        IsSearching = true;
        StatusMessage = Loc.S("GH_ApplyingCover");
        try
        {
            if (await _art.ApplyCandidateAsync(_game, SelectedCandidate.Candidate))
            {
                _library.UpdateGame(_game);
                CloseRequested?.Invoke(true);
            }
            else
            {
                StatusMessage = Loc.S("GH_CoverApplyFailed");
            }
        }
        finally { IsSearching = false; }
    }

    /// <summary>Usa uma imagem do computador em vez de uma sugestão online.</summary>
    [RelayCommand]
    private void UseLocalFile()
    {
        string? file = PickFileRequested?.Invoke(Loc.S("GH_PickImage"),
            "Imagens (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Todos os arquivos (*.*)|*.*");
        if (file is null) return;

        if (_art.SetCustomArt(_game, ArtKind.Cover, file))
        {
            _library.UpdateGame(_game);
            CloseRequested?.Invoke(true);
        }
    }

    /// <summary>Descarta a arte atual para a busca automática tentar de novo do zero.</summary>
    [RelayCommand]
    private void ResetArt()
    {
        _art.ResetArt(_game);
        _library.UpdateGame(_game);
        _ = _art.EnsureArtAsync(_game).ContinueWith(_ => _library.Save());
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _searchCts?.Cancel();
        CloseRequested?.Invoke(false);
    }
}
