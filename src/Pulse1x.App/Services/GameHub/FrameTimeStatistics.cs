namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Transforma instantes de apresentação de quadros (em milissegundos, na ordem em que chegam) em
/// FPS por segundo, FPS médio e 1% low. É a parte "matemática" do medidor de FPS, separada do ETW
/// para poder ser testada sem um jogo rodando.
///
/// Guarda um histograma dos tempos de quadro em vez da lista de todos eles: uma sessão de 3 horas
/// a 300 fps são mais de 3 milhões de quadros, e o 1% low só precisa saber quantos quadros caíram
/// em cada faixa de tempo — com faixas de 0,1 ms o erro é desprezível e a memória fica fixa.
/// </summary>
public sealed class FrameTimeStatistics
{
    /// <summary>
    /// Intervalo acima do qual o jogo é considerado pausado (minimizado, alt-tab, tela de carga que
    /// parou de apresentar). Esse buraco não é um "engasgo" de verdade: contá-lo como um quadro de
    /// vários segundos derrubaria o 1% low da sessão inteira por causa de um alt-tab.
    /// </summary>
    public const double PauseThresholdMs = 1000;

    /// <summary>Largura de cada faixa do histograma de tempos de quadro.</summary>
    private const double BinWidthMs = 0.1;
    private static readonly int BinCount = (int)(PauseThresholdMs / BinWidthMs) + 1;

    private readonly long[] _binCounts = new long[BinCount];
    private readonly double[] _binSums = new double[BinCount];
    private readonly List<int> _perSecond = new();

    private double? _lastTimestamp;
    private double _windowStart;
    private int _windowFrames;
    private long _frameTimeCount;
    private double _frameTimeSum;

    /// <summary>Total de apresentações recebidas.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Quantidade de quadros de cada segundo completo medido (segundos pausados ficam de fora).</summary>
    public IReadOnlyList<int> PerSecondFrames => _perSecond;

    /// <summary>FPS do último segundo completo, para exibição ao vivo.</summary>
    public double? CurrentFps => _perSecond.Count > 0 ? _perSecond[^1] : null;

    /// <summary>Registra uma apresentação ocorrida no instante indicado (milissegundos).</summary>
    public void AddPresent(double timestampMs)
    {
        FrameCount++;

        if (_lastTimestamp is not double last)
        {
            _lastTimestamp = timestampMs;
            _windowStart = timestampMs;
            _windowFrames = 1;
            return;
        }

        double delta = timestampMs - last;
        if (delta <= 0)
        {
            // Eventos de CPUs diferentes podem chegar levemente fora de ordem numa sessão em tempo
            // real. O quadro existiu e conta para o segundo atual, mas não gera tempo de quadro —
            // um intervalo negativo ou zero não significa nada.
            _windowFrames++;
            return;
        }

        _lastTimestamp = timestampMs;

        if (delta > PauseThresholdMs)
        {
            // Pausa: o segundo que estava aberto foi cortado pelo meio e subestimaria o FPS, então
            // é descartado; a contagem recomeça neste quadro.
            _windowStart = timestampMs;
            _windowFrames = 1;
            return;
        }

        RecordFrameTime(delta);

        // Com delta <= 1 s, no máximo uma fronteira de segundo é cruzada por quadro.
        if (timestampMs >= _windowStart + 1000)
        {
            _perSecond.Add(_windowFrames);
            _windowStart += 1000;
            _windowFrames = 0;
        }
        _windowFrames++;
    }

    private void RecordFrameTime(double deltaMs)
    {
        int bin = Math.Min(BinCount - 1, (int)(deltaMs / BinWidthMs));
        _binCounts[bin]++;
        _binSums[bin] += deltaMs;
        _frameTimeCount++;
        _frameTimeSum += deltaMs;
    }

    /// <summary>
    /// Resume a sessão. Null quando há menos de <paramref name="minimumSeconds"/> segundos
    /// completos — pouco demais para uma média ter significado.
    /// </summary>
    public FpsSummary? Summarize(int minimumSeconds = 5)
    {
        if (_perSecond.Count < minimumSeconds || _frameTimeCount == 0) return null;

        // Média pelo tempo total de quadros (quadros / tempo), e não média das médias por segundo:
        // é a definição usada pelas ferramentas de benchmark.
        double average = 1000.0 * _frameTimeCount / _frameTimeSum;

        return new FpsSummary(
            average,
            _perSecond.Min(),
            _perSecond.Max(),
            OnePercentLow(),
            _perSecond.Count);
    }

    /// <summary>
    /// 1% low: o FPS equivalente à média dos 1% quadros mais lentos da sessão — é o que se sente
    /// como engasgo, e o que a média esconde.
    /// </summary>
    private double OnePercentLow()
    {
        long wanted = Math.Max(1, (long)Math.Ceiling(_frameTimeCount * 0.01));
        long taken = 0;
        double sum = 0;

        for (int bin = BinCount - 1; bin >= 0 && taken < wanted; bin--)
        {
            long count = _binCounts[bin];
            if (count == 0) continue;

            long use = Math.Min(count, wanted - taken);
            // Numa faixa usada só em parte, cada quadro vale a média da faixa.
            sum += use == count ? _binSums[bin] : _binSums[bin] / count * use;
            taken += use;
        }

        return taken == 0 || sum <= 0 ? 0 : 1000.0 * taken / sum;
    }
}
