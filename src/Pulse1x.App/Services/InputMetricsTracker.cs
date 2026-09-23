namespace Pulse1x.App.Services;

/// <summary>Resultado de uma janela de eventos de entrada. A taxa representa eventos observados,
/// não a frequência USB anunciada pelo dispositivo.</summary>
public sealed record InputMetricsSnapshot(
    int SampleCount,
    double PollingRate,
    double AverageInterval,
    double CurrentInterval,
    double Jitter,
    IReadOnlyList<double> Intervals);

/// <summary>
/// Calcula intervalo, taxa e jitter em uma janela móvel. Pausas longas iniciam uma nova sequência,
/// para o tempo em que o usuário ficou parado não distorcer a medição seguinte.
/// </summary>
public sealed class InputMetricsTracker
{
    private const double BurstTimeoutMilliseconds = 350;
    // 8 kHz = 0,125 ms. O limite só elimina timestamps duplicados/ruído impossível, sem cortar
    // dispositivos modernos de 2, 4 ou 8 kHz.
    private const double MinimumIntervalMilliseconds = 0.025;
    private const int MaximumIntervals = 512;

    private readonly Queue<double> _intervals = new();
    private readonly Func<double> _clock;
    private double? _lastTimestamp;
    private double? _lastActivityMilliseconds;

    public InputMetricsSnapshot Snapshot => BuildSnapshot();

    public InputMetricsTracker() : this(
        () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency) { }

    internal InputMetricsTracker(Func<double> clock) => _clock = clock;

    public void Record(double timestampMilliseconds)
    {
        _lastActivityMilliseconds = _clock();
        if (_lastTimestamp is not double previous)
        {
            _lastTimestamp = timestampMilliseconds;
            return;
        }

        double interval = timestampMilliseconds - previous;
        if (interval <= 0) return;
        _lastTimestamp = timestampMilliseconds;

        if (interval > BurstTimeoutMilliseconds)
        {
            _intervals.Clear();
            return;
        }

        if (interval < MinimumIntervalMilliseconds) return;
        _intervals.Enqueue(interval);
        while (_intervals.Count > MaximumIntervals) _intervals.Dequeue();
    }

    public void Reset()
    {
        _intervals.Clear();
        _lastTimestamp = null;
        _lastActivityMilliseconds = null;
    }

    private InputMetricsSnapshot BuildSnapshot()
    {
        if (_intervals.Count == 0 ||
            (_lastActivityMilliseconds is double activity && _clock() - activity > BurstTimeoutMilliseconds))
            return new(0, 0, 0, 0, 0, Array.Empty<double>());

        var values = _intervals.ToArray();
        double average = values.Average();
        double variance = values.Select(value => Math.Pow(value - average, 2)).Average();
        return new(
            values.Length,
            average > 0 ? 1000 / average : 0,
            average,
            values[^1],
            Math.Sqrt(variance),
            values);
    }
}
