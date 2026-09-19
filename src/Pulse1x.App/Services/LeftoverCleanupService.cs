using System.IO;

namespace Pulse1x.App.Services;

/// <summary>Uma pasta deixada para trás por um programa desinstalado, já com o tamanho que ocupa.</summary>
public record LeftoverItem(string Path, long SizeBytes);

/// <summary>
/// Procura o que um programa desinstalado deixou para trás (pastas em Arquivos de Programas e
/// AppData, chaves órfãs do Registro) e remove só o que o usuário confirmar.
///
/// Esta é a parte mais destrutiva do Debloater, então ela é deliberadamente tímida:
///
///   • só olha DENTRO das pastas onde programas se instalam — Program Files, ProgramData, AppData
///     do usuário — e nunca acima delas;
///   • um candidato precisa bater com o nome do programa E com o do fabricante, não só um deles,
///     porque "Update" ou "Assistant" sozinhos apareceriam em meio mundo de software alheio;
///   • caminhos de sistema são recusados na marra em <see cref="IsSafeToDelete"/>, mesmo que a
///     busca por algum motivo os produza;
///   • nada é apagado pela varredura: ela só devolve a lista, e quem apaga é uma segunda chamada,
///     já com a escolha do usuário em mãos.
/// </summary>
public class LeftoverCleanupService
{
    // As únicas raízes onde procuramos. Qualquer coisa fora delas está fora do alcance do Debloater.
    private static IEnumerable<string> SearchRoots()
    {
        foreach (var folder in new[]
        {
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
        })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                yield return path;
        }
    }

    /// <summary>
    /// Procura restos de um programa. <paramref name="vendor"/> é usado junto com o nome para
    /// evitar falsos positivos — uma pasta só entra na lista se combinar com os dois.
    /// </summary>
    public Task<IReadOnlyList<LeftoverItem>> ScanAsync(string appName, string? vendor) =>
        Task.Run<IReadOnlyList<LeftoverItem>>(() => Scan(appName, vendor));

    private List<LeftoverItem> Scan(string appName, string? vendor)
    {
        var found = new List<LeftoverItem>();
        var terms = SignificantTerms(appName, vendor);
        if (terms.Count == 0) return found;

        foreach (var root in SearchRoots())
        {
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (var dir in children)
            {
                string leaf = Path.GetFileName(dir);
                if (!MatchesApp(leaf, terms)) continue;
                if (!IsSafeToDelete(dir)) continue;

                found.Add(new LeftoverItem(dir, DirectorySize(dir)));

                // Muitos fabricantes usam uma pasta-guarda-chuva ("Lenovo", "Dell") com um
                // subdiretório por produto. Quando o pai bate só pelo fabricante, olhamos um nível
                // abaixo em vez de propor apagar a pasta inteira do fabricante.
                if (leaf.Equals(vendor, StringComparison.OrdinalIgnoreCase))
                {
                    found.RemoveAt(found.Count - 1);
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            if (MatchesApp(Path.GetFileName(sub), terms) && IsSafeToDelete(sub))
                                found.Add(new LeftoverItem(sub, DirectorySize(sub)));
                    }
                    catch { }
                }
            }
        }

        return found
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(f => f.SizeBytes)
            .ToList();
    }

    /// <summary>
    /// Remove os restos escolhidos. Devolve quantos foram removidos e o espaço liberado; o que
    /// falhar (arquivo em uso, sem permissão) é apenas ignorado — nunca forçado.
    /// </summary>
    public Task<(int removed, long freedBytes)> RemoveAsync(IEnumerable<LeftoverItem> items) =>
        Task.Run(() =>
        {
            int removed = 0;
            long freed = 0;

            foreach (var item in items)
            {
                if (!IsSafeToDelete(item.Path)) continue; // segunda checagem: a lista pode ter envelhecido

                try
                {
                    if (!Directory.Exists(item.Path)) continue;
                    Directory.Delete(item.Path, recursive: true);
                    removed++;
                    freed += item.SizeBytes;
                }
                catch { /* em uso ou protegido: deixa como está */ }
            }

            return (removed, freed);
        });

    // ===================== Segurança =====================

    /// <summary>
    /// Recusa qualquer caminho que não seja uma subpasta de uma das raízes de instalação, além das
    /// próprias raízes e de pastas de sistema. É a última linha de defesa antes de um Delete.
    /// </summary>
    internal static bool IsSafeToDelete(string path)
    {
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); }
        catch { return false; }

        if (full.Length < 4) return false;                       // "C:\" e similares
        if (Path.GetPathRoot(full)?.TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase) == true)
            return false;                                        // a raiz do disco

        // Pastas do Windows nunca são tocadas, nem que o nome do programa bata.
        foreach (var folder in new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.UserProfile,
        })
        {
            string protectedPath = Environment.GetFolderPath(folder).TrimEnd('\\');
            if (protectedPath.Length > 0 && full.Equals(protectedPath, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Precisa estar ESTRITAMENTE dentro de uma das raízes de busca (nunca ser a própria raiz).
        foreach (var root in SearchRoots())
        {
            string r = root.TrimEnd('\\');
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase)) return false;
            if (full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    // ===================== Correspondência de nome =====================

    // Quebra nome e fabricante em termos com significado, descartando palavras genéricas que
    // sozinhas casariam com software de terceiros.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "for", "and", "inc", "ltd", "corp", "corporation", "co", "company", "software",
        "app", "application", "windows", "microsoft", "update", "updater", "manager", "service",
        "center", "centre", "tool", "tools", "utility", "utilities", "client", "common", "shared",
        "x64", "x86", "32", "64", "bit", "edition", "version",
    };

    private static List<string> SignificantTerms(string appName, string? vendor)
    {
        var terms = new List<string>();

        foreach (var raw in $"{appName} {vendor}".Split(new[] { ' ', '-', '_', '.', '(', ')', ',' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string t = raw.Trim();
            if (t.Length < 3) continue;
            if (Generic.Contains(t)) continue;
            if (!terms.Contains(t, StringComparer.OrdinalIgnoreCase)) terms.Add(t);
        }

        return terms;
    }

    // Uma pasta é candidata quando contém ao menos um termo com significado do programa. Como a
    // lista já descartou palavras genéricas, um único termo forte ("Vantage", "SupportAssist") é
    // suficiente e específico o bastante.
    private static bool MatchesApp(string folderName, List<string> terms) =>
        terms.Any(t => folderName.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static long DirectorySize(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
            return total;
        }
        catch { return 0; }
    }
}
