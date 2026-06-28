using System.Linq;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;

namespace Pulse1x.App.Services;

public class HardwareMonitorService : IHardwareMonitorService, IDisposable
{
    private readonly Computer _computer;
    private readonly object _lock = new();

    public bool HasElevatedAccess { get; }
    public bool IsMemoryIntegrityEnabled { get; }
    public string CpuName { get; private set; } = "CPU";

    public HardwareMonitorService()
    {
        HasElevatedAccess = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

        IsMemoryIntegrityEnabled = DetectMemoryIntegrity();

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
        };

        try
        {
            _computer.Open();
        }
        catch
        {
            // Sem acesso a alguns sensores; leituras retornarão nulo/zero.
        }

        CpuName = ResolveCpuName();
    }

    private string ResolveCpuName()
    {
        var fromSensor = _computer.Hardware
            .FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name;
        if (!string.IsNullOrWhiteSpace(fromSensor))
            return fromSensor;

        // Fallback: registro (não depende do driver de sensores).
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch
        {
            // ignora; mantém o padrão.
        }

        return "CPU";
    }

    public HardwareReading ReadCpu()
    {
        var reading = ReadHardware(HardwareType.Cpu);

        var temperature = reading.TemperatureCelsius;
        var clock = reading.ClockMHz;

        // O sensor real (LibreHardwareMonitor, via driver de kernel) leu a temperatura?
        // Se sim — caso típico com Memory Integrity DESLIGADO — usamos o DTS real, preciso.
        // Se não — caso típico com Memory Integrity LIGADO, que bloqueia o driver — caímos
        // num fallback sem driver: primeiro o contador de performance de thermal zone
        // (atualiza a cada ~1s, mais suave) e, por último, a zona ACPI crua. Ambos são
        // aproximados (não são o DTS) e por isso sinalizados como tal.
        bool temperatureIsApproximate = false;
        if (temperature is null)
        {
            temperature = ReadCpuTemperatureFromPerfCounter() ?? ReadCpuTemperatureFromWmi();
            temperatureIsApproximate = temperature is not null;
        }

        if (clock is null)
            clock = ReadCpuClockFromWmi();

        return new HardwareReading(reading.UsagePercent, temperature, clock, temperatureIsApproximate);
    }

    private bool _wmiTemperatureUnavailable;

    private double? ReadCpuTemperatureFromWmi()
    {
        if (_wmiTemperatureUnavailable) return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            // O sistema pode expor várias zonas térmicas (CPU, placa-mãe, zonas passivas).
            // A primeira costuma ser uma zona estática/não-CPU; pegamos a mais quente, que
            // tende a ser a que acompanha a CPU.
            double? hottest = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = Convert.ToDouble(obj["CurrentTemperature"]);
                var celsius = raw / 10.0 - 273.15;
                if (hottest is null || celsius > hottest)
                    hottest = celsius;
            }

            if (hottest is not null)
                return hottest;
        }
        catch
        {
            // Sem suporte a essa classe WMI neste hardware; não tentar novamente.
        }

        _wmiTemperatureUnavailable = true;
        return null;
    }

    private bool _perfThermalUnavailable;

    // Fallback otimizado sem driver: o contador de performance "Thermal Zone Information"
    // (root\CIMV2). Diferente da MSAcpi_ThermalZoneTemperature crua, o coletor de performance
    // do Windows reamostra esse valor periodicamente, então ele atualiza de forma mais regular
    // (não fica "congelado" por longos períodos). Continua sendo uma zona térmica ACPI — é
    // aproximado e não substitui o DTS real do processador.
    private double? ReadCpuTemperatureFromPerfCounter()
    {
        if (_perfThermalUnavailable) return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            double? hottest = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                double? celsius = null;

                // "Temperature" vem em Kelvin inteiro.
                if (obj["Temperature"] is { } t)
                {
                    var kelvin = Convert.ToDouble(t);
                    if (kelvin is > 200 and < 400)
                        celsius = kelvin - 273.15;
                }

                // "HighPrecisionTemperature" vem em décimos de Kelvin; preferimos se for plausível
                // (em alguns sistemas o campo reporta lixo, então validamos a faixa).
                if (obj["HighPrecisionTemperature"] is { } hp)
                {
                    var candidate = Convert.ToDouble(hp) / 10.0 - 273.15;
                    if (candidate is > 0 and < 125)
                        celsius = candidate;
                }

                if (celsius is >= 0 and <= 125 && (hottest is null || celsius > hottest))
                    hottest = celsius;
            }

            if (hottest is not null)
                return hottest;
        }
        catch
        {
            // Contador indisponível neste sistema; não tentar novamente.
        }

        _perfThermalUnavailable = true;
        return null;
    }

    private static bool DetectMemoryIntegrity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            return key?.GetValue("Enabled") is int enabled && enabled == 1;
        }
        catch
        {
            return false;
        }
    }

    private bool _wmiClockUnavailable;

    // CurrentClockSpeed do WMI (Win32_Processor) normalmente é estático e não reflete o Turbo
    // Boost. O contador "Processor Information\Processor Frequency" (o mesmo que o Gerenciador
    // de Tarefas usa) reporta a frequência efetiva real, que varia de fato com a carga.
    private double? ReadCpuClockFromWmi()
    {
        if (_wmiClockUnavailable) return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'");

            foreach (ManagementObject obj in searcher.Get())
            {
                return Convert.ToDouble(obj["ProcessorFrequency"]);
            }
        }
        catch
        {
            // Sem suporte a essa classe WMI neste hardware; não tentar novamente.
        }

        _wmiClockUnavailable = true;
        return null;
    }

    private static readonly HardwareType[] GpuTypes =
    {
        HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel
    };

    public IReadOnlyList<GpuReading> ReadGpus()
    {
        lock (_lock)
        {
            var readings = new List<GpuReading>();

            foreach (var hardware in _computer.Hardware)
            {
                if (!GpuTypes.Contains(hardware.HardwareType)) continue;

                hardware.Update();

                double usage = ReadUsage(hardware);
                double? temperature = ReadTemperature(hardware);
                double? clock = ReadClock(hardware);

                readings.Add(new GpuReading(hardware.Name, usage, temperature, clock));
            }

            return readings;
        }
    }

    private HardwareReading ReadHardware(HardwareType type)
    {
        lock (_lock)
        {
            double usage = 0;
            double? temperature = null;
            double? clock = null;

            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType != type) continue;

                hardware.Update();

                usage = ReadUsage(hardware);
                temperature = ReadTemperature(hardware);
                clock = ReadClock(hardware);
            }

            return new HardwareReading(usage, temperature, clock);
        }
    }

    private static readonly string[] PreferredTemperatureNames = { "Package", "Core (Tctl/Tdie)", "Core Average", "Hot Spot", "Core" };

    private static double ReadUsage(IHardware hardware)
    {
        double usage = 0;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue &&
                (sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || usage == 0))
            {
                usage = sensor.Value.Value;
            }
        }

        return usage;
    }

    private static double? ReadClock(IHardware hardware)
    {
        var clockSensors = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue &&
                        !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (clockSensors.Count == 0) return null;

        return clockSensors.Max(s => s.Value!.Value);
    }

    private static double? ReadTemperature(IHardware hardware)
    {
        var temperatureSensors = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .ToList();

        if (temperatureSensors.Count == 0) return null;

        foreach (var preferredName in PreferredTemperatureNames)
        {
            var match = temperatureSensors.FirstOrDefault(s =>
                s.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Value;
        }

        return temperatureSensors.Max(s => s.Value!.Value);
    }

    public void Dispose()
    {
        _computer.Close();
    }
}
