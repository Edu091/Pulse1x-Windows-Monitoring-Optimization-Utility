using System.IO;
using System.Text.Json;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Histórico de sessões de jogo e as estatísticas derivadas dele.
///
/// Guarda cada sessão em <c>%APPDATA%\Pulse1x\gamehub-metrics.json</c> — um registro por partida,
/// não números já somados. Assim qualquer estatística nova (média por dia, picos, comparação entre
/// perfis) pode ser calculada depois a partir do que já foi registrado, sem perder histórico.
///
/// Pode ser desligado por completo nas Configurações: com <see cref="Enabled"/> falso, nada é
/// gravado — e o botão de apagar remove o que já existe.
/// </summary>
public class PlayMetricsService
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private PlayMetricsData _data = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Registrar sessões (preferência do usuário).</summary>
    public bool Enabled { get; set; } = true;

    public TelemetryCollectionOptions Options { get; private set; } = new();

    public void Configure(TelemetryCollectionOptions options)
    {
        Options = options;
        Changed?.Invoke();
    }

    public event Action? Changed;

    public PlayMetricsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gamehub-metrics.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<PlayMetricsData>(File.ReadAllText(_filePath));
                if (data is not null) { _data = data; return; }
            }
        }
        catch { }
        _data = new PlayMetricsData();
    }

    private void Save()
    {
        lock (_gate)
        {
            try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, JsonOptions)); }
            catch { }
        }
        Changed?.Invoke();
    }

    /// <summary>Registra uma sessão encerrada. Sessões de menos de um minuto são descartadas —
    /// abrir e fechar um jogo por engano não deve sujar as estatísticas.</summary>
    public void Record(PlaySession session)
    {
        var options = Options;
        if (!Enabled || !options.HasAny) return;

        if (!options.Playtime)
        {
            session.Minutes = 0;
            session.StartedAt = session.EndedAt;
        }
        if (!options.Fps)
        {
            session.AverageFps = null;
            session.MaxFps = null;
            session.MinFps = null;
            session.OnePercentLowFps = null;
        }
        if (!options.Temperatures)
        {
            session.AverageCpuTemperature = null;
            session.AverageGpuTemperature = null;
        }
        if (!options.HardwareUsage)
        {
            session.AverageCpuUsage = null;
            session.AverageGpuUsage = null;
        }
        if (!options.Memory)
        {
            session.AverageRamUsedGb = null;
            session.AverageRamUsagePercent = null;
        }

        bool hasTelemetry = session.AverageFps is > 0 || session.AverageCpuTemperature is > 0 ||
                            session.AverageGpuTemperature is > 0 || session.AverageCpuUsage is > 0 ||
                            session.AverageGpuUsage is > 0 || session.AverageRamUsedGb is > 0;
        if (session.Minutes < 1 && !hasTelemetry) return;

        lock (_gate) _data.Sessions.Add(session);
        Save();
    }

    public IReadOnlyList<PlaySession> Sessions
    {
        get { lock (_gate) return _data.Sessions.ToList(); }
    }

    /// <summary>Apaga todo o histórico (botão "limpar estatísticas").</summary>
    public void Clear()
    {
        lock (_gate) _data.Sessions.Clear();
        Save();
    }

    // =====================================================================================
    //  Estatísticas
    // =====================================================================================

    /// <summary>Estatísticas de um jogo específico (null quando ele nunca foi aberto).</summary>
    public GameStats? StatsFor(string gameId)
    {
        List<PlaySession> sessions;
        lock (_gate) sessions = _data.Sessions.Where(s => s.GameId == gameId).ToList();
        return sessions.Count == 0 ? null : Aggregate(sessions);
    }

    /// <summary>Estatísticas de todos os jogos, do mais jogado para o menos.</summary>
    public IReadOnlyList<GameStats> AllStats()
    {
        List<PlaySession> sessions;
        lock (_gate) sessions = _data.Sessions.ToList();

        return sessions
            .GroupBy(s => s.GameId)
            .Select(g => Aggregate(g.ToList()))
            .OrderByDescending(s => s.TotalMinutes)
            .ToList();
    }

    private static GameStats Aggregate(List<PlaySession> sessions)
    {
        var first = sessions.OrderBy(s => s.StartedAt).First();

        // "Dias ativos" = dias distintos em que o jogo foi aberto. A média por dia usa esse número,
        // e não o intervalo de calendário: um jogo aberto 3 vezes numa semana tem média de 3 dias,
        // não de 7 — que é o que a pessoa entende por "quanto eu jogo quando jogo".
        var activeDays = sessions.Select(s => s.StartedAt.Date).Distinct().Count();
        double total = sessions.Sum(s => s.Minutes);

        var withFps = sessions.Where(s => s.AverageFps is > 0).ToList();

        return new GameStats
        {
            GameId = first.GameId,
            GameName = first.GameName,
            SessionCount = sessions.Count,
            TotalMinutes = total,
            AverageSessionMinutes = total / sessions.Count,
            LongestSessionMinutes = sessions.Max(s => s.Minutes),
            FirstPlayed = first.StartedAt,
            LastPlayed = sessions.Max(s => s.EndedAt),
            ActiveDays = activeDays,
            AverageMinutesPerActiveDay = activeDays > 0 ? total / activeDays : 0,
            AverageFps = withFps.Count > 0 ? withFps.Average(s => s.AverageFps!.Value) : null,
            BestMaxFps = sessions.Where(s => s.MaxFps is > 0).Select(s => s.MaxFps!.Value).DefaultIfEmpty(0).Max() is var max && max > 0 ? max : null,
            WorstOnePercentLowFps = sessions.Where(s => s.OnePercentLowFps is > 0)
                .Select(s => s.OnePercentLowFps!.Value).DefaultIfEmpty(0).Min() is var low && low > 0 ? low : null,
            AverageOnePercentLowFps = AverageNullable(sessions.Select(s => s.OnePercentLowFps)),
            AverageCpuTemperature = AverageNullable(sessions.Select(s => s.AverageCpuTemperature)),
            AverageGpuTemperature = AverageNullable(sessions.Select(s => s.AverageGpuTemperature)),
            AverageCpuUsage = AverageNullable(sessions.Select(s => s.AverageCpuUsage)),
            AverageGpuUsage = AverageNullable(sessions.Select(s => s.AverageGpuUsage)),
            AverageRamUsedGb = AverageNullable(sessions.Select(s => s.AverageRamUsedGb)),
            AverageRamUsagePercent = AverageNullable(sessions.Select(s => s.AverageRamUsagePercent)),
        };
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var available = values.Where(v => v is > 0).Select(v => v!.Value).ToList();
        return available.Count == 0 ? null : available.Average();
    }

    // =====================================================================================
    //  Resumo geral
    // =====================================================================================

    /// <summary>Total de horas em todos os jogos.</summary>
    public double TotalMinutes
    {
        get { lock (_gate) return _data.Sessions.Sum(s => s.Minutes); }
    }

    /// <summary>Média de minutos por dia nos últimos <paramref name="days"/> dias de calendário.</summary>
    public double AverageMinutesPerDay(int days = 30)
    {
        var since = DateTime.Now.Date.AddDays(-days + 1);
        lock (_gate)
        {
            double total = _data.Sessions.Where(s => s.StartedAt.Date >= since).Sum(s => s.Minutes);
            return total / days;
        }
    }

    /// <summary>Minutos jogados por dia no período — alimenta o gráfico de barras da seção.</summary>
    public IReadOnlyList<(DateTime Day, double Minutes)> DailyTotals(int days = 14)
        => DailyTotals(null, days);

    public IReadOnlyList<(DateTime Day, double Minutes)> DailyTotals(string? gameId, int days = 14)
    {
        var result = new List<(DateTime, double)>();
        var start = DateTime.Now.Date.AddDays(-days + 1);

        lock (_gate)
        {
            for (int i = 0; i < days; i++)
            {
                var day = start.AddDays(i);
                double minutes = _data.Sessions
                    .Where(s => s.StartedAt.Date == day && (gameId is null || s.GameId == gameId))
                    .Sum(s => s.Minutes);
                result.Add((day, minutes));
            }
        }

        return result;
    }

    /// <summary>Dia em que mais se jogou (o "pico"), no histórico inteiro.</summary>
    public (DateTime Day, double Minutes)? PeakDay() => PeakDay(null);

    /// <summary>Dia em que mais se jogou no histórico geral ou de um jogo específico.</summary>
    public (DateTime Day, double Minutes)? PeakDay(string? gameId)
    {
        lock (_gate)
        {
            var sessions = _data.Sessions
                .Where(s => s.Minutes > 0 && (gameId is null || s.GameId == gameId))
                .ToList();
            if (sessions.Count == 0) return null;

            var best = sessions
                .GroupBy(s => s.StartedAt.Date)
                .Select(g => (Day: g.Key, Minutes: g.Sum(s => s.Minutes)))
                .OrderByDescending(x => x.Minutes)
                .First();
            return best;
        }
    }
}
