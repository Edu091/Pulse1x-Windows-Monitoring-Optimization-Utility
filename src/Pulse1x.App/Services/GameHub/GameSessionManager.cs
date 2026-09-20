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
                session.MainProcess = main;
                _engine.AttachToProcess(session.Profile, main,
                    new Progress<ProfileStepProgress>(step => StepReported?.Invoke(step)));

                // Com o processo do jogo em mãos, começa a medir o desempenho da sessão.
                if (_metrics?.Enabled == true)
                {
                    var options = _metrics.Options;
                    if (options.Fps) _fps?.Start(main);
                    _telemetry?.Start(options);
                }

                try { await main.WaitForExitAsync(token); }
                catch (OperationCanceledException) { return; }
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

        string? processName = null;
        try { processName = session.MainProcess?.ProcessName; } catch { }

        _library.RecordSession(session.Game.Id, session.Duration, processName);

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
