using System.Diagnostics;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

public record StaticSystemInfo(string SystemModel, string GpuName, string WindowsVersion);

/// <summary>
/// Coleta informações descritivas do sistema (modelo, GPU, versão do Windows) e
/// métricas leves de estado (tempo ligado, número de processos).
/// </summary>
public class SystemInfoService
{
    public StaticSystemInfo GetStaticInfo() => new(
        ResolveSystemModel(),
        ResolveGpuName(),
        ResolveWindowsVersion());

    public string GetUptimeText()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return uptime.TotalDays >= 1
            ? $"{uptime.Days}d {uptime.Hours:00}h {uptime.Minutes:00}m"
            : $"{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    public int GetProcessCount() => Process.GetProcesses().Length;

    private static string ResolveSystemModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");

            foreach (ManagementObject obj in searcher.Get())
            {
                var manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                var model = obj["Model"]?.ToString()?.Trim() ?? "";
                var combined = string.Join(" ",
                    new[] { manufacturer, model }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(combined))
                    return combined;
            }
        }
        catch
        {
            // WMI indisponível.
        }

        return Environment.MachineName;
    }

    private static string ResolveGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");

            var names = new List<string>();
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["Name"] is string n && !string.IsNullOrWhiteSpace(n))
                    names.Add(n.Trim());
            }

            // Prefere a GPU dedicada (NVIDIA/AMD) sobre a integrada.
            var discrete = names.FirstOrDefault(n =>
                n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("AMD", StringComparison.OrdinalIgnoreCase));

            return discrete ?? names.FirstOrDefault() ?? "GPU";
        }
        catch
        {
            return "GPU";
        }
    }

    private static string ResolveWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return "Windows";

            var build = key.GetValue("CurrentBuildNumber") as string ?? "0";
            var display = key.GetValue("DisplayVersion") as string; // ex.: "23H2"
            int.TryParse(build, out var buildNum);

            // O ProductName ainda diz "Windows 10" no Windows 11; o build é quem distingue.
            var name = buildNum >= 22000 ? "Windows 11" : "Windows 10";
            return string.IsNullOrWhiteSpace(display)
                ? $"{name} (build {build})"
                : $"{name} {display} (build {build})";
        }
        catch
        {
            return "Windows";
        }
    }
}
