using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Uma capa encontrada na busca online, já pronta para exibir como sugestão.</summary>
public record ArtCandidate(string Title, string CoverUrl, string? HeroUrl, string AppId);

/// <summary>
/// Busca capas na internet pelo NOME do jogo, para os itens que não têm arte local — jogos da
/// Epic/GOG, executáveis avulsos e ROMs de emulador.
///
/// Usa o catálogo público da Steam (a mesma busca do site da loja), que não exige cadastro nem
/// chave de API: procuramos o título, pegamos o appid do melhor resultado e montamos as URLs de
/// capa vertical e hero no CDN. Como a Steam cataloga praticamente todo jogo de PC, isso cobre bem
/// mais do que só a biblioteca Steam do usuário.
///
/// Nada aqui é obrigatório: sem internet, a busca simplesmente não encontra nada e o GameHub segue
/// com o ícone do executável ou com a capa gerada.
/// </summary>
public class OnlineArtService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>Nomes já buscados nesta sessão — evita repetir a consulta a cada varredura.</summary>
    private readonly Dictionary<string, IReadOnlyList<ArtCandidate>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>No máximo duas buscas ao mesmo tempo: é educado com o serviço e com a rede.</summary>
    private readonly SemaphoreSlim _gate = new(2);

    static OnlineArtService()
    {
        // Um User-Agent identificável evita ser tratado como tráfego anônimo suspeito.
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Pulse1x/1.2 (+GameHub)");
    }

    /// <summary>
    /// Procura capas para um título. Devolve as melhores opções (para o usuário escolher) ou uma
    /// lista vazia quando não há resultado.
    /// </summary>
    public async Task<IReadOnlyList<ArtCandidate>> SearchAsync(string title, CancellationToken token = default)
    {
        string term = CleanTitle(title);
        if (string.IsNullOrWhiteSpace(term)) return Array.Empty<ArtCandidate>();

        lock (_cache)
            if (_cache.TryGetValue(term, out var cached)) return cached;

        await _gate.WaitAsync(token);
        try
        {
            var results = await QuerySteamAsync(term, token);

            lock (_cache)
            {
                if (_cache.Count > 400) _cache.Clear();
                _cache[term] = results;
            }

            return results;
        }
        catch (OperationCanceledException) { throw; }
        catch { return Array.Empty<ArtCandidate>(); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Consulta o endpoint público de sugestões da loja Steam. Ele devolve JSON com os títulos que
    /// batem com o termo, cada um com o seu appid — que é tudo o que precisamos para montar as URLs
    /// da arte no CDN.
    /// </summary>
    private static async Task<IReadOnlyList<ArtCandidate>> QuerySteamAsync(string term, CancellationToken token)
    {
        string url = "https://store.steampowered.com/api/storesearch/?cc=br&l=portuguese&term="
                     + Uri.EscapeDataString(term);

        using var response = await Http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode) return Array.Empty<ArtCandidate>();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return Array.Empty<ArtCandidate>();

        var candidates = new List<ArtCandidate>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idElement)) continue;
            if (!item.TryGetProperty("name", out var nameElement)) continue;

            string appId = idElement.ValueKind == JsonValueKind.Number
                ? idElement.GetInt64().ToString()
                : idElement.GetString() ?? "";
            string name = nameElement.GetString() ?? "";
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name)) continue;

            candidates.Add(new ArtCandidate(
                name,
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_hero.jpg",
                appId));

            if (candidates.Count >= 8) break;
        }

        // O mais parecido com o que o usuário tem na biblioteca vem primeiro.
        return candidates
            .OrderByDescending(c => Similarity(term, c.Title))
            .ToList();
    }

    /// <summary>
    /// A URL da arte de um appid, perguntada à API de detalhes da loja. Os endereços fixos do CDN
    /// (.../apps/&lt;id&gt;/library_600x900.jpg) deixaram de valer para os jogos publicados mais
    /// recentemente, que a Steam serve por um caminho com hash — daí o 404 em títulos que estão
    /// na loja normalmente. Aqui a própria loja diz qual é o endereço bom.
    /// </summary>
    public async Task<string?> ResolveArtUrlAsync(string appId, bool wantHero, CancellationToken token = default)
    {
        try
        {
            using var response = await Http.GetAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}", token);
            if (!response.IsSuccessStatusCode) return null;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!document.RootElement.TryGetProperty(appId, out var entry)) return null;
            if (!entry.TryGetProperty("data", out var data)) return null;

            if (data.TryGetProperty("header_image", out var header))
            {
                string? url = header.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            if (!wantHero && data.TryGetProperty("capsule_image", out var capsule))
            {
                string? url = capsule.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Baixa uma imagem para um arquivo. Devolve false (sem lançar) quando a URL não existe — é
    /// comum um jogo ter capa vertical mas não hero, por exemplo.
    /// </summary>
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

    // =====================================================================================
    //  Normalização
    // =====================================================================================

    /// <summary>
    /// Limpa o nome antes de buscar. Nomes de biblioteca costumam trazer ruído que atrapalha a
    /// busca: marcas registradas, edições, sufixos de versão e as tags típicas de ROM.
    /// </summary>
    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string text = raw;
        text = Regex.Replace(text, @"[\(\[][^\)\]]*[\)\]]", " ");          // (USA), [!], (v1.2)
        text = text.Replace("™", " ").Replace("®", " ").Replace("©", " ");
        text = Regex.Replace(text, @"\b(GOTY|Definitive|Deluxe|Ultimate|Complete|Remastered|Edition|Edição)\b",
            " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[_\-]+", " ");
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim();
    }

    /// <summary>Semelhança simples entre dois títulos: fração de palavras do termo que aparecem no
    /// resultado. Suficiente para ordenar sugestões e barato de calcular.</summary>
    private static double Similarity(string term, string candidate)
    {
        var termWords = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (termWords.Length == 0) return 0;

        int hits = termWords.Count(w => candidate.Contains(w, StringComparison.OrdinalIgnoreCase));
        double score = (double)hits / termWords.Length;

        // Um título idêntico vale mais do que um que apenas contém as palavras.
        if (string.Equals(term, candidate, StringComparison.OrdinalIgnoreCase)) score += 1;
        return score;
    }
}
