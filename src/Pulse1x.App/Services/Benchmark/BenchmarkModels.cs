namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Áreas medidas. O resultado geral é a média geométrica das quatro primeiras; o disco fica de fora
/// (um HD derrubaria o "geral" de um PC ótimo para jogos, e o teste escreve no disco escolhido).
/// </summary>
public enum BenchmarkKind { CpuSingle, CpuMulti, Memory, Gpu, Disk }

/// <summary>
/// Sessão de stress contínuo (como o "Stress CPU" do CPU-Z): mantém a carga rodando e devolve uma
/// pontuação a cada ~1 s, na mesma escala do benchmark, para ver o desempenho cair com o calor.
/// </summary>
public interface IStressSession : IDisposable
{
    /// <summary>Mede a próxima janela (~1 s) e devolve a pontuação dela.</summary>
    BenchmarkResult Sample(CancellationToken ct);
}

/// <summary>Uma medida concreta dentro de um teste (ex.: "Leitura: 45,2 GB/s").</summary>
public sealed record BenchmarkMetric(string LabelKey, double Value, string Unit, int Decimals = 1);

/// <summary>Resultado de uma área: pontuação (1000 = máquina de referência) e as medidas que a compõem.</summary>
public sealed class BenchmarkResult
{
    public BenchmarkKind Kind { get; init; }
    public int Score { get; init; }
    public List<BenchmarkMetric> Metrics { get; init; } = new();
    /// <summary>Nome do hardware testado (GPU usada, por exemplo).</summary>
    public string Device { get; init; } = "";
    /// <summary>Preenchido quando o teste não pôde rodar (sem GPU com Direct3D 11, por exemplo).</summary>
    public string? Error { get; init; }
}

/// <summary>Uma execução salva no histórico.</summary>
public sealed class BenchmarkRun
{
    public DateTime When { get; init; } = DateTime.Now;
    public string AppVersion { get; init; } = "";
    public bool OnBattery { get; init; }
    public List<BenchmarkResult> Results { get; init; } = new();

    /// <summary>Média geométrica das áreas medidas — só existe com as quatro presentes.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? Overall => BenchmarkScoring.Overall(Results);

    public BenchmarkResult? Get(BenchmarkKind kind) => Results.FirstOrDefault(r => r.Kind == kind && r.Error is null);
}

public static class BenchmarkScoring
{
    /// <summary>
    /// Pontuação de uma área: média geométrica das razões contra a máquina de referência, × 1000.
    /// A geométrica impede que um único sub-teste muito alto domine o resultado.
    /// </summary>
    public static int Score(IEnumerable<(double Measured, double Reference, bool HigherIsBetter)> parts)
    {
        double logSum = 0;
        int n = 0;
        foreach (var (measured, reference, higher) in parts)
        {
            if (measured <= 0 || reference <= 0) continue;
            double ratio = higher ? measured / reference : reference / measured;
            logSum += Math.Log(ratio);
            n++;
        }
        return n == 0 ? 0 : (int)Math.Round(Math.Exp(logSum / n) * 1000);
    }

    /// <summary>As áreas que entram no resultado geral.</summary>
    public static readonly BenchmarkKind[] CoreKinds = { BenchmarkKind.CpuSingle, BenchmarkKind.CpuMulti, BenchmarkKind.Memory, BenchmarkKind.Gpu };

    public static int? Overall(IEnumerable<BenchmarkResult> results)
    {
        var scores = CoreKinds
            .Select(k => results.FirstOrDefault(r => r.Kind == k && r.Error is null && r.Score > 0)?.Score)
            .ToList();
        if (scores.Any(s => s is null)) return null;
        double log = scores.Average(s => Math.Log(s!.Value));
        return (int)Math.Round(Math.Exp(log));
    }
}
