using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Contrato de descoberta automática de jogos. Cada loja/launcher tem seu próprio formato de
/// catálogo, então cada uma vira um scanner independente — dá para acrescentar uma plataforma nova
/// sem tocar na biblioteca nem na interface.
/// </summary>
public interface ILauncherScanner
{
    LauncherKind Kind { get; }

    /// <summary>O launcher está instalado nesta máquina?</summary>
    bool IsInstalled { get; }

    /// <summary>Jogos instalados encontrados agora (lista vazia quando o launcher não existe).</summary>
    Task<IReadOnlyList<GameEntry>> ScanAsync();
}

// =====================================================================================
//  Steam
// =====================================================================================

/// <summary>
/// Lê o catálogo local da Steam: as pastas de biblioteca em <c>libraryfolders.vdf</c> e, dentro de
/// cada uma, um <c>appmanifest_*.acf</c> por jogo instalado. Nada é baixado nem exige login — é a
/// mesma informação que a própria Steam usa para saber o que está no disco.
/// </summary>
public class SteamScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Steam;

    /// <summary>Pasta de instalação da Steam (null quando não está instalada).</summary>
    public static string? SteamPath
    {
        get
        {
            foreach (var (root, path, value) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key?.GetValue(value) is string dir && Directory.Exists(dir))
                        return dir.Replace('/', '\\');
                }
                catch { }
            }
            return null;
        }
    }

    public bool IsInstalled => SteamPath is not null;

    public Task<IReadOnlyList<GameEntry>> ScanAsync() => Task.Run<IReadOnlyList<GameEntry>>(Scan);

    private static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();
        string? steam = SteamPath;
        if (steam is null) return games;

        foreach (var library in LibraryFolders(steam))
        {
            string appsDir = Path.Combine(library, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            string[] manifests;
            try { manifests = Directory.GetFiles(appsDir, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var manifest in manifests)
            {
                var game = ParseManifest(manifest, appsDir);
                if (game is not null) games.Add(game);
            }
        }

        return games;
    }

    /// <summary>Todas as pastas de biblioteca da Steam (a principal e as de outros discos).</summary>
    private static IEnumerable<string> LibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return folders;

        try
        {
            // O formato VDF é um JSON com aspas e sem vírgulas; só precisamos dos valores de "path".
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)"""))
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    folders.Add(path);
            }
        }
        catch { }

        return folders;
    }

    private static GameEntry? ParseManifest(string manifestPath, string appsDir)
    {
        try
        {
            string content = File.ReadAllText(manifestPath);
            string? appId = VdfValue(content, "appid");
            string? name = VdfValue(content, "name");
            string? installDir = VdfValue(content, "installdir");
            if (appId is null || name is null) return null;

            // Ferramentas e runtimes aparecem como "jogos" no catálogo; não são jogáveis.
            if (name.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase))
                return null;

            string workingDir = installDir is null ? "" : Path.Combine(appsDir, "common", installDir);

            return new GameEntry
            {
                Name = name,
                // A Steam precisa iniciar o jogo (DRM, nuvem, conquistas); chamar o .exe direto
                // quebraria isso. O protocolo steam:// é o caminho oficial.
                Executable = $"steam://rungameid/{appId}",
                WorkingDirectory = Directory.Exists(workingDir) ? workingDir : "",
                Launcher = LauncherKind.Steam,
                LauncherAppId = appId,
                AutoDetected = true,
            };
        }
        catch { return null; }
    }

    private static string? VdfValue(string content, string key)
    {
        var match = Regex.Match(content, $@"""{key}""\s+""([^""]*)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}

// =====================================================================================
//  Epic Games
// =====================================================================================

/// <summary>
/// Lê os manifestos da Epic em <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests</c> — um
/// arquivo .item (JSON) por jogo instalado, com nome, pasta e executável.
/// </summary>
public class EpicScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Epic;

    private static string ManifestsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public bool IsInstalled => Directory.Exists(ManifestsDir);

    public Task<IReadOnlyList<GameEntry>> ScanAsync() => Task.Run<IReadOnlyList<GameEntry>>(Scan);

    private static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();
        if (!Directory.Exists(ManifestsDir)) return games;

        string[] files;
        try { files = Directory.GetFiles(ManifestsDir, "*.item"); }
        catch { return games; }

        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;

                string? name = Text(root, "DisplayName");
                string? appName = Text(root, "AppName");
                string? catalogNamespace = Text(root, "CatalogNamespace");
                string? catalogItemId = Text(root, "CatalogItemId");
                string? installLocation = Text(root, "InstallLocation");
                string? launchExecutable = Text(root, "LaunchExecutable");

                if (name is null || appName is null) continue;

                // Plugins/DLCs não têm executável próprio.
                if (string.IsNullOrEmpty(launchExecutable)) continue;

                string executable = catalogNamespace is not null && catalogItemId is not null
                    // Protocolo oficial da Epic: abre o launcher e inicia o jogo já autenticado.
                    ? $"com.epicgames.launcher://apps/{catalogNamespace}%3A{catalogItemId}%3A{appName}?action=launch&silent=true"
                    : Path.Combine(installLocation ?? "", launchExecutable);

                games.Add(new GameEntry
                {
                    Name = name,
                    Executable = executable,
                    WorkingDirectory = installLocation ?? "",
                    Launcher = LauncherKind.Epic,
                    LauncherAppId = appName,
                    // Guardamos o nome do .exe real: é assim que o perfil acha o processo do jogo
                    // depois, já que quem iniciamos foi o launcher.
                    KnownProcessName = Path.GetFileNameWithoutExtension(launchExecutable),
                    AutoDetected = true,
                });
            }
            catch { /* manifesto corrompido — ignora este e segue */ }
        }

        return games;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

// =====================================================================================
//  GOG
// =====================================================================================

/// <summary>
/// Lê o registro do GOG Galaxy (<c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games</c>), onde cada jogo
/// instalado tem uma subchave com nome, pasta e comando de inicialização.
/// </summary>
public class GogScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Gog;

    private const string Games32 = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    private const string Games64 = @"SOFTWARE\GOG.com\Games";

    public bool IsInstalled
    {
        get
        {
            foreach (var path in new[] { Games32, Games64 })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key is not null && key.GetSubKeyNames().Length > 0) return true;
                }
                catch { }
            }
            return false;
        }
    }

    public Task<IReadOnlyList<GameEntry>> ScanAsync() => Task.Run<IReadOnlyList<GameEntry>>(Scan);

    private static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();

        foreach (var rootPath in new[] { Games32, Games64 })
        {
            RegistryKey? root = null;
            try { root = Registry.LocalMachine.OpenSubKey(rootPath); }
            catch { }
            if (root is null) continue;

            using (root)
            {
                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(subKeyName);
                        if (key is null) continue;

                        string? name = key.GetValue("gameName") as string;
                        string? exe = key.GetValue("exe") as string;
                        string? path = key.GetValue("path") as string;
                        string? gameId = key.GetValue("gameID") as string;

                        if (name is null || exe is null) continue;
                        if (games.Any(g => g.LauncherAppId == gameId)) continue;

                        games.Add(new GameEntry
                        {
                            Name = name,
                            Executable = exe,
                            WorkingDirectory = path ?? Path.GetDirectoryName(exe) ?? "",
                            Launcher = LauncherKind.Gog,
                            LauncherAppId = gameId ?? subKeyName,
                            KnownProcessName = Path.GetFileNameWithoutExtension(exe),
                            AutoDetected = true,
                        });
                    }
                    catch { }
                }
            }
        }

        return games;
    }
}

// =====================================================================================
//  Atalhos (Menu Iniciar / Área de Trabalho)
// =====================================================================================

/// <summary>
/// Varre os atalhos (.lnk) do Menu Iniciar e da Área de Trabalho, resolvendo cada um para o
/// executável de destino. Serve para trazer jogos e aplicativos que não vieram de nenhuma loja.
///
/// Os alvos são lidos pelo Windows Script Host (ligação tardia), o mesmo componente que o Explorer
/// usa — assim não precisamos de nenhuma dependência nova para interpretar o formato .lnk.
/// </summary>
public class ShortcutScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Shortcut;
    public bool IsInstalled => true;

    /// <summary>Atalhos que nunca interessam (desinstaladores, documentação, ferramentas do SO).</summary>
    private static readonly string[] IgnoredFragments =
    {
        "uninstall", "desinstalar", "readme", "leia-me", "manual", "documentation", "support",
        "website", "site oficial", "config", "launcher settings", "crash", "report",
    };

    public Task<IReadOnlyList<GameEntry>> ScanAsync() => Task.Run<IReadOnlyList<GameEntry>>(Scan);

    private static List<GameEntry> Scan()
    {
        var games = new List<GameEntry>();

        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        foreach (var folder in folders.Where(Directory.Exists).Distinct())
        {
            string[] links;
            try { links = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var link in links)
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(link);
                    if (IgnoredFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;

                    var (target, arguments, workingDirectory) = ResolveShortcut(link);
                    if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(target)) continue;

                    // Fora: o próprio Windows e os instaladores/atualizadores.
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (target.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;

                    if (games.Any(g => string.Equals(g.Executable, target, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    games.Add(new GameEntry
                    {
                        Name = name,
                        Executable = target,
                        Arguments = arguments ?? "",
                        WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(target) ?? "",
                        Launcher = LauncherKind.Shortcut,
                        KnownProcessName = Path.GetFileNameWithoutExtension(target),
                        AutoDetected = true,
                    });
                }
                catch { }
            }
        }

        return games;
    }

    /// <summary>Lê alvo, argumentos e pasta de trabalho de um .lnk.</summary>
    public static (string? target, string? arguments, string? workingDirectory) ResolveShortcut(string linkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return (null, null, null);

            shell = Activator.CreateInstance(type);
            if (shell is null) return (null, null, null);

            shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                null, shell, new object[] { linkPath });
            if (shortcut is null) return (null, null, null);

            var shortcutType = shortcut.GetType();
            string? Get(string property) => shortcutType.InvokeMember(property,
                System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;

            return (Get("TargetPath"), Get("Arguments"), Get("WorkingDirectory"));
        }
        catch { return (null, null, null); }
        finally
        {
            if (shortcut is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }
}

// =====================================================================================
//  Pastas personalizadas
// =====================================================================================

/// <summary>
/// Varre pastas indicadas pelo usuário em busca de executáveis — para bibliotecas montadas à mão
/// (jogos portáteis, repacks, projetos próprios). Ignora o ruído típico de pastas de jogo
/// (instaladores, redistribuíveis, utilitários de suporte).
/// </summary>
public class FolderScanner
{
    private static readonly string[] IgnoredNames =
    {
        "unins", "setup", "install", "vcredist", "directx", "dxsetup", "dotnet", "redist",
        "crashreport", "crashhandler", "launcher_helper", "update", "patch", "config",
        "benchmark", "editor", "server", "dedicated",
    };

    /// <summary>Executáveis candidatos a jogo dentro de uma pasta (até 3 níveis de profundidade).</summary>
    public static Task<IReadOnlyList<GameEntry>> ScanAsync(string folder) =>
        Task.Run<IReadOnlyList<GameEntry>>(() => Scan(folder));

    private static List<GameEntry> Scan(string folder)
    {
        var games = new List<GameEntry>();
        if (!Directory.Exists(folder)) return games;

        foreach (var directory in EnumerateDirectories(folder, maxDepth: 3))
        {
            string[] executables;
            try { executables = Directory.GetFiles(directory, "*.exe"); }
            catch { continue; }

            var candidates = executables
                .Where(e => !IgnoredNames.Any(i => Path.GetFileNameWithoutExtension(e).Contains(i, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (candidates.Count == 0) continue;

            // Numa pasta de jogo, o executável principal é quase sempre o maior arquivo.
            string main = candidates.OrderByDescending(e =>
            {
                try { return new FileInfo(e).Length; } catch { return 0L; }
            }).First();

            games.Add(new GameEntry
            {
                Name = Path.GetFileNameWithoutExtension(main),
                Executable = main,
                WorkingDirectory = directory,
                Launcher = LauncherKind.Manual,
                KnownProcessName = Path.GetFileNameWithoutExtension(main),
                AutoDetected = true,
            });
        }

        return games;
    }

    private static IEnumerable<string> EnumerateDirectories(string root, int maxDepth, int depth = 0)
    {
        if (depth > maxDepth) yield break;
        yield return root;

        string[] children;
        try { children = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var child in children)
            foreach (var descendant in EnumerateDirectories(child, maxDepth, depth + 1))
                yield return descendant;
    }
}

// =====================================================================================
//  Emuladores
// =====================================================================================

/// <summary>
/// Transforma as ROMs de um emulador cadastrado em itens normais da biblioteca: mesmo cartão, mesma
/// capa, mesmo botão Jogar. O nome exibido é o do arquivo, limpo das marcações típicas de ROM
/// (região, revisão, tags entre colchetes).
/// </summary>
public class EmulatorScanner
{
    public static Task<IReadOnlyList<GameEntry>> ScanAsync(EmulatorEntry emulator) =>
        Task.Run<IReadOnlyList<GameEntry>>(() => Scan(emulator));

    private static List<GameEntry> Scan(EmulatorEntry emulator)
    {
        var games = new List<GameEntry>();
        if (!emulator.ScanEnabled || !Directory.Exists(emulator.RomsFolder)) return games;

        var extensions = emulator.Extensions
            .Select(e => "." + e.TrimStart('.').ToLowerInvariant())
            .ToHashSet();
        if (extensions.Count == 0) return games;

        string[] files;
        try { files = Directory.GetFiles(emulator.RomsFolder, "*.*", SearchOption.AllDirectories); }
        catch { return games; }

        foreach (var file in files)
        {
            if (!extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

            games.Add(new GameEntry
            {
                Name = CleanRomName(Path.GetFileNameWithoutExtension(file)),
                Executable = emulator.Executable,
                Arguments = emulator.ArgumentsTemplate.Replace("{rom}", file),
                WorkingDirectory = string.IsNullOrWhiteSpace(emulator.Directory)
                    ? Path.GetDirectoryName(emulator.Executable) ?? ""
                    : emulator.Directory,
                Launcher = LauncherKind.Emulator,
                EmulatorId = emulator.Id,
                RomPath = file,
                Category = emulator.Platform,
                ProfileId = emulator.DefaultProfileId,
                KnownProcessName = Path.GetFileNameWithoutExtension(emulator.Executable),
                AutoDetected = true,
            });
        }

        return games;
    }

    /// <summary>"Super Mario World (USA) [!]" vira "Super Mario World".</summary>
    private static string CleanRomName(string raw)
    {
        string name = Regex.Replace(raw, @"\s*[\(\[][^\)\]]*[\)\]]", "");
        name = name.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(name) ? raw : name;
    }
}
