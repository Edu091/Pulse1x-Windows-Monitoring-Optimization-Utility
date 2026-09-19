using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Pulse1x.App.Models.WinCustom;

namespace Pulse1x.App.Services.WinCustom;

/// <summary>
/// Guarda os temas de Personalização do Windows em disco e oferece as operações de biblioteca:
/// criar, salvar, renomear, duplicar, excluir, importar e exportar.
///
/// Os temas ficam em %APPDATA%\Pulse1x\win-themes\ — um JSON por tema, e não um arquivo único,
/// para que um tema corrompido nunca leve a biblioteca inteira junto. As imagens usadas são
/// copiadas para uma pasta própria do tema na importação, de modo que um tema importado não
/// dependa de arquivos que o usuário pode mover ou apagar.
/// </summary>
public class WinThemeStore
{
    private readonly string _dir;
    private readonly string _imagesDir;
    private readonly List<WinTheme> _themes = new();
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WinThemeStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        _dir = Path.Combine(root, "win-themes");
        _imagesDir = Path.Combine(root, "win-theme-images");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_imagesDir);
        Load();
    }

    /// <summary>Todos os temas — presets de fábrica primeiro, depois os do usuário por nome.</summary>
    public IReadOnlyList<WinTheme> Themes
    {
        get
        {
            lock (_gate)
                return _themes
                    .OrderByDescending(t => t.IsBuiltIn)
                    .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
        }
    }

    public WinTheme? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_gate) return _themes.FirstOrDefault(t => t.Id == id);
    }

    // =====================================================================================
    //  Carregamento
    // =====================================================================================

    private void Load()
    {
        lock (_gate)
        {
            _themes.Clear();

            foreach (var file in SafeEnumerate(_dir, "*.json"))
            {
                try
                {
                    var theme = JsonSerializer.Deserialize<WinTheme>(File.ReadAllText(file));
                    if (theme is not null && !string.IsNullOrEmpty(theme.Id))
                        _themes.Add(theme);
                }
                catch
                {
                    // Um tema ilegível é ignorado — os demais continuam disponíveis.
                }
            }

            // Os presets de fábrica são recriados quando faltarem, para que uma exclusão
            // acidental (ou um arquivo corrompido) não deixe o usuário sem ponto de partida.
            foreach (var preset in WinThemePresets.BuildAll())
            {
                if (_themes.Any(t => t.IsBuiltIn && t.Name == preset.Name)) continue;
                _themes.Add(preset);
                SaveToDisk(preset);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    // =====================================================================================
    //  Operações da biblioteca
    // =====================================================================================

    /// <summary>Cria um tema novo, partindo do padrão do Windows (nada aplicado).</summary>
    public WinTheme Create(string name)
    {
        var theme = new WinTheme { Name = Deduplicate(name) };
        lock (_gate) _themes.Add(theme);
        SaveToDisk(theme);
        return theme;
    }

    /// <summary>Persiste as alterações de um tema já existente.</summary>
    public void Save(WinTheme theme)
    {
        theme.ModifiedAt = DateTime.Now;
        SaveToDisk(theme);
    }

    /// <summary>
    /// Renomeia. Presets de fábrica não podem ser renomeados — a interface oferece "Duplicar"
    /// no lugar, para o usuário ter uma cópia própria editável.
    /// </summary>
    public bool Rename(WinTheme theme, string newName)
    {
        if (theme.IsBuiltIn || string.IsNullOrWhiteSpace(newName)) return false;
        theme.Name = Deduplicate(newName.Trim(), exceptId: theme.Id);
        Save(theme);
        return true;
    }

    public WinTheme Duplicate(WinTheme theme)
    {
        var copy = theme.Duplicate(Deduplicate($"{theme.Name} (cópia)"));
        lock (_gate) _themes.Add(copy);
        SaveToDisk(copy);
        return copy;
    }

    /// <summary>Exclui um tema do usuário. Presets de fábrica são preservados.</summary>
    public bool Delete(WinTheme theme)
    {
        if (theme.IsBuiltIn) return false;

        lock (_gate) _themes.Remove(theme);
        try { File.Delete(PathFor(theme)); } catch { /* já não existe: tudo certo */ }

        // Imagens copiadas para dentro do tema saem junto — não deixamos resíduo.
        try
        {
            var dir = ImageDirFor(theme.Id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* melhor esforço */ }

        return true;
    }

    // =====================================================================================
    //  Importar / exportar
    // =====================================================================================

    /// <summary>
    /// Exporta o tema como um .pulsetheme (um zip) contendo o JSON e TODAS as imagens usadas.
    /// Assim o arquivo é autossuficiente: pode ir para outra máquina e continuar funcionando.
    /// </summary>
    public void Export(WinTheme theme, string destinationPath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "pulse-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            // O tema exportado aponta para caminhos RELATIVOS dentro do pacote; sem isso, a
            // máquina de destino procuraria a imagem no caminho original, que não existe lá.
            var portable = theme.Duplicate(theme.Name);
            portable.Id = theme.Id;
            portable.IsBuiltIn = false;

            string imagesDir = Path.Combine(staging, "images");
            Directory.CreateDirectory(imagesDir);

            int index = 0;
            foreach (var appearance in portable.AllAppearances())
            {
                string? source = appearance.ImagePath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;

                string name = $"img{index++}{Path.GetExtension(source)}";
                try
                {
                    File.Copy(source, Path.Combine(imagesDir, name), overwrite: true);
                    appearance.ImagePath = "images/" + name;
                }
                catch
                {
                    // Imagem ilegível: o tema ainda vale, apenas sem aquele plano de fundo.
                    appearance.ImagePath = null;
                }
            }

            File.WriteAllText(Path.Combine(staging, "theme.json"),
                JsonSerializer.Serialize(portable, JsonOptions));

            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            ZipFile.CreateFromDirectory(staging, destinationPath);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Importa um .pulsetheme. As imagens são copiadas para a pasta do Pulse e os caminhos
    /// reescritos para absolutos, deixando o tema independente do arquivo importado.
    /// </summary>
    public WinTheme? Import(string packagePath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "pulse-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(packagePath, staging);

            string json = Path.Combine(staging, "theme.json");
            if (!File.Exists(json)) return null;

            var theme = JsonSerializer.Deserialize<WinTheme>(File.ReadAllText(json));
            if (theme is null) return null;

            // Id novo: importar duas vezes cria dois temas em vez de sobrescrever o primeiro.
            theme.Id = Guid.NewGuid().ToString("N");
            theme.IsBuiltIn = false;
            theme.Name = Deduplicate(string.IsNullOrWhiteSpace(theme.Name) ? "Tema importado" : theme.Name);

            string targetImages = ImageDirFor(theme.Id);
            Directory.CreateDirectory(targetImages);

            foreach (var appearance in theme.AllAppearances())
            {
                string? relative = appearance.ImagePath;
                if (string.IsNullOrWhiteSpace(relative)) continue;

                // Só aceitamos caminhos relativos de dentro do pacote. Um .pulsetheme adulterado
                // poderia trazer "C:\..." ou "..\..\" para nos fazer ler outro lugar do disco.
                string candidate = Path.GetFullPath(Path.Combine(staging, relative));
                if (!candidate.StartsWith(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(candidate))
                {
                    appearance.ImagePath = null;
                    continue;
                }

                string destination = Path.Combine(targetImages, Path.GetFileName(candidate));
                try
                {
                    File.Copy(candidate, destination, overwrite: true);
                    appearance.ImagePath = destination;
                }
                catch { appearance.ImagePath = null; }
            }

            lock (_gate) _themes.Add(theme);
            SaveToDisk(theme);
            return theme;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    // =====================================================================================
    //  Disco
    // =====================================================================================

    private string PathFor(WinTheme theme) => Path.Combine(_dir, theme.Id + ".json");

    private string ImageDirFor(string themeId) => Path.Combine(_imagesDir, themeId);

    private void SaveToDisk(WinTheme theme)
    {
        try
        {
            File.WriteAllText(PathFor(theme), JsonSerializer.Serialize(theme, JsonOptions));
        }
        catch
        {
            // Sem permissão/disco cheio: o tema continua válido em memória nesta sessão.
        }
    }

    /// <summary>Garante um nome único na biblioteca, acrescentando (2), (3)... quando preciso.</summary>
    private string Deduplicate(string name, string? exceptId = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Novo tema" : name.Trim();

        lock (_gate)
        {
            bool Taken(string candidate) => _themes.Any(t =>
                t.Id != exceptId && string.Equals(t.Name, candidate, StringComparison.CurrentCultureIgnoreCase));

            if (!Taken(name)) return name;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = $"{name} ({i})";
                if (!Taken(candidate)) return candidate;
            }
            return $"{name} ({Guid.NewGuid():N})";
        }
    }
}
