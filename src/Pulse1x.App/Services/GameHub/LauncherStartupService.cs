using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Um launcher que o GameHub sabe abrir sozinho.</summary>
public record LauncherInfo(LauncherKind Kind, string Name, string? ExecutablePath)
{
    public bool IsInstalled => ExecutablePath is not null && File.Exists(ExecutablePath);

    /// <summary>Nome do processo (sem .exe) usado para saber se já está aberto.</summary>
    public string ProcessName => ExecutablePath is null
        ? ""
        : Path.GetFileNameWithoutExtension(ExecutablePath);
}

/// <summary>
/// Abre os launchers escolhidos junto com o GameHub.
///
/// A ideia é ganhar tempo: Steam, Epic e companhia levam vários segundos para subir, e se eles só
/// começarem a abrir no momento em que o usuário aperta Jogar, esse tempo aparece inteiro como
/// espera. Abrindo junto com o hub, quando o jogo for iniciado o cliente já está de pé.
///
/// Nada é aberto duas vezes: se o launcher já estiver rodando, ele é deixado em paz. E tudo é
/// iniciado minimizado/em segundo plano quando o próprio launcher oferece esse parâmetro, para não
/// roubar o foco de quem está navegando pela biblioteca.
/// </summary>
public class LauncherStartupService
{
    /// <summary>Launchers que o Pulse1x sabe localizar nesta máquina.</summary>
    public IReadOnlyList<LauncherInfo> Detect()
    {
        var result = new List<LauncherInfo>
        {
            new(LauncherKind.Steam, "Steam", FindSteam()),
            new(LauncherKind.Epic, "Epic Games", FindEpic()),
            new(LauncherKind.Gog, "GOG Galaxy", FindGog()),
            new(LauncherKind.Ea, "EA App", FindEa()),
            new(LauncherKind.Ubisoft, "Ubisoft Connect", FindUbisoft()),
        };

        return result.Where(l => l.IsInstalled).ToList();
    }

    /// <summary>
    /// Abre os launchers pedidos que ainda não estejam rodando. Devolve quantos foram iniciados.
    /// </summary>
    public int StartMissing(IEnumerable<LauncherKind> wanted)
    {
        var installed = Detect().ToDictionary(l => l.Kind);
        int started = 0;

        foreach (var kind in wanted.Distinct())
        {
            if (!installed.TryGetValue(kind, out var launcher)) continue;
            if (IsRunning(launcher)) continue;

            if (Start(launcher)) started++;
        }

        return started;
    }

    public static bool IsRunning(LauncherInfo launcher)
    {
        if (string.IsNullOrEmpty(launcher.ProcessName)) return false;
        try { return Process.GetProcessesByName(launcher.ProcessName).Length > 0; }
        catch { return false; }
    }

    private static bool Start(LauncherInfo launcher)
    {
        if (launcher.ExecutablePath is null) return false;

        try
        {
            // Onde o launcher aceita, pedimos que ele suba em segundo plano — o usuário está
            // olhando a biblioteca, não quer a janela da loja por cima.
            string arguments = launcher.Kind switch
            {
                LauncherKind.Steam => "-silent",
                LauncherKind.Epic => "-silent",
                _ => "",
            };

            Process.Start(new ProcessStartInfo
            {
                FileName = launcher.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(launcher.ExecutablePath) ?? "",
            });
            return true;
        }
        catch { return false; }
    }

    // =====================================================================================
    //  Localização de cada launcher
    // =====================================================================================

    private static string? FindSteam()
    {
        string? path = SteamScanner.SteamPath;
        if (path is null) return null;
        string exe = Path.Combine(path, "steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static string? FindEpic()
    {
        foreach (var candidate in new[]
        {
            @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe",
            @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
            @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return ReadRegistryPath(@"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher", "AppPath");
    }

    private static string? FindGog()
    {
        string? path = ReadRegistryPath(@"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client");
        if (path is null) return null;
        string exe = Path.Combine(path, "GalaxyClient.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static string? FindEa()
    {
        foreach (var candidate in new[]
        {
            @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
            @"C:\Program Files (x86)\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
            @"C:\Program Files (x86)\Origin\Origin.exe",
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindUbisoft()
    {
        string? path = ReadRegistryPath(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir");
        if (path is null) return null;
        string exe = Path.Combine(path, "upc.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Lê um caminho do registro, tolerando chave/valor ausentes.</summary>
    private static string? ReadRegistryPath(string subKey, string valueName)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    string cleaned = value.Trim('"');
                    if (File.Exists(cleaned) || Directory.Exists(cleaned)) return cleaned;
                }
            }
            catch { }
        }
        return null;
    }
}
