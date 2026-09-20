using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Os três tipos de arte que o GameHub usa.</summary>
public enum ArtKind { Cover, Hero, Icon }

/// <summary>
/// Busca, guarda e gera a arte da biblioteca (capa vertical, background/hero e ícone).
///
/// A ordem de busca é sempre da fonte mais barata para a mais cara:
///   1. o que o usuário escolheu à mão (nunca é sobrescrito);
///   2. o cache local do próprio launcher — a Steam já baixou as capas dos seus jogos e elas estão
///      no disco, então na prática a biblioteca fica ilustrada sem baixar nada;
///   3. o CDN público da Steam, para jogos cujo cache local está incompleto;
///   4. o ícone embutido no executável, para jogos e aplicativos fora de loja;
///   5. um placeholder gerado na hora, com as iniciais sobre um gradiente derivado do nome — é
///      determinístico, então o mesmo jogo tem sempre a mesma cor.
///
/// Tudo o que é obtido vai para <c>%APPDATA%\Pulse1x\gamehub\art</c>, e a interface lê apenas do
/// disco. Isso mantém o carregamento da biblioteca rápido e permite o Modo Gaming suspender
/// qualquer busca sem deixar a tela quebrada.
/// </summary>
public class GameArtService
{
    private readonly string _artDir;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Evita baixar a mesma arte duas vezes quando vários cartões pedem ao mesmo tempo.</summary>
    private readonly SemaphoreSlim _downloadGate = new(3);
    private readonly HashSet<string> _failed = new();

    public GameArtService()
    {
        _artDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse1x", "gamehub", "art");
        Directory.CreateDirectory(_artDir);
    }

    public string ArtDirectory => _artDir;

    private string PathFor(GameEntry game, ArtKind kind, string extension = ".jpg") =>
        Path.Combine(_artDir, $"{game.Id}_{kind.ToString().ToLowerInvariant()}{extension}");

    // =====================================================================================
    //  Busca
    // =====================================================================================

    /// <summary>
    /// Garante que o item tenha capa, hero e ícone, preenchendo o que faltar. Devolve true quando
    /// algo mudou (e portanto vale salvar a biblioteca).
    /// </summary>
    public async Task<bool> EnsureArtAsync(GameEntry game, CancellationToken token = default)
    {
        if (game.ArtLockedByUser) return false;

        bool changed = false;
        changed |= await EnsureOneAsync(game, ArtKind.Cover, token);
        changed |= await EnsureOneAsync(game, ArtKind.Hero, token);
        changed |= await EnsureOneAsync(game, ArtKind.Icon, token);
        return changed;
    }

    /// <summary>
    /// Uma capa é "de verdade" quando não é o quadrado colorido que o Pulse1x desenha ao não achar
    /// nada. Como o placeholder é gravado com esse marcador no nome, dá para reconhecê-lo depois e
    /// tentar de novo — é o que permite ilustrar um jogo que ficou sem capa numa varredura antiga,
    /// por exemplo porque a internet estava fora ou o acervo ainda não tinha aquele título.
    /// </summary>
    public static bool IsPlaceholder(string? path) =>
        path is not null && path.Contains("_placeholder_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Item sem capa de verdade — nenhuma, ou apenas o placeholder gerado.</summary>
    public static bool NeedsRealCover(GameEntry game) =>
        string.IsNullOrEmpty(game.CoverPath) || IsPlaceholder(game.CoverPath) || !File.Exists(game.CoverPath);

    /// <summary>
    /// Procura de novo a capa dos itens que ficaram sem uma de verdade. Diferente do
    /// <see cref="EnsureArtAsync"/>, aqui o placeholder não conta como capa e as falhas anteriores
    /// são esquecidas — é uma segunda chance, pedida pelo usuário.
    /// </summary>
    public async Task<int> RefetchMissingCoversAsync(IEnumerable<GameEntry> games,
        IProgress<(int done, int total, string name)>? progress = null, CancellationToken token = default)
    {
        var pending = games.Where(g => !g.ArtLockedByUser && NeedsRealCover(g)).ToList();
        if (pending.Count == 0) return 0;

        lock (_failed) _failed.Clear();

        int found = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var game = pending[i];
            progress?.Report((i + 1, pending.Count, game.Name));

            string? resolved = await ResolveRealCoverAsync(game, token);
            if (resolved is null) continue;

            game.CoverPath = resolved;
            found++;
        }

        return found;
    }

    /// <summary>
    /// Só as fontes que produzem uma capa real: acervo de ROM e busca na loja. Sem placeholder —
    /// quem chama isto já tem um e quer trocá-lo por arte de verdade.
    /// </summary>
    private async Task<string?> ResolveRealCoverAsync(GameEntry game, CancellationToken token)
    {
        if (game.Launcher == LauncherKind.Steam && game.LauncherAppId is not null)
        {
            string? local = SteamLocalArt(game.LauncherAppId, ArtKind.Cover);
            if (local is not null) return local;

            string? downloaded = await DownloadSteamArtAsync(game, ArtKind.Cover, token);
            if (downloaded is not null) return downloaded;
        }

        if (RomArt is not null && game.Launcher == LauncherKind.Emulator)
        {
            string? boxart = await DownloadRomCoverAsync(game, token);
            if (boxart is not null) return boxart;
        }

        if (Online is not null)
            return await DownloadByNameAsync(game, ArtKind.Cover, token);

        return null;
    }

    private async Task<bool> EnsureOneAsync(GameEntry game, ArtKind kind, CancellationToken token)
    {
        string? current = kind switch
        {
            ArtKind.Cover => game.CoverPath,
            ArtKind.Hero => game.HeroPath,
            _ => game.IconPath,
        };

        if (current is not null && File.Exists(current)) return false;

        string? resolved = await ResolveAsync(game, kind, token);
        if (resolved is null) return false;

        switch (kind)
        {
            case ArtKind.Cover: game.CoverPath = resolved; break;
            case ArtKind.Hero: game.HeroPath = resolved; break;
            default: game.IconPath = resolved; break;
        }
        return true;
    }

    /// <summary>Busca online por nome — injetada pelo App quando a opção está ligada.</summary>
    public OnlineArtService? Online { get; set; }

    /// <summary>Capas de ROM no acervo do libretro — a loja não cataloga jogos de console.</summary>
    public RomArtService? RomArt { get; set; }

    /// <summary>Procurar capa na internet pelo nome quando não houver arte local.</summary>
    public bool OnlineEnabled { get; set; } = true;

    private async Task<string?> ResolveAsync(GameEntry game, ArtKind kind, CancellationToken token)
    {
        // 1/2) Cache local da Steam — sem rede, instantâneo.
        if (game.Launcher == LauncherKind.Steam && game.LauncherAppId is not null)
        {
            string? local = SteamLocalArt(game.LauncherAppId, kind);
            if (local is not null) return local;

            // 3) CDN público da Steam, pelo appid que já conhecemos.
            string? downloaded = await DownloadSteamArtAsync(game, kind, token);
            if (downloaded is not null) return downloaded;
        }

        // 4) ROM: o acervo do libretro tem a capa original do console, que a loja não cataloga.
        //    Vem antes da busca por nome porque é mais preciso — casa pelo arquivo, com região.
        if (OnlineEnabled && RomArt is not null && kind == ArtKind.Cover &&
            game.Launcher == LauncherKind.Emulator)
        {
            string? boxart = await DownloadRomCoverAsync(game, token);
            if (boxart is not null) return boxart;
        }

        // 5) Busca online pelo NOME. É o que ilustra a Epic, a GOG, os executáveis avulsos e as
        //    ROMs de console fora do acervo (o Switch), que não têm appid nem cache local.
        if (OnlineEnabled && Online is not null && kind != ArtKind.Icon)
        {
            string? found = await DownloadByNameAsync(game, kind, token);
            if (found is not null) return found;
        }

        // 6) Ícone do executável — serve de capa/ícone para jogos e apps fora de loja.
        if (kind != ArtKind.Hero)
        {
            string? extracted = ExtractIcon(game);
            if (extracted is not null) return extracted;
        }

        // 7) Placeholder gerado (só capa e hero; sem ícone o cartão usa a própria capa).
        if (kind == ArtKind.Cover) return GeneratePlaceholder(game, 300, 450);
        if (kind == ArtKind.Hero) return GeneratePlaceholder(game, 960, 540);
        return null;
    }

    /// <summary>
    /// Baixa a capa original da ROM no acervo do libretro. A busca usa o nome do ARQUIVO, não o
    /// nome exibido: o scanner limpa "(USA)" e as demais tags do título para a biblioteca ficar
    /// legível, mas é justamente essa marcação que identifica a capa certa no acervo.
    /// </summary>
    private async Task<string?> DownloadRomCoverAsync(GameEntry game, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game.RomPath)) return null;

        string key = $"rom:{game.Id}";
        lock (_failed) { if (_failed.Contains(key)) return null; }

        try
        {
            string fileName = Path.GetFileNameWithoutExtension(game.RomPath);
            string? url = await RomArt!.FindCoverUrlAsync(game.Category, fileName, token);
            if (url is null)
            {
                lock (_failed) _failed.Add(key);
                return null;
            }

            string destination = PathFor(game, ArtKind.Cover);
            if (!await RomArt.DownloadAsync(url, destination, token))
            {
                lock (_failed) _failed.Add(key);
                return null;
            }

            return destination;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            lock (_failed) _failed.Add(key);
            return null;
        }
    }

    /// <summary>
    /// Procura a arte na internet pelo nome do jogo e baixa a melhor correspondência. Guardamos o
    /// appid encontrado no item, para as próximas artes virem direto sem nova busca.
    /// </summary>
    private async Task<string?> DownloadByNameAsync(GameEntry game, ArtKind kind, CancellationToken token)
    {
        string key = $"name:{game.Id}:{kind}";
        lock (_failed) { if (_failed.Contains(key)) return null; }

        try
        {
            var results = await Online!.SearchAsync(game.Name, token);
            if (results.Count == 0)
            {
                lock (_failed) _failed.Add(key);
                return null;
            }

            var best = results[0];
            string? url = kind == ArtKind.Cover ? best.CoverUrl : best.HeroUrl;
            if (url is null) return null;

            string destination = PathFor(game, kind);
            if (!await Online.DownloadAsync(url, destination, token))
            {
                lock (_failed) _failed.Add(key);
                return null;
            }

            game.OnlineArtAppId = best.AppId;
            return destination;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            lock (_failed) _failed.Add(key);
            return null;
        }
    }

    /// <summary>
    /// Lista as capas encontradas na internet para este item, para o usuário escolher a certa
    /// quando a busca automática acertar o jogo errado (nomes parecidos, coletâneas, remasters).
    /// </summary>
    public async Task<IReadOnlyList<ArtCandidate>> SearchOnlineAsync(GameEntry game, string? overrideTitle = null,
        CancellationToken token = default)
    {
        if (Online is null) return Array.Empty<ArtCandidate>();
        try { return await Online.SearchAsync(overrideTitle ?? game.Name, token); }
        catch { return Array.Empty<ArtCandidate>(); }
    }

    /// <summary>Aplica uma capa escolhida pelo usuário na lista de sugestões online.</summary>
    public async Task<bool> ApplyCandidateAsync(GameEntry game, ArtCandidate candidate, CancellationToken token = default)
    {
        if (Online is null) return false;

        bool any = false;

        string cover = PathFor(game, ArtKind.Cover);
        if (await Online.DownloadAsync(candidate.CoverUrl, cover, token))
        {
            game.CoverPath = cover;
            any = true;
        }

        if (candidate.HeroUrl is not null)
        {
            string hero = PathFor(game, ArtKind.Hero);
            if (await Online.DownloadAsync(candidate.HeroUrl, hero, token))
            {
                game.HeroPath = hero;
                any = true;
            }
        }

        if (any)
        {
            game.OnlineArtAppId = candidate.AppId;
            // Escolha manual: a busca automática não mexe mais neste item.
            game.ArtLockedByUser = true;
        }

        return any;
    }

    /// <summary>
    /// Procura a arte que a própria Steam já baixou
    /// (<c>Steam\appcache\librarycache\{appid}\...</c>). Como o cliente mantém esse cache para a sua
    /// biblioteca, quase todos os jogos instalados já têm capa e hero disponíveis offline.
    /// </summary>
    private static string? SteamLocalArt(string appId, ArtKind kind)
    {
        string? steam = SteamScanner.SteamPath;
        if (steam is null) return null;

        string[] names = kind switch
        {
            ArtKind.Cover => new[] { "library_600x900.jpg", "library_600x900_2x.jpg", "header.jpg" },
            ArtKind.Hero => new[] { "library_hero.jpg", "library_hero_blur.jpg", "header.jpg" },
            _ => new[] { "logo.png", "header.jpg" },
        };

        // Layout atual (uma pasta por jogo) e o antigo (arquivos com o appid no nome).
        var candidates = names
            .Select(n => Path.Combine(steam, "appcache", "librarycache", appId, n))
            .Concat(names.Select(n => Path.Combine(steam, "appcache", "librarycache", $"{appId}_{n}")));

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task<string?> DownloadSteamArtAsync(GameEntry game, ArtKind kind, CancellationToken token)
    {
        string appId = game.LauncherAppId!;
        string key = $"{appId}:{kind}";
        lock (_failed) { if (_failed.Contains(key)) return null; }

        string[] urls = kind switch
        {
            ArtKind.Cover => new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
            },
            ArtKind.Hero => new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_hero.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
            },
            _ => new[] { $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/logo.png" },
        };

        await _downloadGate.WaitAsync(token);
        try
        {
            foreach (var url in urls)
            {
                try
                {
                    using var response = await Http.GetAsync(url, token);
                    if (!response.IsSuccessStatusCode) continue;

                    string extension = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
                    string destination = PathFor(game, kind, extension);
                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    await using var file = File.Create(destination);
                    await stream.CopyToAsync(file, token);
                    return destination;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* tenta a próxima URL */ }
            }
        }
        catch (OperationCanceledException) { return null; }
        finally { _downloadGate.Release(); }

        lock (_failed) _failed.Add(key);
        return null;
    }

    /// <summary>Extrai o ícone de maior resolução embutido no executável do jogo/app.</summary>
    private string? ExtractIcon(GameEntry game)
    {
        string exe = game.Launcher == LauncherKind.Emulator && !string.IsNullOrEmpty(game.Executable)
            ? game.Executable
            : game.Executable;

        if (string.IsNullOrWhiteSpace(exe) || exe.Contains("://") || !File.Exists(exe)) return null;

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return null;

            string destination = PathFor(game, ArtKind.Icon, ".png");
            using var bitmap = icon.ToBitmap();
            bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
            return destination;
        }
        catch { return null; }
    }

    // =====================================================================================
    //  Placeholder
    // =====================================================================================

    /// <summary>
    /// Desenha uma capa elegante quando não existe arte: gradiente escuro com um tom derivado do
    /// nome do jogo (sempre o mesmo para o mesmo nome), as iniciais em destaque e o nome completo
    /// embaixo. Fica coerente com o resto do GameHub em vez de um retângulo vazio.
    /// </summary>
    public string? GeneratePlaceholder(GameEntry game, int width, int height)
    {
        try
        {
            string destination = Path.Combine(_artDir,
                $"{game.Id}_placeholder_{width}x{height}.png");

            var hue = HueFromName(game.Name);
            var top = ColorFromHsl(hue, 0.45, 0.28);
            var bottom = ColorFromHsl((hue + 28) % 360, 0.55, 0.10);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(1, 1));
                context.DrawRectangle(brush, null, rect);

                // Brilho diagonal sutil, para o cartão não parecer chapado.
                var glow = new LinearGradientBrush(
                    Color.FromArgb(38, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0.9, 0.6));
                context.DrawRectangle(glow, null, rect);

                string initials = Initials(game.Name);
                var initialsText = new FormattedText(
                    initials, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    height * 0.28, new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 96);

                context.DrawText(initialsText,
                    new Point((width - initialsText.Width) / 2, height * 0.32));

                var nameText = new FormattedText(
                    game.Name, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    Math.Max(12, height * 0.045), new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 96)
                {
                    MaxTextWidth = width * 0.84,
                    MaxLineCount = 2,
                    TextAlignment = TextAlignment.Center,
                    Trimming = TextTrimming.CharacterEllipsis,
                };

                context.DrawText(nameText, new Point(width * 0.08, height * 0.72));
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(destination);
            encoder.Save(stream);

            return destination;
        }
        catch { return null; }
    }

    /// <summary>Até duas iniciais a partir do nome ("Cyberpunk 2077" vira "C2", "Hades" vira "HA").</summary>
    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', ':', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        if (words.Length == 1)
            return words[0].Length >= 2 ? words[0][..2].ToUpperInvariant() : words[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
    }

    /// <summary>Matiz estável derivada do nome — o mesmo jogo recebe sempre a mesma cor.</summary>
    private static double HueFromName(string name)
    {
        int hash = 17;
        foreach (char c in name) hash = hash * 31 + c;
        return Math.Abs(hash) % 360;
    }

    private static Color ColorFromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - c / 2;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // =====================================================================================
    //  Substituição manual
    // =====================================================================================

    /// <summary>
    /// Copia uma imagem escolhida pelo usuário para o cache e a define como a arte do item. A partir
    /// daí o item fica marcado como "arte definida à mão" e a busca automática não mexe mais nele.
    /// </summary>
    public bool SetCustomArt(GameEntry game, ArtKind kind, string sourceFile)
    {
        try
        {
            if (!File.Exists(sourceFile)) return false;

            string extension = Path.GetExtension(sourceFile);
            if (string.IsNullOrEmpty(extension)) extension = ".png";
            string destination = Path.Combine(_artDir,
                $"{game.Id}_{kind.ToString().ToLowerInvariant()}_custom{extension}");

            File.Copy(sourceFile, destination, overwrite: true);

            switch (kind)
            {
                case ArtKind.Cover: game.CoverPath = destination; break;
                case ArtKind.Hero: game.HeroPath = destination; break;
                default: game.IconPath = destination; break;
            }

            game.ArtLockedByUser = true;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Descarta a arte atual do item para que a busca automática rode de novo.</summary>
    public void ResetArt(GameEntry game)
    {
        game.ArtLockedByUser = false;
        foreach (var path in new[] { game.CoverPath, game.HeroPath, game.IconPath })
        {
            if (path is null || !path.StartsWith(_artDir, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(path); } catch { }
        }
        game.CoverPath = game.HeroPath = game.IconPath = null;
    }

    /// <summary>Carrega uma imagem do disco já pronta para exibir, sem travar o arquivo.</summary>
    public static BitmapImage? LoadBitmap(string? path, int decodeWidth = 0)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // OnLoad + o stream fechado logo em seguida: o arquivo não fica preso, então o usuário
            // pode trocar a capa a qualquer momento sem "arquivo em uso".
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }
}
