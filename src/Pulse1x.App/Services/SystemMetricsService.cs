using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services;

public class SystemMetricsService : ISystemMetricsService
{
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private bool _hasPreviousNetworkSample;

    // A descoberta de discos USB usa WMI (lento). Como discos externos não são
    // conectados/removidos a cada segundo, guardamos o resultado e só reconsultamos
    // a cada poucos segundos — elimina a consulta WMI mais cara do ciclo de atualização.
    private HashSet<string> _cachedUsbLetters = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _usbCacheTimeUtc = DateTime.MinValue;
    private static readonly TimeSpan UsbCacheTtl = TimeSpan.FromSeconds(10);

    public RamReading ReadRam()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
            return new RamReading(0, 0, 0);

        const double bytesPerGb = 1024d * 1024 * 1024;
        double totalGb = status.ullTotalPhys / bytesPerGb;
        double freeGb = status.ullAvailPhys / bytesPerGb;
        double usedGb = totalGb - freeGb;
        double percent = totalGb > 0 ? usedGb / totalGb * 100 : 0;

        return new RamReading(usedGb, totalGb, percent);
    }

    public IReadOnlyList<DiskReading> ReadDisks()
    {
        const double bytesPerGb = 1024d * 1024 * 1024;
        var usbLetters = GetUsbDriveLettersCached();
        var readings = new List<DiskReading>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            // Apenas volumes acessíveis e com tamanho real (ignora CD/DVD vazio, cartões removidos etc.).
            if (!drive.IsReady) continue;
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;

            long total;
            long free;
            string label;
            try
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
                label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Disco Local" : drive.VolumeLabel;
            }
            catch
            {
                continue; // volume ficou indisponível entre o GetDrives e a leitura.
            }

            if (total <= 0) continue;

            double totalGb = total / bytesPerGb;
            double freeGb = free / bytesPerGb;
            double usedGb = totalGb - freeGb;
            double percent = totalGb > 0 ? usedGb / totalGb * 100 : 0;

            // Nome no formato "C:".
            string name = drive.Name.TrimEnd('\\', '/');
            string typeText = drive.DriveType switch
            {
                DriveType.Network => "Rede",
                DriveType.Removable => "Externo (removível)",
                DriveType.Fixed when usbLetters.Contains(name) => "Externo (USB)",
                DriveType.Fixed => "Interno",
                _ => "Outro"
            };

            readings.Add(new DiskReading(name, label, typeText, usedGb, freeGb, totalGb, percent));
        }

        return readings;
    }

    // Devolve as letras USB do cache; só refaz a consulta WMI quando o cache expira.
    private HashSet<string> GetUsbDriveLettersCached()
    {
        if (DateTime.UtcNow - _usbCacheTimeUtc < UsbCacheTtl)
            return _cachedUsbLetters;

        _cachedUsbLetters = GetUsbDriveLetters();
        _usbCacheTimeUtc = DateTime.UtcNow;
        return _cachedUsbLetters;
    }

    // Descobre quais letras de unidade correspondem a discos conectados via USB (HDs/SSDs externos
    // costumam reportar DriveType.Fixed, então sem isso seriam marcados como "Interno").
    private static HashSet<string> GetUsbDriveLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var driveSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType = 'USB'");

            foreach (ManagementObject drive in driveSearcher.Get())
            {
                foreach (ManagementObject partition in drive.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        if (logical["DeviceID"] is string id && !string.IsNullOrEmpty(id))
                            letters.Add(id);
                    }
                }
            }
        }
        catch
        {
            // WMI indisponível; caímos no DriveType (apenas removíveis serão marcados como externos).
        }

        return letters;
    }

    public NetworkReading ReadNetwork(TimeSpan elapsedSinceLastRead)
    {
        long bytesReceived = 0;
        long bytesSent = 0;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var stats = nic.GetIPv4Statistics();
            bytesReceived += stats.BytesReceived;
            bytesSent += stats.BytesSent;
        }

        if (!_hasPreviousNetworkSample || elapsedSinceLastRead.TotalSeconds <= 0)
        {
            _lastBytesReceived = bytesReceived;
            _lastBytesSent = bytesSent;
            _hasPreviousNetworkSample = true;
            return new NetworkReading(0, 0);
        }

        double deltaReceived = Math.Max(0, bytesReceived - _lastBytesReceived);
        double deltaSent = Math.Max(0, bytesSent - _lastBytesSent);
        double seconds = elapsedSinceLastRead.TotalSeconds;

        double downloadKbps = deltaReceived / 1024.0 / seconds;
        double uploadKbps = deltaSent / 1024.0 / seconds;

        _lastBytesReceived = bytesReceived;
        _lastBytesSent = bytesSent;

        return new NetworkReading(downloadKbps, uploadKbps);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
