using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Capas de ROM, buscadas no acervo público de thumbnails do libretro — o mesmo que o RetroArch
/// usa, servido por HTTP simples, sem cadastro nem chave de API.
///
/// A dificuldade aqui não é baixar a imagem, é acertar o NOME do arquivo. O acervo segue a
/// convenção do No-Intro/Redump, que difere do nome que o usuário tem no disco em três pontos:
///
///   • a região faz parte do nome — "Metroid Prime (USA).png" existe, "Metroid Prime.png" não;
///   • artigos vão para o fim — "Legend of Zelda, The - Ocarina of Time", não "The Legend of...";
///   • dois-pontos e outros caracteres proibidos em nomes de arquivo viram hífen ou somem.
///
/// Por isso a busca tenta o caminho direto primeiro (barato: uma requisição) e, se falhar, baixa a
/// listagem do sistema uma única vez e procura nela por semelhança. A listagem fica em memória
/// durante a sessão, então o custo se paga já na segunda ROM do mesmo console.
///
/// Nada disso é obrigatório: sem internet, ou para um console fora do acervo (o Switch não está
/// lá), a busca não acha nada e o GameHub segue para as outras fontes de capa.
/// </summary>
public class RomArtService
{
    private const string Root = "https://thumbnails.libretro.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Listagem de cada sistema já baixada nesta sessão (nome do arquivo sem extensão).</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(2);

    static RomArtService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Pulse1x/1.6 (+GameHub)");
    }

    /// <summary>
    /// Nome da pasta do sistema no acervo, a partir da plataforma que o usuário escolheu ao
    /// cadastrar o emulador. Devolve null para consoles que o acervo não cobre — o Switch é o
    /// caso mais comum, e para ele a busca por nome na loja é o caminho.
    /// </summary>
    public static string? SystemFolderFor(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;
        string p = platform.Trim().ToLowerInvariant();

        foreach (var (fragment, folder) in Systems)
            if (p.Contains(fragment)) return folder;

        return null;
    }

    // Da plataforma escrita pelo usuário para a pasta do acervo. A ordem importa: "super nintendo"
    // precisa ser testado antes de "nintendo", e "playstation 2" antes de "playstation".
    private static readonly (string fragment, string folder)[] Systems =
    {
        ("super nintendo", "Nintendo - Super Nintendo Entertainment System"),
        ("super famicom", "Nintendo - Super Nintendo Entertainment System"),
        ("snes",          "Nintendo - Super Nintendo Entertainment System"),
        ("nintendo 64",   "Nintendo - Nintendo 64"),
        ("n64",           "Nintendo - Nintendo 64"),
        ("gamecube",      "Nintendo - GameCube"),
        ("game cube",     "Nintendo - GameCube"),
        ("wii u",         "Nintendo - Wii U"),
        ("wii",           "Nintendo - Wii"),
        ("game boy advance", "Nintendo - Game Boy Advance"),
        ("gba",           "Nintendo - Game Boy Advance"),
        ("game boy color", "Nintendo - Game Boy Color"),
        ("game boy",      "Nintendo - Game Boy"),
        ("nintendo ds",   "Nintendo - Nintendo DS"),
        ("3ds",           "Nintendo - Nintendo 3DS"),
        ("nintendo entertainment", "Nintendo - Nintendo Entertainment System"),
        ("nes",           "Nintendo - Nintendo Entertainment System"),
        ("famicom",       "Nintendo - Nintendo Entertainment System"),

        ("playstation 2", "Sony - PlayStation 2"),
        ("ps2",           "Sony - PlayStation 2"),
        ("playstation 3", "Sony - PlayStation 3"),
        ("ps3",           "Sony - PlayStation 3"),
        ("playstation 4", "Sony - PlayStation 4"),
        ("ps4",           "Sony - PlayStation 4"),
        ("playstation portable", "Sony - PlayStation Portable"),
        ("psp",           "Sony - PlayStation Portable"),
        ("playstation vita", "Sony - PlayStation Vita"),
        ("vita",          "Sony - PlayStation Vita"),
        ("playstation",   "Sony - PlayStation"),
        ("ps1",           "Sony - PlayStation"),
        ("psx",           "Sony - PlayStation"),

        ("mega drive",    "Sega - Mega Drive - Genesis"),
        ("genesis",       "Sega - Mega Drive - Genesis"),
        ("master system", "Sega - Master System - Mark III"),
        ("game gear",     "Sega - Game Gear"),
        ("dreamcast",     "Sega - Dreamcast"),
        ("saturn",        "Sega - Saturn"),
        ("sega cd",       "Sega - Mega-CD - Sega CD"),
        ("32x",           "Sega - 32X"),

        ("xbox 360",      "Microsoft - Xbox 360"),
        ("xbox",          "Microsoft - Xbox"),
        ("turbografx",    "NEC - PC Engine - TurboGrafx 16"),
        ("pc engine",     "NEC - PC Engine - TurboGrafx 16"),
        ("neo geo",       "SNK - Neo Geo"),
        ("wonderswan",    "Bandai - WonderSwan"),
        ("atari 2600",    "Atari - 2600"),
        ("atari 7800",    "Atari - 7800"),
        ("lynx",          "Atari - Lynx"),
        ("jaguar",        "Atari - Jaguar"),
        ("amiga",         "Commodore - Amiga"),
        ("msx",           "Microsoft - MSX"),
        ("arcade",        "MAME"),
        ("mame",          "MAME"),
    };

    /// <summary>
    /// Procura a capa de uma ROM e devolve a URL, ou null quando não encontra.
    /// <paramref name="romFileName"/> é o nome do ARQUIVO, sem extensão — é ele que carrega a
    /// região e a grafia que o acervo espera, perdidas no nome limpo exibido na biblioteca.
    /// </summary>
    public async Task<string?> FindCoverUrlAsync(string? platform, string romFileName, CancellationToken token = default)
    {
        string? system = SystemFolderFor(platform);
        if (system is null || string.IsNullOrWhiteSpace(romFileName)) return null;

        await _gate.WaitAsync(token);
        try
        {
            // 1) O nome do arquivo tal como está, só com os caracteres que o acervo substitui.
            string direct = SanitizeForCatalog(romFileName);
            string url = BuildUrl(system, direct);
            if (await ExistsAsync(url, token)) return url;

            // 2) Não bateu: procura na listagem do sistema por semelhança. A listagem é baixada uma
            //    vez por sessão e serve para todas as ROMs daquele console.
            var names = await GetCatalogAsync(system, token);
            string? match = BestMatch(romFileName, names);
            return match is not null ? BuildUrl(system, match) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
        finally { _gate.Release(); }
    }

    /// <summary>Baixa a capa para um arquivo. Devolve false (sem lançar) quando não dá.</summary>
    public async Task<bool> DownloadAsync(string url, string destination, CancellationToken token = default)
    {
        try
        {
            using var response = await Http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var file = File.Create(destination);
            await source.CopyToAsync(file, token);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    private static string BuildUrl(string system, string fileName) =>
        $"{Root}/{Uri.EscapeDataString(system)}/Named_Boxarts/{Uri.EscapeDataString(fileName)}.png";

    private static async Task<bool> ExistsAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(request, token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>
    /// Baixa (uma vez por sessão) a listagem de capas de um sistema. É uma página de índice do
    /// servidor, então basta extrair os nomes dos arquivos .png dos links.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetCatalogAsync(string system, CancellationToken token)
    {
        lock (_catalog)
            if (_catalog.TryGetValue(system, out var cached)) return cached;

        var names = new List<string>();
        try
        {
            string url = $"{Root}/{Uri.EscapeDataString(system)}/Named_Boxarts/";
            string html = await Http.GetStringAsync(url, token);

            foreach (Match m in Regex.Matches(html, @"href=""([^""]+\.png)""", RegexOptions.IgnoreCase))
            {
                string name = Uri.UnescapeDataString(m.Groups[1].Value);
                if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    names.Add(name[..^4]);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* sem rede ou sistema inexistente: lista vazia, e a busca apenas não acha */ }

        lock (_catalog) _catalog[system] = names;
        return names;
    }

    // =====================================================================================
    //  Correspondência de nomes
    // =====================================================================================

    // Caracteres que não podem existir num nome de arquivo: o acervo os substitui sempre da mesma
    // forma, então aplicamos a mesma troca antes de tentar o caminho direto.
    private static string SanitizeForCatalog(string name) =>
        name.Replace(":", " -").Replace("/", "-").Replace("?", "").Replace("*", "")
            .Replace("\"", "'").Replace("<", "").Replace(">", "").Replace("|", "-")
            .Trim();

    /// <summary>
    /// Escolhe o melhor nome da listagem para a ROM. Compara só as palavras que importam: fora as
    /// tags entre parênteses/colchetes (região, revisão, idiomas) e a pontuação, o que sobra é o
    /// título — e é nele que a decisão é tomada.
    /// </summary>
    private static string? BestMatch(string romName, IReadOnlyList<string> catalog)
    {
        if (catalog.Count == 0) return null;

        var wanted = TitleWords(romName);
        if (wanted.Count == 0) return null;

        string? best = null;
        double bestScore = 0;

        foreach (var entry in catalog)
        {
            var words = TitleWords(entry);
            if (words.Count == 0) continue;

            // Fração das palavras do título da ROM presentes no candidato, penalizando candidatos
            // muito mais longos — sem isso, uma coletânea casaria com qualquer jogo dela.
            int hits = wanted.Count(w => words.Contains(w));
            if (hits == 0) continue;

            double score = (double)hits / wanted.Count;
            score -= 0.03 * Math.Max(0, words.Count - wanted.Count);

            // Desempate: versões regionais/betas/protótipos perdem para a edição comum.
            if (entry.Contains("(USA)", StringComparison.OrdinalIgnoreCase)) score += 0.05;
            if (entry.Contains("Beta", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("Proto", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("Demo", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("Debug", StringComparison.OrdinalIgnoreCase)) score -= 0.5;

            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        // Abaixo de 80% das palavras, é mais provável ser outro jogo do que o certo — melhor não
        // ilustrar do que ilustrar errado.
        return bestScore >= 0.8 ? best : null;
    }

    /// <summary>
    /// Palavras do título, normalizadas: sem tags, sem pontuação e sem o artigo que o acervo move
    /// para o fim ("Legend of Zelda, The"). Comparar conjuntos de palavras absorve essa diferença
    /// de ordem sem precisar reproduzir a regra exata da convenção.
    /// </summary>
    private static HashSet<string> TitleWords(string raw)
    {
        string text = Regex.Replace(raw, @"[\(\[][^\)\]]*[\)\]]", " ");  // (USA), [!], (Rev 1)
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^a-z0-9 ]+", " ");                  // pontuação e acentos fora

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 0 && !Noise.Contains(w));

        return new HashSet<string>(words);
    }

    // Palavras que não distinguem um jogo de outro e só atrapalhariam a pontuação.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "and", "de", "da", "do", "el", "la", "les", "le",
    };
}
