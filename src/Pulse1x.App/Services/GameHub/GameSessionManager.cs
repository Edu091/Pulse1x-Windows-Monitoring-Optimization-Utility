using System.Diagnostics;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Uma sessão de jogo em andamento.</summary>
public class GameSession
{
    public required GameEntry Game { get; init; }
    public GameProfile? Profile { get; init; }
    public required SystemSnapshot Snapshot { get; init; }
    public DateTime StartedAt { get; } = DateTime.Now;
    public Process? MainProcess { get; set; }

    /// <summary>Nome do último processo principal, lido enquanto ele ainda estava vivo — depois que
    /// sai, <see cref="Process.ProcessName"/> de um processo iniciado por nós lança exceção.</summary>
    public string? MainProcessName { get; set; }

    public TimeSpan Duration => DateTime.Now - StartedAt;
}

/// <summary>
/// Conduz uma sessão do começo ao fim: aplica o perfil, inicia o jogo, encontra o processo
/// principal, ajusta prioridade/afinidade, liga o Modo Gaming, espera o jogo fechar e então
/// restaura tudo e contabiliza o tempo jogado.
///
/// Também é quem cuida da recuperação: se o Pulse1x for encerrado com uma sessão em andamento, na
/// próxima abertura <see cref="RecoverPendingAsync"/> encontra o snapshot em disco e desfaz as
/// alterações que ficaram para trás.
/// </summary>
public class GameSessionManager
{
    private readonly GameProfileEngine _engine;
    private readonly GameLibraryService _library;
    private readonly ProfileStoreService _profiles;
    private readonly SnapshotService _snapshots;
    private readonly GamingModeService _gamingMode;
    private readonly PlayMetricsService? _metrics;
    private readonly FpsMonitorService? _fps;
    private readonly SessionTelemetryService? _telemetry;

    private CancellationTokenSource? _monitorCts;

    public GameSession? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary>Disparado quando uma sessão começa e quando termina (sempre na thread de UI).</summary>
    public event Action<GameSession>? SessionStarted;
    public event Action<GameSession>? SessionEnded;

    /// <summary>Andamento das etapas, para a interface mostrar o que o perfil está fazendo.</summary>
    public event Action<ProfileStepProgress>? StepReported;

    public GameSessionManager(
        GameProfileEngine engine,
        GameLibraryService library,
        ProfileStoreService profiles,
        SnapshotService snapshots,
        GamingModeService gamingMode,
        PlayMetricsService? metrics = null,
        FpsMonitorService? fps = null,
        SessionTelemetryService? telemetry = null)
    {
        _engine = engine;
        _library = library;
        _profiles = profiles;
        _snapshots = snapshots;
        _gamingMode = gamingMode;
        _metrics = metrics;
        _fps = fps;
        _telemetry = telemetry;
    }

    // =====================================================================================
    //  Início
    // =====================================================================================

    /// <summary>
    /// Inicia um item da biblioteca com o perfil associado. Só uma sessão por vez: iniciar outro
    /// jogo com um já em andamento não faria sentido (os perfis se sobreporiam e a restauração
    /// ficaria ambígua).
    /// </summary>
    public async Task<bool> StartAsync(GameEntry game)
    {
        if (IsRunning) return false;

        var profile = _profiles.Find(game.ProfileId);
        var progress = new Progress<ProfileStepProgress>(step => StepReported?.Invoke(step));

        var (snapshot, launched) = await _engine.ApplyAsync(game, profile, progress);

        var session = new GameSession
        {
            Game = game,
            Profile = profile,
            Snapshot = snapshot,
            MainProcess = launched,
        };
        Current = session;
        SessionStarted?.Invoke(session);

        if (profile?.GamingModeWhileRunning != false)
            _gamingMode.Enter(game.Name);

        // O monitoramento roda em segundo plano: a interface volta a responder imediatamente.
        _monitorCts = new CancellationTokenSource();
        _ = MonitorAsync(session, launched, _monitorCts.Token);

        return true;
    }

    // =====================================================================================
    //  Acompanhamento
    // =====================================================================================

    private async Task MonitorAsync(GameSession session, Process? launched, CancellationToken token)
    {
        try
        {
            // Encontrar o processo real pode levar um tempo: launchers como Steam e Epic abrem o
            // cliente primeiro e só depois o jogo. Damos dois minutos antes de desistir.
            var main = await _engine.Processes.DetectMainProcessAsync(
                session.Game, launched, TimeSpan.FromMinutes(2), token);

            if (main is not null)
            {
                var since = Track(session, main);
                _engine.AttachToProcess(session.Profile, main,
                    new Progress<ProfileStepProgress>(step => StepReported?.Invoke(step)));

                // Com o processo do jogo em mãos, começa a medir o desempenho da sessão.
                if (_metrics?.Enabled == true)
                {
                    var options = _metrics.Options;
                    if (options.Fps) _fps?.Start(main);
                    _telemetry?.Start(options);
                }

                // O processo achado pode ser só um intermediário (anti-cheat, "Launcher.exe") que sai
                // assim que o jogo de verdade abre. Antes a sessão acabava ali, no meio da partida:
                // o perfil era desfeito e o tempo jogado virava segundos. Quando o observado sai,
                // procuramos por alguns segundos um sucessor na pasta do jogo antes de encerrar.
                // Só vale para quem viveu pouco: um intermediário sai logo depois de abrir o jogo;
                // um jogo que rodou a partida inteira e fechou encerra a sessão na hora, sem
                // atrasar a restauração do perfil em 15 s a cada saída normal.
                var current = main;
                for (int hop = 0; hop < 5; hop++)
                {
                    try { await current.WaitForExitAsync(token); }
                    catch (OperationCanceledException) { return; }

                    if (!ShouldLookForSuccessor(DateTime.Now - since)) break;

                    var next = await _engine.Processes.DetectMainProcessAsync(
                        session.Game, launched: null, TimeSpan.FromSeconds(15), token);
                    if (next is null || next.Id == current.Id) break;

                    current = next;
                    since = Track(session, next);
                    _engine.AttachToProcess(session.Profile, next,
                        new Progress<ProfileStepProgress>(step => StepReported?.Invoke(step)));
                    if (_metrics?.Enabled == true && _metrics.Options.Fps) _fps?.Start(next);
                }
            }
            else if (launched is not null)
            {
                try { await launched.WaitForExitAsync(token); }
                catch (OperationCanceledException) { return; }
            }
            else
            {
                // Sem processo para observar (o launcher assumiu e saiu): encerramos a sessão em
                // vez de deixar o sistema preso no perfil para sempre.
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) { return; }
        catch { /* qualquer falha no acompanhamento termina a sessão e restaura — nunca deixa preso */ }

        if (!token.IsCancellationRequested)
            await EndAsync(session);
    }

    /// <summary>Até quanto tempo de vida um processo que saiu ainda pode ser só um intermediário
    /// (start_protected_game.exe do EAC, "Launcher.exe"), que fecha pouco depois de abrir o jogo.</summary>
    public static readonly TimeSpan BootstrapperMaxLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Vale procurar um sucessor quando o processo observado viveu pouco.</summary>
    public static bool ShouldLookForSuccessor(TimeSpan lifetime) => lifetime < BootstrapperMaxLifetime;

    /// <summary>
    /// Passa a observar <paramref name="process"/>: guarda o nome (ainda vivo) e devolve desde
    /// quando ele existe — o início real do processo quando dá para ler, senão agora.
    /// </summary>
    private static DateTime Track(GameSession session, Process process)
    {
        session.MainProcess = process;
        try { session.MainProcessName = process.ProcessName; } catch { }

        try { return process.StartTime; }
        catch { return DateTime.Now; }
    }

    /// <summary>Encerra a sessão manualmente (botão "Parar" na interface).</summary>
    public async Task StopAsync()
    {
        var session = Current;
        if (session is null) return;

        _monitorCts?.Cancel();
        await EndAsync(session);
    }

    private async Task EndAsync(GameSession session)
    {
        if (Current != session) return;
        Current = null;

        // O nome anotado ao observar o ÚLTIMO processo principal — o jogo, não o intermediário que
        // o abriu. Ler MainProcess.ProcessName aqui falhava para processos já encerrados.
        _library.RecordSession(session.Game.Id, session.Duration, session.MainProcessName);

        // Histórico detalhado da sessão (horas, FPS, perfil usado) para a seção de estatísticas.
        var fps = _fps?.Stop();
        var telemetry = _telemetry?.Stop();
        _metrics?.Record(new Models.GameHub.PlaySession
        {
            GameId = session.Game.Id,
            GameName = session.Game.Name,
            StartedAt = session.StartedAt,
            EndedAt = DateTime.Now,
            Minutes = session.Duration.TotalMinutes,
            AverageFps = fps?.Average,
            MaxFps = fps?.Max,
            MinFps = fps?.Min,
            OnePercentLowFps = fps?.OnePercentLow,
            FpsEstimated = fps?.IsEstimated,
            AverageCpuTemperature = telemetry?.AverageCpuTemperature,
            AverageGpuTemperature = telemetry?.AverageGpuTemperature,
            AverageCpuUsage = telemetry?.AverageCpuUsage,
            AverageGpuUsage = telemetry?.AverageGpuUsage,
            AverageRamUsedGb = telemetry?.AverageRamUsedGb,
            AverageRamUsagePercent = telemetry?.AverageRamUsagePercent,
            TelemetrySamples = telemetry?.Samples ?? 0,
            ProfileId = session.Profile?.Id,
            ProfileName = session.Profile?.Name,
        });

        if (session.Profile?.RestoreOnExit != false)
        {
            await _engine.RestoreAsync(session.Snapshot, session.Profile,
                new Progress<ProfileStepProgress>(step => StepReported?.Invoke(step)));
        }
        else
        {
            _snapshots.Clear();
        }

        _gamingMode.Exit();
        SessionEnded?.Invoke(session);
    }

    // =====================================================================================
    //  Recuperação
    // =====================================================================================

    /// <summary>
    /// Procura uma sessão que não foi encerrada normalmente (queda do jogo, do Pulse1x ou da
    /// máquina) e desfaz o que tinha sido alterado. Chamado na abertura do app.
    /// Devolve o nome do jogo restaurado, ou null quando não havia nada pendente.
    /// </summary>
    public async Task<string?> RecoverPendingAsync()
    {
        var pending = _snapshots.LoadPending();
        if (pending is null || !pending.HasAnything)
        {
            _snapshots.Clear();
            return null;
        }

        var profile = _profiles.Find(pending.ProfileId);
        await _engine.RestoreAsync(pending, profile);
        return pending.GameName;
    }
}
