using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.GameHub;

/// <summary>
/// Uma sessão de jogo registrada. É a unidade que alimenta todas as estatísticas — total de horas,
/// média por dia, picos, FPS — e por isso guarda o que aconteceu, não os números já calculados:
/// assim uma métrica nova no futuro pode ser derivada do histórico que já existe.
/// </summary>
public class PlaySession
{
    public string GameId { get; set; } = "";
    public string GameName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }

    /// <summary>Duração em minutos (guardada em vez de calculada, para sobreviver a ajustes de relógio).</summary>
    public double Minutes { get; set; }

    // ---- FPS (só preenchido quando o monitoramento de FPS está ligado e conseguiu medir) ----
    public double? AverageFps { get; set; }
    public double? MaxFps { get; set; }
    public double? MinFps { get; set; }
    /// <summary>1% low: a média dos piores 1% dos quadros — é o que se sente como engasgo.</summary>
    public double? OnePercentLowFps { get; set; }

    /// <summary>Perfil aplicado nesta sessão, para comparar desempenho entre perfis.</summary>
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
}

/// <summary>Estatísticas agregadas de um jogo, calculadas a partir das sessões.</summary>
public class GameStats
{
    public string GameId { get; set; } = "";
    public string GameName { get; set; } = "";
    public int SessionCount { get; set; }
    public double TotalMinutes { get; set; }
    public double AverageSessionMinutes { get; set; }
    public double LongestSessionMinutes { get; set; }
    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

    /// <summary>Média de minutos por dia, contando apenas os dias em que o jogo foi aberto.</summary>
    public double AverageMinutesPerActiveDay { get; set; }
    public int ActiveDays { get; set; }

    public double? AverageFps { get; set; }
    public double? BestMaxFps { get; set; }
    public double? WorstOnePercentLowFps { get; set; }

    [JsonIgnore]
    public string TotalText => FormatDuration(TotalMinutes);
    [JsonIgnore]
    public string AverageSessionText => FormatDuration(AverageSessionMinutes);
    [JsonIgnore]
    public string LongestSessionText => FormatDuration(LongestSessionMinutes);
    [JsonIgnore]
    public string PerDayText => FormatDuration(AverageMinutesPerActiveDay);

    public static string FormatDuration(double minutes)
    {
        if (minutes < 1) return "—";
        int hours = (int)(minutes / 60);
        int mins = (int)Math.Round(minutes % 60);
        return hours > 0 ? $"{hours} h {mins} min" : $"{mins} min";
    }
}

/// <summary>Raiz persistida do histórico (<c>gamehub-metrics.json</c>).</summary>
public class PlayMetricsData
{
    public List<PlaySession> Sessions { get; set; } = new();
}
