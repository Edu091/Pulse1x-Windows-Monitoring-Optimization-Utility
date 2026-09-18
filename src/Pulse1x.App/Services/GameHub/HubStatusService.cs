using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Estado do sistema mostrado na barra superior do GameHub.</summary>
public record HubStatus(
    string Clock,
    int? BatteryPercent,
    bool Charging,
    bool HasBattery,
    double? CpuTemperature,
    double? GpuTemperature);

/// <summary>
/// Relógio, bateria e temperatura para a barra do GameHub — as mesmas informações que um console
/// mostra no canto da tela.
///
/// A temperatura vem do <see cref="IHardwareMonitorService"/> que o Pulse1x já usa no Dashboard, e
/// a bateria da API do Windows (GetSystemPowerStatus), que não custa praticamente nada. Num desktop
/// sem bateria, o indicador simplesmente não aparece.
/// </summary>
public class HubStatusService
{
    private readonly IHardwareMonitorService _hardware;

    public HubStatusService(IHardwareMonitorService hardware) => _hardware = hardware;

    public HubStatus Read()
    {
        var (percent, charging, hasBattery) = ReadBattery();
        var (cpu, gpu) = ReadTemperatures();

        return new HubStatus(
            DateTime.Now.ToString("HH:mm"),
            percent, charging, hasBattery,
            cpu, gpu);
    }

    // =====================================================================================
    //  Bateria
    // =====================================================================================

    private static (int? percent, bool charging, bool hasBattery) ReadBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status)) return (null, false, false);

            // 128 = "sem bateria no sistema"; 255 = desconhecido.
            bool hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
            if (!hasBattery) return (null, status.ACLineStatus == 1, false);

            int? percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
            // ACLineStatus 1 = na tomada; o bit 8 do BatteryFlag indica carregando.
            bool charging = status.ACLineStatus == 1 || (status.BatteryFlag & 8) != 0;

            return (percent, charging, true);
        }
        catch { return (null, false, false); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // =====================================================================================
    //  Temperatura
    // =====================================================================================

    /// <summary>
    /// Lê as temperaturas de CPU e GPU do monitor de hardware que o Pulse1x já mantém aberto.
    /// Nunca lança: numa máquina onde o sensor não existe, devolve null e a interface esconde o
    /// indicador em vez de mostrar um valor inventado.
    /// </summary>
    private (double? cpu, double? gpu) ReadTemperatures()
    {
        double? cpu = null, gpu = null;

        try { cpu = _hardware.ReadCpu().TemperatureCelsius; }
        catch { }

        try
        {
            // Num notebook com gráficos híbridos há mais de uma GPU; mostramos a mais quente, que é
            // a que interessa quando se está jogando.
            gpu = _hardware.ReadGpus()
                .Select(g => g.TemperatureCelsius)
                .Where(t => t is > 0)
                .DefaultIfEmpty(null)
                .Max();
        }
        catch { }

        return (cpu, gpu);
    }
}
