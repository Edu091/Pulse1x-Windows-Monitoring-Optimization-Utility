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
    private double? _lastTimestamp;

    public InputMetricsSnapshot Snapshot => BuildSnapshot();

    public void Record(double timestampMilliseconds)
    {
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
    }

    private InputMetricsSnapshot BuildSnapshot()
    {
        if (_intervals.Count == 0)
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
