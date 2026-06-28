namespace Pulse1x.App.Services;

public record HardwareReading(double UsagePercent, double? TemperatureCelsius, double? ClockMHz = null, bool TemperatureIsApproximate = false);

public record GpuReading(string Name, double UsagePercent, double? TemperatureCelsius, double? ClockMHz = null);

public interface IHardwareMonitorService
{
    bool HasElevatedAccess { get; }

    /// <summary>
    /// Memory Integrity (HVCI) ligado bloqueia o driver de leitura de MSR do
    /// LibreHardwareMonitor, impedindo a leitura do sensor real (DTS) da CPU.
    /// </summary>
    bool IsMemoryIntegrityEnabled { get; }

    /// <summary>Modelo da CPU (ex.: "Intel Core i7-12700"), ou "CPU" se indisponível.</summary>
    string CpuName { get; }

    HardwareReading ReadCpu();
    IReadOnlyList<GpuReading> ReadGpus();
}
