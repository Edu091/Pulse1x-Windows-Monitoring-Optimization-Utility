namespace Pulse1x.App.Services.FpsInGame;

public enum GraphicsPreset { Low, Medium, High, Ultra }

public enum TargetResolution { R1080p, R1440p, R4K }

/// <summary>Quão bem o jogo roda, do pior para o melhor.</summary>
public enum RunVerdict { CannotRun, Struggles, Playable, Smooth, VerySmooth }

/// <summary>Resultado da estimativa de um jogo no PC do usuário.</summary>
public sealed record FpsEstimate(
    double Fps,
    int RangeLow,
    int RangeHigh,
    bool HitCap,
    int Cap,
    RunVerdict Verdict,
    bool BelowMinGpu,
    bool BelowMinRam,
    bool BelowRecRam);

/// <summary>
/// Estima o FPS de um jogo para um score G3D qualquer a partir dos pontos já publicados na base
/// (FPS em 1080p Alto nas GPUs de referência). Nenhum modelo próprio de jogo: só interpolação
/// log-log entre as GPUs de referência e os mesmos multiplicadores de resolução e preset da
/// metodologia da PCGameBenchmarks.
/// </summary>
public static class FpsEstimator
{
    /// <summary>Teto global da fonte: acima disso o limite costuma ser o processador ou o motor.</summary>
    public const int GlobalCap = 360;

    /// <summary>Margem da faixa exibida (±). A fonte declara erro típico de ~30%; ±10% dá uma faixa
    /// legível ("55-65") em torno do valor central sem sugerir precisão de benchmark.</summary>
    private const double RangeMargin = 0.10;

    // Multiplicadores relativos ao preset Alto (a base está em Alto; a metodologia dá Low 1,25×,
    // High 0,80× e Ultra 0,65× do Médio).
    public static double PresetFactor(GraphicsPreset preset) => preset switch
    {
        GraphicsPreset.Low => 1.25 / 0.80,
        GraphicsPreset.Medium => 1.0 / 0.80,
        GraphicsPreset.Ultra => 0.65 / 0.80,
        _ => 1.0,
    };

    public static double ResolutionFactor(TargetResolution resolution) => resolution switch
    {
        TargetResolution.R1440p => 0.72,
        TargetResolution.R4K => 0.39,
        _ => 1.0,
    };

    public static FpsEstimate Estimate(
        GameSpec game, IReadOnlyList<int> referenceScores, int gpuScore, double? ramGb,
        GraphicsPreset preset, TargetResolution resolution)
    {
        int cap = game.FpsCap is > 0 and < GlobalCap ? game.FpsCap.Value : GlobalCap;
        double raw = Interpolate1080pHigh(game, referenceScores, gpuScore, cap)
                     * PresetFactor(preset) * ResolutionFactor(resolution);
        bool hitCap = raw >= cap;
        double fps = Math.Min(raw, cap);

        bool belowMinGpu = game.MinG3d is > 0 && gpuScore < game.MinG3d.Value * 0.85;
        bool belowMinRam = ramGb is > 0 && game.MinRamGb is > 0 && ramGb.Value + 0.5 < game.MinRamGb.Value;
        bool belowRecRam = ramGb is > 0 && game.RecRamGb is > 0 && ramGb.Value + 0.5 < game.RecRamGb.Value;

        var verdict = fps switch
        {
            < 20 => RunVerdict.CannotRun,
            < 30 => RunVerdict.Struggles,
            < 55 => RunVerdict.Playable,
            < 100 => RunVerdict.Smooth,
            _ => RunVerdict.VerySmooth,
        };
        // Abaixo do mínimo oficial (GPU ou RAM) o jogo pode nem abrir, por mais FPS que a curva dê.
        if (belowMinGpu && verdict > RunVerdict.Struggles) verdict = RunVerdict.Struggles;
        if (belowMinRam && verdict > RunVerdict.CannotRun) verdict--;

        int low, high;
        if (hitCap)
        {
            low = high = cap;
        }
        else
        {
            low = (int)Math.Floor(fps * (1 - RangeMargin));
            high = Math.Min(cap, (int)Math.Ceiling(fps * (1 + RangeMargin)));
            if (fps >= 30)
            {
                // Múltiplos de 5 mais próximos: 60 FPS vira "55-65", não "50-70".
                low = Math.Max(1, (int)(Math.Round(fps * (1 - RangeMargin) / 5.0) * 5));
                high = Math.Min(cap, (int)(Math.Round(fps * (1 + RangeMargin) / 5.0) * 5));
            }
            if (high <= low) high = low + 1;
        }

        return new FpsEstimate(fps, Math.Max(0, low), high, hitCap, cap, verdict, belowMinGpu, belowMinRam, belowRecRam);
    }

    /// <summary>Melhor preset que mantém pelo menos <paramref name="targetFps"/>; null se nem o Baixo chega lá.</summary>
    public static GraphicsPreset? BestPresetFor(
        GameSpec game, IReadOnlyList<int> referenceScores, int gpuScore, TargetResolution resolution, double targetFps = 60)
    {
        int cap = game.FpsCap is > 0 and < GlobalCap ? game.FpsCap.Value : GlobalCap;
        double baseFps = Interpolate1080pHigh(game, referenceScores, gpuScore, cap) * ResolutionFactor(resolution);
        foreach (var preset in new[] { GraphicsPreset.Ultra, GraphicsPreset.High, GraphicsPreset.Medium, GraphicsPreset.Low })
        {
            double fps = Math.Min(cap, baseFps * PresetFactor(preset));
            if (fps >= Math.Min(targetFps, cap) - 0.5) return preset;
        }
        return null;
    }

    /// <summary>
    /// A configuração que a lista mostra: o preset mais alto que mantém 60 FPS (ou o limite do
    /// jogo, se for menor); quando nem o Baixo chega lá, o Baixo — o máximo que dá para tirar.
    /// </summary>
    public static (GraphicsPreset Preset, FpsEstimate Estimate) Recommend(
        GameSpec game, IReadOnlyList<int> referenceScores, int gpuScore, double? ramGb, TargetResolution resolution)
    {
        var preset = BestPresetFor(game, referenceScores, gpuScore, resolution) ?? GraphicsPreset.Low;
        return (preset, Estimate(game, referenceScores, gpuScore, ramGb, preset, resolution));
    }

    /// <summary>
    /// Menor VRAM citada no texto do requisito de GPU ("GTX 1060 6GB / RX 580 8GB" -> 6). Valores
    /// fora da faixa de placas de vídeo (RAM do sistema, espaço em disco) são ignorados.
    /// </summary>
    public static double? RequiredVramGb(string gpuRequirement)
    {
        if (string.IsNullOrWhiteSpace(gpuRequirement)) return null;
        double? min = null;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     gpuRequirement, @"(\d+(?:[.,]\d+)?)\s*(GB|MB)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v)) continue;
            if (m.Groups[2].Value.Equals("MB", StringComparison.OrdinalIgnoreCase)) v /= 1024;
            if (v < 0.25 || v > 24) continue;
            min = min is null ? v : Math.Min(min.Value, v);
        }
        return min;
    }

    /// <summary>
    /// FPS em 1080p Alto, SEM teto, para o score dado. Pontos que bateram no teto (do motor ou o
    /// global) são censurados — o FPS real ali é "pelo menos isso" — e por isso não entram na
    /// interpolação: acima deles a curva é extrapolada a partir dos pontos livres, para que a
    /// queda de 4K ou Ultra parta do desempenho de verdade e não do limite.
    /// </summary>
    internal static double Interpolate1080pHigh(GameSpec game, IReadOnlyList<int> referenceScores, int gpuScore, int cap)
    {
        double score = Math.Max(1, gpuScore);
        var points = referenceScores
            .Select((s, i) => (Score: (double)s, Fps: game.ReferenceFps[i]))
            .Where(p => p.Fps > 0)
            .OrderBy(p => p.Score)
            .ToList();
        if (points.Count == 0) return 0;

        var free = points.Where(p => p.Fps < cap - 0.5).ToList();
        if (free.Count == 0)
        {
            // Todas as GPUs de referência batem no teto: o jogo é leve; assume escala quase linear
            // a partir da mais fraca.
            var first = points[0];
            return first.Fps * Math.Pow(score / first.Score, 0.9);
        }

        if (score <= free[0].Score)
        {
            var slope = free.Count >= 2 ? Slope(free[0], free[1]) : 1.4;
            // Abaixo da GPU de referência mais fraca (Iris Xe) estão integradas antigas, que o G3D
            // superestima (a própria fonte avisa) por falta de banda de memória e drivers piores:
            // a queda é mais que proporcional. 1,3-1,6 aproxima resultados reais de UHD 620/HD 620
            // (CS2 a ~20 FPS no Baixo em 1080p).
            slope = Math.Clamp(slope, 1.3, 1.6);
            return free[0].Fps * Math.Pow(score / free[0].Score, slope);
        }

        for (int i = 0; i < free.Count - 1; i++)
        {
            var a = free[i];
            var b = free[i + 1];
            if (score <= b.Score)
            {
                if (b.Score <= a.Score) return Math.Max(a.Fps, b.Fps);
                double t = (Math.Log(score) - Math.Log(a.Score)) / (Math.Log(b.Score) - Math.Log(a.Score));
                return Math.Exp(Math.Log(a.Fps) + t * (Math.Log(b.Fps) - Math.Log(a.Fps)));
            }
        }

        // Acima da última GPU livre: estende a curva com a inclinação do último trecho.
        var last = free[^1];
        double tailSlope = free.Count >= 2 ? Slope(free[^2], last) : 0.9;
        tailSlope = Math.Clamp(tailSlope, 0.3, 1.2);
        return last.Fps * Math.Pow(score / last.Score, tailSlope);
    }

    private static double Slope((double Score, double Fps) a, (double Score, double Fps) b)
    {
        if (b.Score <= a.Score || a.Fps <= 0 || b.Fps <= 0) return 0.9;
        return (Math.Log(b.Fps) - Math.Log(a.Fps)) / (Math.Log(b.Score) - Math.Log(a.Score));
    }
}
