using System.IO;
using Microsoft.Win32;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

// =====================================================================================
//  EA (EA App / Origin)
// =====================================================================================

/// <summary>
/// Lê os jogos da EA pelo registro. A EA registra cada jogo instalado em
/// <c>HKLM\SOFTWARE\(WOW6432Node\)EA Games\{título}</c>, com o nome de exibição e a pasta de
/// instalação; o catálogo antigo do Origin fica em <c>Origin Games\{id}</c>.
///
/// Só entram jogos com pasta existente: o registro guarda entradas de jogos já desinstalados, e
/// trazer um item que não abre seria pior do que não trazer nada.
/// </summary>
public class EaScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Ea;

    private static readonly (RegistryKey Root, string Path)[] Locations =
    {
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\EA Games"),
        (Registry.LocalMachine, @"SOFTWARE\EA Games"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Origin Games"),
        (Registry.LocalMachine, @"SOFTWARE\Origin Games"),
    };

    public bool IsInstalled
    {
        get
        {
            foreach (var (root, path) in Locations)
            {
                try
                {
                    using var key = root.OpenSubKey(path);
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

        foreach (var (root, path) in Locations)
        {
            RegistryKey? parent = null;
            try { parent = root.OpenSubKey(path); }
            catch { }
            if (parent is null) continue;

            using (parent)
            {
                foreach (var subKeyName in parent.GetSubKeyNames())
                {
                    try
                    {
                        using var key = parent.OpenSubKey(subKeyName);
                        if (key is null) continue;

                        string name = (key.GetValue("DisplayName") as string ?? subKeyName).Trim();
                        // O DisplayName às vezes vem com o nome em duas linhas; só a primeira serve.
                        name = name.Split('\n', '\r').First().Trim();

                        string? installDir = key.GetValue("Install Dir") as string
                                             ?? key.GetValue("InstallDir") as string
                                             ?? key.GetValue("InstallLocation") as string;

                        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) continue;
                        if (games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

                        string? exe = FindMainExecutable(installDir, name);
                        if (exe is null) continue;

                        games.Add(new GameEntry
                        {
                            Name = name,
                            Executable = exe,
                            WorkingDirectory = installDir.TrimEnd('\\'),
                            Launcher = LauncherKind.Ea,
                            LauncherAppId = subKeyName,
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

    /// <summary>
    /// Escolhe o executável principal de uma pasta de jogo: prefere o que lembra o nome do jogo e,
    /// na dúvida, o maior arquivo — que numa instalação de jogo é quase sempre o binário principal.
    /// </summary>
    internal static string? FindMainExecutable(string directory, string gameName)
    {
        try
        {
            var executables = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(e => !IsNoise(Path.GetFileNameWithoutExtension(e)))
                .ToList();

            if (executables.Count == 0)
            {
                // Alguns jogos guardam o binário numa subpasta (bin, Binaries, retail).
                executables = Directory.GetDirectories(directory)
                    .SelectMany(d =>
                    {
                        try { return Directory.GetFiles(d, "*.exe", SearchOption.TopDirectoryOnly); }
                        catch { return Array.Empty<string>(); }
                    })
                    .Where(e => !IsNoise(Path.GetFileNameWithoutExtension(e)))
                    .ToList();
            }

            if (executables.Count == 0) return null;

            string[] words = gameName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var byName = executables
                .OrderByDescending(e => words.Count(w =>
                    Path.GetFileNameWithoutExtension(e).Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(e => { try { return new FileInfo(e).Length; } catch { return 0L; } })
                .First();

            return byName;
        }
        catch { return null; }
    }

    private static readonly string[] NoiseNames =
    {
        "unins", "setup", "install", "vcredist", "directx", "dxsetup", "dotnet", "redist",
        "crashreport", "crashhandler", "activation", "cleanup", "touchup", "helper",
    };

    internal static bool IsNoise(string fileName) =>
        NoiseNames.Any(n => fileName.Contains(n, StringComparison.OrdinalIgnoreCase));
}

// =====================================================================================
//  Ubisoft Connect
// =====================================================================================

/// <summary>
/// Lê os jogos do Ubisoft Connect em <c>HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}</c>,
/// onde cada subchave é o id do jogo e traz a pasta instalada. O nome não está no registro, então é
/// derivado da pasta; o jogo é iniciado pelo protocolo oficial <c>uplay://launch/{id}/0</c>, para o
/// cliente cuidar de autenticação e sincronização.
/// </summary>
public class UbisoftScanner : ILauncherScanner
{
    public LauncherKind Kind => LauncherKind.Ubisoft;

    private const string InstallsPath = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    private const string InstallsPath64 = @"SOFTWARE\Ubisoft\Launcher\Installs";

    public bool IsInstalled
    {
        get
        {
            foreach (var path in new[] { InstallsPath, InstallsPath64 })
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

        foreach (var path in new[] { InstallsPath, InstallsPath64 })
        {
            RegistryKey? parent = null;
            try { parent = Registry.LocalMachine.OpenSubKey(path); }
            catch { }
            if (parent is null) continue;

            using (parent)
            {
                foreach (var gameId in parent.GetSubKeyNames())
                {
                    try
                    {
                        using var key = parent.OpenSubKey(gameId);
                        string? installDir = key?.GetValue("InstallDir") as string;
                        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) continue;
                        if (games.Any(g => g.LauncherAppId == gameId)) continue;

                        string folder = new DirectoryInfo(installDir.TrimEnd('\\', '/')).Name;
                        string? exe = EaScanner.FindMainExecutable(installDir, folder);

                        games.Add(new GameEntry
                        {
                            Name = folder.Replace('_', ' ').Trim(),
                            // Protocolo oficial: o Ubisoft Connect precisa iniciar o jogo.
                            Executable = $"uplay://launch/{gameId}/0",
                            WorkingDirectory = installDir.TrimEnd('\\'),
                            Launcher = LauncherKind.Ubisoft,
                            LauncherAppId = gameId,
                            // Guardamos o .exe real só para reconhecer o processo do jogo depois.
                            KnownProcessName = exe is null ? null : Path.GetFileNameWithoutExtension(exe),
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
