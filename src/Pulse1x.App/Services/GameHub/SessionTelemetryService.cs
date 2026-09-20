using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

public record SessionTelemetrySummary(
    double? AverageCpuTemperature,
    double? AverageGpuTemperature,
    double? AverageCpuUsage,
    double? AverageGpuUsage,
    double? AverageRamUsedGb,
    double? AverageRamUsagePercent,
    int Samples);

/// <summary>
/// Reuses Pulse1x hardware readers and keeps only session averages. Sampling every three seconds
/// keeps overhead low and avoids persisting an unnecessary raw timeline.
/// </summary>
public sealed class SessionTelemetryService : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);
    private readonly IHardwareMonitorService _hardware;
    private readonly ISystemMetricsService _system;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private TelemetryCollectionOptions _options = new();
    private bool _sampling;
    private bool _active;
    private double _cpuTemperatureTotal, _gpuTemperatureTotal, _cpuUsageTotal, _gpuUsageTotal;
    private double _ramUsedTotal, _ramPercentTotal;
    private int _cpuTemperatureSamples, _gpuTemperatureSamples, _cpuUsageSamples;
    private int _gpuUsageSamples, _ramSamples;

    public SessionTelemetryService(IHardwareMonitorService hardware, ISystemMetricsService system)
    {
        _hardware = hardware;
        _system = system;
    }

    public void Start(TelemetryCollectionOptions options)
    {
        Stop();
        if (!options.HasPerformanceTelemetry) return;

        lock (_gate)
        {
            _options = options;
            _active = true;
            ResetTotals();
            _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.Zero, SampleInterval);
        }
    }

    private void Sample()
    {
        lock (_gate)
        {
            if (!_active || _sampling) return;
            _sampling = true;
        }

        try
        {
            HardwareReading? cpu = null;
            IReadOnlyList<GpuReading>? gpus = null;
            RamReading? ram = null;

            if (_options.Temperatures || _options.HardwareUsage)
            {
                cpu = _hardware.ReadCpu();
                gpus = _hardware.ReadGpus();
            }
            if (_options.Memory) ram = _system.ReadRam();

            lock (_gate)
            {
                if (!_active) return;
                if (_options.Temperatures)
                {
                    Add(cpu?.TemperatureCelsius, ref _cpuTemperatureTotal, ref _cpuTemperatureSamples);
                    Add(Maximum(gpus?.Select(g => g.TemperatureCelsius)),
                        ref _gpuTemperatureTotal, ref _gpuTemperatureSamples);
                }
                if (_options.HardwareUsage)
                {
                    Add(cpu?.UsagePercent, ref _cpuUsageTotal, ref _cpuUsageSamples);
                    Add(gpus?.Count > 0 ? gpus.Max(g => g.UsagePercent) : null,
                        ref _gpuUsageTotal, ref _gpuUsageSamples);
                }
                if (_options.Memory && ram is { TotalGb: > 0 })
                {
                    _ramUsedTotal += ram.UsedGb;
                    _ramPercentTotal += ram.UsagePercent;
                    _ramSamples++;
                }
            }
        }
        catch
        {
            // Missing sensors are normal on some systems and do not invalidate the session.
        }
        finally
        {
            lock (_gate) _sampling = false;
        }
    }

    public SessionTelemetrySummary? Stop()
    {
        lock (_gate)
        {
            _active = false;
            _timer?.Dispose();
            _timer = null;

            int samples = Math.Max(Math.Max(_cpuUsageSamples, _gpuUsageSamples), _ramSamples);
            if (samples == 0 && _cpuTemperatureSamples == 0 && _gpuTemperatureSamples == 0)
                return null;

            return new SessionTelemetrySummary(
                Average(_cpuTemperatureTotal, _cpuTemperatureSamples),
                Average(_gpuTemperatureTotal, _gpuTemperatureSamples),
                Average(_cpuUsageTotal, _cpuUsageSamples),
                Average(_gpuUsageTotal, _gpuUsageSamples),
                Average(_ramUsedTotal, _ramSamples),
                Average(_ramPercentTotal, _ramSamples),
                samples);
        }
    }

    private static void Add(double? value, ref double total, ref int count)
    {
        if (value is not > 0 || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return;
        total += value.Value;
        count++;
    }

    private static double? Average(double total, int count) => count > 0 ? total / count : null;

    private static double? Maximum(IEnumerable<double?>? values)
    {
        var available = values?.Where(v => v is > 0).Select(v => v!.Value).ToList();
        return available is { Count: > 0 } ? available.Max() : null;
    }

    private void ResetTotals()
    {
        _cpuTemperatureTotal = _gpuTemperatureTotal = _cpuUsageTotal = _gpuUsageTotal = 0;
        _ramUsedTotal = _ramPercentTotal = 0;
        _cpuTemperatureSamples = _gpuTemperatureSamples = _cpuUsageSamples = 0;
        _gpuUsageSamples = _ramSamples = 0;
    }

    public void Dispose() => Stop();
}
