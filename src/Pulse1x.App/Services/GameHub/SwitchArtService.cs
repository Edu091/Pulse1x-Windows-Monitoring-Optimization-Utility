using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Capas de jogos de Nintendo Switch, tiradas do catálogo público da eShop (o titledb, que reúne
/// os metadados que a própria Nintendo publica: nome, ícone, banner e descrição de cada título).
///
/// O acervo do libretro, usado para os demais consoles, não cobre o Switch — e a loja da Steam só
/// ajuda em jogos multiplataforma, deixando os exclusivos sem nada. Aqui a imagem vem do CDN da
/// eShop, que é onde a arte oficial de cada jogo mora.
///
/// O catálogo é um JSON grande demais para carregar de uma vez (perto de 90 MB), então ele é lido
/// em fluxo: percorremos os registros descartando cada um logo depois de olhar, guardando apenas
/// o par nome→imagem. O que fica em memória é uma fração disso, e só na primeira ROM de Switch da
/// sessão — dali em diante a consulta é instantânea.
/// </summary>
public class SwitchArtService
{
    // O catálogo da região americana em inglês: é o mais completo, e os nomes batem com os que
    // aparecem nos arquivos de ROM.
    private const string CatalogUrl = "https://raw.githubusercontent.com/blawar/titledb/master/US.en.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>Nome normalizado do jogo → URL da arte. Montado uma vez por sessão.</summary>
    private Dictionary<string, string>? _index;
    private readonly SemaphoreSlim _buildGate = new(1);

    static SwitchArtService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Pulse1x/1.7 (+GameHub)");
    }

    /// <summary>
    /// Procura a arte de um jogo de Switch pelo nome do arquivo da ROM. Devolve null quando o
    /// catálogo não está acessível ou o nome não corresponde a nenhum título com confiança.
    /// </summary>
    public async Task<string?> FindCoverUrlAsync(string romFileName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(romFileName)) return null;

        var index = await GetIndexAsync(token);
        if (index is null || index.Count == 0) return null;

        var wanted = TitleWords(romFileName);
        if (wanted.Count == 0) return null;

        // Nome idêntico resolve a maioria dos casos sem precisar pontuar nada.
        string exact = string.Join(' ', wanted.OrderBy(w => w));
        if (index.TryGetValue(exact, out var direct)) return direct;

        string? best = null;
        double bestScore = 0;

        foreach (var (key, url) in index)
        {
            var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int hits = wanted.Count(w => words.Contains(w));
            if (hits == 0) continue;

            // Penaliza títulos bem mais longos: sem isso uma coletânea ou uma edição especial
            // abocanharia qualquer jogo cujo nome esteja contido nela.
            double score = (double)hits / wanted.Count - 0.04 * Math.Max(0, words.Length - wanted.Count);
            if (score > bestScore) { bestScore = score; best = url; }
        }

        // Mesma régua do acervo de ROM: abaixo disso é mais provável ser outro jogo.
        return bestScore >= 0.8 ? best : null;
    }

    /// <summary>
    /// Monta (uma vez) o índice nome→imagem lendo o catálogo em fluxo. Uma falha aqui é silenciosa
    /// e definitiva para a sessão: sem internet, as ROMs de Switch simplesmente seguem sem capa.
    /// </summary>
    private async Task<Dictionary<string, string>?> GetIndexAsync(CancellationToken token)
    {
        if (_index is not null) return _index;

        await _buildGate.WaitAsync(token);
        try
        {
            if (_index is not null) return _index;

            var index = new Dictionary<string, string>(StringComparer.Ordinal);

            using var response = await Http.GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) { _index = index; return _index; }

            await using var stream = await response.Content.ReadAsStreamAsync(token);

            // Lê o catálogo registro a registro. Carregá-lo inteiro num JsonDocument custaria
            // centenas de megabytes de memória para extrair dois campos de cada entrada; aqui só
            // o índice final permanece, e ele é pequeno.
            string? name = null, banner = null, icon = null;

            await foreach (var (property, value) in ReadRecordsAsync(stream, token))
            {
                token.ThrowIfCancellationRequested();

                switch (property)
                {
                    case null:                      // fim de um registro
                        AddToIndex(index, name, banner ?? icon);
                        name = banner = icon = null;
                        break;
                    case "name": name = value; break;
                    case "bannerUrl": banner = value; break;
                    case "iconUrl": icon = value; break;
                }
            }

            _index = index;
            return _index;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _index = new Dictionary<string, string>(StringComparer.Ordinal);
            return _index;
        }
        finally { _buildGate.Release(); }
    }

    // Guarda um título no índice. A chave é o conjunto de palavras ordenado: absorve diferenças
    // de ordem e de pontuação entre o nome do arquivo e o da eShop.
    private static void AddToIndex(Dictionary<string, string> index, string? name, string? url)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(url)) return;

        var words = TitleWords(name);
        if (words.Count == 0) return;

        index.TryAdd(string.Join(' ', words.OrderBy(w => w)), url);
    }

    /// <summary>
    /// Percorre o catálogo devolvendo, para cada registro, os campos que interessam e um marcador
    /// de fim (propriedade nula). Ler assim mantém o uso de memória constante: em vez de uma
    /// árvore com o arquivo inteiro, só um buffer deslizante e o índice que estamos montando.
    /// </summary>
    private static async IAsyncEnumerable<(string? property, string? value)> ReadRecordsAsync(
        Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new byte[128 * 1024];
        int dataLength = 0;
        bool final = false;
        var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

        // Profundidade 1 = dentro do objeto raiz; 2 = dentro de um registro de jogo.
        int depth = 0;
        string? pending = null;

        while (!final)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(dataLength), token);
            if (read == 0) final = true;
            dataLength += read;

            var results = new List<(string?, string?)>();
            int consumed = ReadChunk(buffer.AsSpan(0, dataLength), final, ref state, ref depth, ref pending, results);

            foreach (var item in results) yield return item;

            // O que sobrou é um token partido no meio: volta para o começo do buffer e completa
            // com a próxima leitura.
            int leftover = dataLength - consumed;
            if (leftover > 0) Array.Copy(buffer, consumed, buffer, 0, leftover);
            dataLength = leftover;

            // Um registro maior que o buffer (descrições longas) precisa de mais espaço.
            if (leftover == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
        }
    }

    // Lê o que couber do buffer. Fica em método separado porque Span não pode atravessar um await.
    private static int ReadChunk(ReadOnlySpan<byte> data, bool final, ref JsonReaderState state,
        ref int depth, ref string? pending, List<(string?, string?)> results)
    {
        var reader = new Utf8JsonReader(data, final, state);

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    depth++;
                    break;

                case JsonTokenType.EndObject:
                    if (depth == 2) results.Add((null, null));   // fim de um registro de jogo
                    depth--;
                    break;

                case JsonTokenType.PropertyName:
                    pending = depth == 2 ? reader.GetString() : null;
                    break;

                case JsonTokenType.String:
                    if (pending is "name" or "bannerUrl" or "iconUrl")
                        results.Add((pending, reader.GetString()));
                    pending = null;
                    break;

                default:
                    pending = null;
                    break;
            }
        }

        state = reader.CurrentState;
        return (int)reader.BytesConsumed;
    }

    /// <summary>Baixa a arte para um arquivo.</summary>
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

    /// <summary>
    /// Palavras que identificam o título, sem as tags do arquivo, a pontuação, os símbolos de
    /// marca e os artigos — o mesmo tratamento dos dois lados da comparação.
    /// </summary>
    private static HashSet<string> TitleWords(string raw)
    {
        string text = Regex.Replace(raw, @"[\(\[][^\)\]]*[\)\]]", " ");   // (USA), [v1.0], (NSP)
        text = text.Replace("™", " ").Replace("®", " ").Replace("©", " ");
        text = text.ToLowerInvariant();

        // "Pokémon" na eShop, "Pokemon" no arquivo: sem tirar o acento, os dois nunca se encontram.
        text = string.Concat(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark));

        text = Regex.Replace(text, @"[^a-z0-9 ]+", " ");

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Noise.Contains(w));

        return new HashSet<string>(words);
    }

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "and", "de", "da", "do", "el", "la", "le", "les",
        "nsp", "xci", "nsz", "xcz", "switch", "eshop",
    };
}
