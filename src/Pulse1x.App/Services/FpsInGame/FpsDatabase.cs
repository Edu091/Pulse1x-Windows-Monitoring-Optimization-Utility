using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Pulse1x.App.Services.FpsInGame;

/// <summary>GPU do catálogo, com o score PassMark G3D Mark usado em todas as comparações.</summary>
public sealed record GpuSpec(string Name, string Type, int G3d)
{
    public override string ToString() => Name;
}

/// <summary>
/// Um jogo da base: requisitos publicados na Steam e o FPS estimado (1080p, preset Alto) em cada
/// uma das GPUs de referência. A estimativa para o PC do usuário sai da interpolação desses pontos.
/// </summary>
public sealed class GameSpec
{
    public required string Name { get; init; }
    public int? Year { get; init; }
    public string? SteamAppId { get; init; }
    public string MinGpu { get; init; } = "";
    public string MinCpu { get; init; } = "";
    public double? MinRamGb { get; init; }
    public string RecGpu { get; init; } = "";
    public string RecCpu { get; init; } = "";
    public double? RecRamGb { get; init; }
    public int? MinG3d { get; init; }
    public int? RecG3d { get; init; }
    /// <summary>Limite de FPS do próprio motor do jogo (ex.: 60), quando existe.</summary>
    public int? FpsCap { get; init; }
    /// <summary>Posição no ranking de popularidade (0 = mais jogado); null fora do ranking.</summary>
    public int? Popularity { get; init; }
    public string Slug { get; init; } = "";
    /// <summary>FPS 1080p Alto nas GPUs de referência, na ordem de <see cref="FpsDatabase.ReferenceScores"/>.</summary>
    public required double[] ReferenceFps { get; init; }

    /// <summary>Nome normalizado para busca (minúsculo, sem acentos nem pontuação).</summary>
    public string SearchKey { get; init; } = "";

    public string PageUrl => string.IsNullOrEmpty(Slug) ? FpsDatabase.SourceUrl : $"{FpsDatabase.SourceUrl}/game/{Slug}";
}

/// <summary>
/// Base de jogos e GPUs da categoria Fps-In-Game, embutida no executável (funciona offline).
///
/// Os dados vêm prontos do dataset aberto da PCGameBenchmarks (CC BY 4.0): requisitos da Steam de
/// mais de 16 mil jogos, FPS estimado em 12 GPUs de referência (calibrado com ~2 mil medições reais
/// da GameGPU e Notebookcheck) e o score G3D de ~1.400 GPUs. O Pulse1x não estima jogo a jogo — só
/// interpola a curva já publicada para a GPU do usuário. Para atualizar a base, rode
/// <c>tools/FpsInGameData/build_dataset.py</c>.
/// </summary>
public sealed class FpsDatabase
{
    public const string SourceName = "PCGameBenchmarks";
    public const string SourceUrl = "https://pcgamebenchmarks.com";

    public IReadOnlyList<GameSpec> Games { get; }
    public IReadOnlyList<GpuSpec> Gpus { get; }
    /// <summary>Score G3D de cada GPU de referência, na ordem das colunas de FPS dos jogos.</summary>
    public IReadOnlyList<int> ReferenceScores { get; }

    private FpsDatabase(IReadOnlyList<GameSpec> games, IReadOnlyList<GpuSpec> gpus, IReadOnlyList<int> referenceScores)
    {
        Games = games;
        Gpus = gpus;
        ReferenceScores = referenceScores;
    }

    private static Task<FpsDatabase>? _loading;

    /// <summary>Carrega a base uma única vez, fora da thread de UI (a descompressão leva ~100 ms).</summary>
    public static Task<FpsDatabase> LoadAsync() => _loading ??= Task.Run(Load);

    public static FpsDatabase Load()
    {
        var assembly = typeof(FpsDatabase).Assembly;
        var gpus = ParseGpus(ReadLines(assembly, "Pulse1x.App.FpsInGame.gpus.tsv.gz"));
        var (refs, games) = ParseGames(ReadLines(assembly, "Pulse1x.App.FpsInGame.games.tsv.gz"));
        return new FpsDatabase(games, gpus, refs);
    }

    internal static IEnumerable<string> ReadLines(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Recurso embutido ausente: {resource}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    internal static List<GpuSpec> ParseGpus(IEnumerable<string> lines)
    {
        var list = new List<GpuSpec>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 3 || !int.TryParse(f[2], out int g3d)) continue;
            // "RadeonT" é o símbolo ™ corrompido no envio ao PassMark.
            string name = f[0].Replace("RadeonT ", "Radeon ", StringComparison.Ordinal);
            list.Add(new GpuSpec(name, f[1], g3d));
        }
        // O catálogo vem do PassMark, que aceita qualquer adaptador enviado por usuários: telas
        // USB, monitores virtuais e nomes corrompidos ficam de fora do seletor e do reconhecimento.
        // O mesmo chip às vezes aparece duas vezes ("Intel UHD 630" e "Intel UHD Graphics 630"):
        // as entradas viram uma só, com a média dos scores e o nome mais completo.
        return list.Where(GpuMatcher.IsCatalogGpu)
            .GroupBy(g => string.Join(' ', GpuMatcher.Tokens(g.Name).OrderBy(t => t, StringComparer.Ordinal)))
            .Select(group => group.Count() == 1
                ? group.First()
                : group.OrderByDescending(g => g.Name.Length).First() with { G3d = (int)Math.Round(group.Average(g => g.G3d)) })
            .OrderByDescending(g => g.G3d)
            .ToList();
    }

    internal static (int[] Refs, List<GameSpec> Games) ParseGames(IEnumerable<string> lines)
    {
        int[] refs = Array.Empty<int>();
        var games = new List<GameSpec>(17000);
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("#ref\t", StringComparison.Ordinal))
            {
                refs = line.Split('\t').Skip(1).Select(int.Parse).ToArray();
                continue;
            }
            if (line[0] == '#') continue;

            var f = line.Split('\t');
            if (f.Length < 15) continue;
            var fps = f[14].Split(';').Select(Num).ToArray();
            if (fps.Length != refs.Length || fps.Any(v => v is null)) continue;

            games.Add(new GameSpec
            {
                Name = f[0],
                Year = Int(f[1]),
                SteamAppId = f[2].Length > 0 ? f[2] : null,
                MinGpu = f[3],
                MinCpu = f[4],
                MinRamGb = Num(f[5]),
                RecGpu = f[6],
                RecCpu = f[7],
                RecRamGb = Num(f[8]),
                MinG3d = Int(f[9]),
                RecG3d = Int(f[10]),
                FpsCap = Int(f[11]),
                Popularity = Int(f[12]),
                Slug = f[13],
                ReferenceFps = fps.Select(v => v!.Value).ToArray(),
                SearchKey = TextNormalizer.Normalize(f[0]),
            });
        }
        return (refs, games);
    }

    private static double? Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? Int(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)Math.Round(v) : null;
}

/// <summary>Normalização de texto compartilhada pela busca de jogos e pelo casamento de GPUs.</summary>
public static class TextNormalizer
{
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        bool space = false;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                space = false;
            }
            else if (!space && sb.Length > 0)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
