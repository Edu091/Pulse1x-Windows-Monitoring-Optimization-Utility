using System.Diagnostics;
using System.IO;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.Profiles;

/// <summary>
/// Prioridade, afinidade e gerenciamento dos processos envolvidos numa sessão do GameHub: detectar
/// o processo principal do jogo, ajustá-lo, fechar o que atrapalha antes de jogar e reabrir tudo
/// depois. Nada aqui encerra processos do sistema — só o que o usuário escolheu no perfil.
/// </summary>
public class ProcessControlService
{
    /// <summary>Processos que nunca são fechados, mesmo que apareçam na lista do perfil.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass", "smss",
        "svchost", "dwm", "explorer", "fontdrvhost", "spoolsv", "Registry", "MemCompression",
        "Pulse1x.App",
    };

    // =====================================================================================
    //  Detecção do processo principal
    // =====================================================================================

    /// <summary>
    /// Descobre o processo principal do jogo depois de iniciar. Como launchers (Steam, Epic) trocam
    /// o processo — o .exe chamado apenas pede ao cliente que abra o jogo e sai —, procuramos pelo
    /// processo real com estas pistas, na ordem:
    ///
    ///   1. o nome de processo que já funcionou na última vez (guardado no item da biblioteca);
    ///   2. um processo cujo executável esteja na pasta do jogo;
    ///   3. o processo iniciado por nós, se ainda estiver vivo.
    ///
    /// Entre os candidatos, vence o que usa mais memória — num jogo, é o processo principal.
    /// </summary>
    public async Task<Process?> DetectMainProcessAsync(
        GameEntry game, Process? launched, TimeSpan timeout, CancellationToken token = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? gameDir = SafeDirectory(game.WorkingDirectory, game.Executable);

        while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
        {
            var candidate = FindCandidate(game, launched, gameDir);
            if (candidate is not null) return candidate;

            try { await Task.Delay(1000, token); }
            catch (OperationCanceledException) { return null; }
        }

        return null;
    }

    private static Process? FindCandidate(GameEntry game, Process? launched, string? gameDir)
    {
        // 1) O nome que já deu certo antes.
        if (!string.IsNullOrEmpty(game.KnownProcessName))
        {
            var known = SafeGetByName(game.KnownProcessName).OrderByDescending(SafeMemory).FirstOrDefault();
            if (known is not null) return known;
        }

        // 2) Qualquer processo rodando de dentro da pasta do jogo.
        if (gameDir is not null)
        {
            Process[] all;
            try { all = Process.GetProcesses(); } catch { all = Array.Empty<Process>(); }

            var inDir = all
                .Where(p => !Protected.Contains(p.ProcessName))
                .Where(p => SafePath(p)?.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(SafeMemory)
                .ToList();

            if (inDir.Count > 0) return inDir[0];
        }

        // 3) O processo que nós mesmos iniciamos, se sobreviveu (jogos sem launcher).
        try
        {
            if (launched is { HasExited: false }) return launched;
        }
        catch { }

        return null;
    }

    private static string? SafeDirectory(string workingDirectory, string executable)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                return Path.GetFullPath(workingDirectory).TrimEnd('\\');
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                return Path.GetDirectoryName(Path.GetFullPath(executable))?.TrimEnd('\\');
        }
        catch { }
        return null;
    }

    private static IEnumerable<Process> SafeGetByName(string name)
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)); }
        catch { return Array.Empty<Process>(); }
    }

    private static long SafeMemory(Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }

    private static string? SafePath(Process p)
    {
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    // =====================================================================================
    //  Prioridade e afinidade
    // =====================================================================================

    public bool SetPriority(Process process, Models.GameHub.ProcessPriority priority)
    {
        try
        {
            process.PriorityClass = priority switch
            {
                Models.GameHub.ProcessPriority.Idle => ProcessPriorityClass.Idle,
                Models.GameHub.ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                Models.GameHub.ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                Models.GameHub.ProcessPriority.High => ProcessPriorityClass.High,
                // "Tempo real" pode travar a máquina se o jogo saturar a CPU; o Windows já
                // desaconselha. Mantemos a opção, pois é o usuário quem decide, mas ela só é
                // aplicada explicitamente.
                Models.GameHub.ProcessPriority.Realtime => ProcessPriorityClass.RealTime,
                _ => ProcessPriorityClass.Normal,
            };
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Define a afinidade de CPU. Máscara 0 com <paramref name="reserveFirstCore"/> = todos os
    /// núcleos menos o 0 (deixa o primeiro para o sistema); máscara 0 sem reserva = todos.
    /// </summary>
    public bool SetAffinity(Process process, long mask, bool reserveFirstCore)
    {
        try
        {
            long all = (1L << Environment.ProcessorCount) - 1;
            long effective = mask != 0 ? (mask & all) : (reserveFirstCore ? all & ~1L : all);
            if (effective == 0) return false;
            process.ProcessorAffinity = (IntPtr)effective;
            return true;
        }
        catch { return false; }
    }

    // =====================================================================================
    //  Fechar / abrir aplicativos
    // =====================================================================================

    /// <summary>
    /// Fecha os processos indicados, com educação: primeiro pede o fechamento da janela (para que o
    /// app possa salvar), e só encerra à força quem não responde. Devolve o que foi fechado, com o
    /// caminho do executável, para conseguir reabrir depois.
    /// </summary>
    public async Task<List<ClosedProcessInfo>> CloseProcessesAsync(IEnumerable<string> names)
    {
        var closed = new List<ClosedProcessInfo>();

        foreach (var rawName in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(rawName.Trim());
            if (string.IsNullOrEmpty(name) || Protected.Contains(name)) continue;

            var processes = SafeGetByName(name).ToList();
            if (processes.Count == 0) continue;

            string? path = processes.Select(SafePath).FirstOrDefault(p => p is not null);
            int count = 0;

            foreach (var process in processes)
            {
                try
                {
                    if (process.CloseMainWindow())
                    {
                        if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    count++;
                }
                catch { /* sem permissão ou já encerrado — segue para o próximo */ }
                finally { process.Dispose(); }
            }

            if (count > 0)
                closed.Add(new ClosedProcessInfo { ProcessName = name, ExecutablePath = path, Count = count });
        }

        await Task.CompletedTask;
        return closed;
    }

    /// <summary>Reabre os apps fechados por um perfil (só os que temos o caminho).</summary>
    public void ReopenProcesses(IEnumerable<ClosedProcessInfo> closed)
    {
        foreach (var info in closed)
        {
            if (string.IsNullOrWhiteSpace(info.ExecutablePath) || !File.Exists(info.ExecutablePath)) continue;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = info.ExecutablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(info.ExecutablePath) ?? "",
                });
            }
            catch { /* o app pode exigir elevação ou ter sido desinstalado — apenas ignora */ }
        }
    }

    /// <summary>Abre os aplicativos que o perfil pede junto ao jogo (Discord, overlay etc.).</summary>
    public List<int> StartApps(IEnumerable<LaunchItem> apps)
    {
        var started = new List<int>();
        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.Path)) continue;
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true,
                    WorkingDirectory = SafeDirectory("", app.Path) ?? "",
                });
                if (process is not null && app.CloseOnExit) started.Add(process.Id);
            }
            catch { }
        }
        return started;
    }

    /// <summary>Fecha os aplicativos abertos junto ao jogo, pelos ids guardados no snapshot.</summary>
    public void CloseStartedApps(IEnumerable<int> processIds)
    {
        foreach (int id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (Protected.Contains(process.ProcessName)) continue;
                if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                    process.Kill(entireProcessTree: true);
            }
            catch { /* já fechou */ }
        }
    }

    /// <summary>Executa um comando/script personalizado do perfil e espera terminar (com limite).</summary>
    public async Task RunCommandAsync(string command, int timeoutMs = 30000)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return;
            using var cts = new CancellationTokenSource(timeoutMs);
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { /* comando demorado: segue sem travar a sequência */ }
        }
        catch { }
    }

    /// <summary>Lista os processos com janela visível — alimenta o seletor "fechar antes de jogar".</summary>
    public static IReadOnlyList<(string Name, string? Path)> ListUserProcesses()
    {
        var result = new List<(string, string?)>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        if (Protected.Contains(process.ProcessName)) continue;
                        result.Add((process.ProcessName, SafePath(process)));
                    }
                    catch { }
                }
            }
        }
        catch { }

        return result
            .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
