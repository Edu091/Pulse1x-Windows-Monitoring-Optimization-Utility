namespace Pulse1x.App.Services.FpsInGame;

/// <summary>
/// Busca de jogos tolerante ao jeito que as pessoas digitam: siglas ("gta", "rdr2", "bg3"),
/// números romanos ou arábicos ("gta 5" = "GTA V"), pedaços de palavra ("theft", "yberpunk"),
/// palavras coladas ("counterstrike") e erros de digitação ("cyberpnuk", "witchr").
/// Cada palavra digitada precisa casar com o título de algum jeito; o tipo de casamento define a
/// pontuação, e os mais fortes vêm primeiro.
/// </summary>
public sealed class GameSearch
{
    private sealed record Entry(GameSpec Game, string Key, string[] Words, string Compact, HashSet<string> Acronyms);

    private readonly List<Entry> _entries;

    public GameSearch(IEnumerable<GameSpec> games)
    {
        _entries = games.Select(BuildEntry).ToList();
    }

    /// <summary>Jogos que casam com a busca, do melhor para o pior casamento.</summary>
    public List<GameSpec> Search(string query, Func<GameSpec, bool>? filter = null)
    {
        string normalized = Normalize(query);
        if (normalized.Length == 0) return new List<GameSpec>();
        var tokens = normalized.Split(' ');

        var hits = new List<(GameSpec Game, double Score)>();
        foreach (var entry in _entries)
        {
            if (filter is not null && !filter(entry.Game)) continue;

            double total = 0;
            foreach (var token in tokens)
            {
                double s = TokenScore(token, entry);
                if (s <= 0) { total = -1; break; }
                total += s;
            }
            if (total <= 0) continue;

            if (entry.Key == normalized) total += 10;
            else if (entry.Key.StartsWith(normalized, StringComparison.Ordinal)) total += 4;
            hits.Add((entry.Game, total));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Game.Popularity ?? int.MaxValue)
            .ThenBy(h => h.Game.Name, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Game)
            .ToList();
    }

    private static double TokenScore(string token, Entry e)
    {
        if (e.Words.Contains(token)) return 4;
        if (e.Acronyms.Contains(token)) return 3.5;
        if (e.Words.Any(w => w.StartsWith(token, StringComparison.Ordinal))) return 3;
        // Pedaço no meio da palavra: quem pulou a primeira letra ("yberpunk", "rand theft").
        if (token.Length >= 3 && e.Key.Contains(token, StringComparison.Ordinal)) return 2;
        // Palavras coladas ou separadas de outro jeito ("counterstrike", "lefthand").
        if (token.Length >= 4 && e.Compact.Contains(token, StringComparison.Ordinal)) return 1.5;
        if (token.Length >= 4 && !token.All(char.IsDigit))
        {
            int maxEdits = token.Length >= 8 ? 2 : 1;
            foreach (var w in e.Words)
            {
                if (w.Length < 3) continue;
                if (Distance(token, w) <= maxEdits) return 1.2;
                // Digitando o começo da palavra com um erro ("cyberpn" de "cyberpunk").
                if (w.Length > token.Length && Distance(token, w[..token.Length]) <= maxEdits) return 1;
            }
        }
        return 0;
    }

    private static Entry BuildEntry(GameSpec game)
    {
        string key = Normalize(game.Name);
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Entry(game, key, words, key.Replace(" ", ""), Acronyms(words));
    }

    /// <summary>
    /// Siglas de cada sequência de palavras do título: a inicial de cada palavra, e números inteiros
    /// ("grand theft auto 5" -> "gta", "gta5"; "baldurs gate 3" -> "bg3"; "the last of us" -> "tlou").
    /// </summary>
    internal static HashSet<string> Acronyms(string[] words)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < words.Length; i++)
        {
            var acc = new System.Text.StringBuilder();
            for (int j = i; j < words.Length && j < i + 7; j++)
            {
                var w = words[j];
                if (w.Length == 0) continue;
                acc.Append(w.All(char.IsDigit) ? w : w[..1]);
                if (j > i) set.Add(acc.ToString());
            }
        }
        return set;
    }

    private static readonly Dictionary<string, string> Roman = new(StringComparer.Ordinal)
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6",
        ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    /// <summary>
    /// Minúsculas, sem acento, apóstrofo colado ("baldur's" -> "baldurs") e números romanos soltos
    /// viram arábicos, para "GTA V" e "gta 5" serem a mesma coisa.
    /// </summary>
    public static string Normalize(string text)
    {
        var cleaned = text.Replace("'", "").Replace("’", "").Replace("`", "");
        var words = TextNormalizer.Normalize(cleaned).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            if (Roman.TryGetValue(words[i], out var arabic)) words[i] = arabic;
        return string.Join(' ', words);
    }

    /// <summary>Distância de edição com transposição (Damerau, versão OSA): "cyberpnuk" -> 1.</summary>
    internal static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 3;
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
            }
        }
        return d[a.Length, b.Length];
    }
}
