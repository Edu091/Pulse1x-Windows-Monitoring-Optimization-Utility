using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using Pulse1x.App.Models;

namespace Pulse1x.App.Services;

/// <summary>
/// Coleta informações DETALHADAS (estáticas) de cada componente via WMI, sob demanda
/// quando o usuário abre a página de detalhes. Tudo é envolto em try/catch para nunca
/// derrubar a UI caso uma classe WMI esteja indisponível.
/// </summary>
public class HardwareDetailsService
{
    private readonly IHardwareMonitorService _hardware;

    public HardwareDetailsService(IHardwareMonitorService hardware)
    {
        _hardware = hardware;
    }

    public ComponentDetails GetDetails(string kind, string id) => kind switch
    {
        "cpu" => GetCpuDetails(),
        "gpu" => GetGpuDetails(id),
        "ram" => GetRamDetails(),
        "disk" => GetDiskDetails(id),
        "net" => GetNetworkDetails(),
        _ => new ComponentDetails("Detalhes", "", Array.Empty<DetailGroup>())
    };

    // ===================================================================== CPU
    private ComponentDetails GetCpuDetails()
    {
        var groups = new List<DetailGroup>();
        string subtitle = _hardware.CpuName;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject o in searcher.Get())
            {
                subtitle = Str(o, "Name", subtitle);

                var items = new List<DetailItem>
                {
                    new("Modelo", Str(o, "Name")),
                    new("Fabricante", Str(o, "Manufacturer")),
                    new("Descrição", Str(o, "Description")),
                    new("Soquete", Str(o, "SocketDesignation")),
                    new("Núcleos físicos", Str(o, "NumberOfCores")),
                    new("Threads (lógicos)", Str(o, "NumberOfLogicalProcessors")),
                    new("Clock máximo", Mhz(o, "MaxClockSpeed")),
                    new("Cache L2", CacheSize(o, "L2CacheSize", level: 4)),
                    new("Cache L3", CacheSize(o, "L3CacheSize", level: 5)),
                    new("Arquitetura", AddressWidth(o)),
                    new("Virtualização", Bool(o, "VirtualizationFirmwareEnabled")),
                    new("ID do processador", Str(o, "ProcessorId")),
                };
                groups.Add(new DetailGroup("Especificações", Clean(items)));
                break;
            }
        }
        catch { /* ignora */ }

        // Leitura ao vivo (sensores).
        try
        {
            var live = _hardware.ReadCpu();
            var liveItems = new List<DetailItem>
            {
                new("Uso atual", $"{live.UsagePercent:0.#}%"),
                new("Temperatura", live.TemperatureCelsius is { } t ? $"{t:0.#} °C{(live.TemperatureIsApproximate ? " (aprox.)" : "")}" : "N/D"),
                new("Frequência (ao vivo)", live.ClockMHz is { } c ? $"{c / 1000:0.##} GHz" : "N/D"),
            };
            groups.Add(new DetailGroup("Em tempo real", liveItems));
        }
        catch { /* ignora */ }

        return new ComponentDetails("Processador", subtitle, groups);
    }

    // ===================================================================== RAM
    private ComponentDetails GetRamDetails()
    {
        var groups = new List<DetailGroup>();
        ulong totalBytes = 0;
        int modules = 0;
        string type = "";

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementObject o in searcher.Get())
            {
                modules++;
                ulong cap = ULong(o, "Capacity");
                totalBytes += cap;
                string memType = SmbiosMemoryType(o);
                if (memType != "" && type == "") type = memType;

                string locator = Str(o, "DeviceLocator");
                string bank = Str(o, "BankLabel");
                string header = !string.IsNullOrEmpty(bank) && !string.IsNullOrEmpty(locator)
                    ? $"Módulo {modules} — {locator} ({bank})"
                    : $"Módulo {modules}" + (locator != "" ? $" — {locator}" : "");

                var items = new List<DetailItem>
                {
                    new("Capacidade", cap > 0 ? Bytes(cap) : ""),
                    new("Tipo", memType),
                    new("Fabricante", CleanManufacturer(Str(o, "Manufacturer"))),
                    new("Modelo (part number)", Str(o, "PartNumber").Trim()),
                    new("Velocidade nominal", Mhz(o, "Speed")),
                    new("Velocidade configurada", Mhz(o, "ConfiguredClockSpeed")),
                    new("Formato", FormFactor(o)),
                    new("Voltagem configurada", Millivolts(o, "ConfiguredVoltage")),
                    new("Número de série", Str(o, "SerialNumber").Trim()),
                };
                groups.Add(new DetailGroup(header, Clean(items)));
            }
        }
        catch { /* ignora */ }

        var summary = new List<DetailItem>
        {
            new("Capacidade total", totalBytes > 0 ? Bytes(totalBytes) : "N/D"),
            new("Módulos instalados", modules > 0 ? modules.ToString() : "N/D"),
            new("Tipo", type != "" ? type : "N/D"),
        };
        groups.Insert(0, new DetailGroup("Resumo", summary));

        return new ComponentDetails("Memória RAM", type != "" ? $"{Bytes(totalBytes)} {type}" : Bytes(totalBytes), groups);
    }

    // ===================================================================== GPU
    private ComponentDetails GetGpuDetails(string gpuName)
    {
        var groups = new List<DetailGroup>();
        string subtitle = gpuName;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            ManagementObject? match = null;
            ManagementObject? first = null;
            foreach (ManagementObject o in searcher.Get())
            {
                first ??= o;
                if (!string.IsNullOrEmpty(gpuName) && Str(o, "Name").Contains(gpuName, StringComparison.OrdinalIgnoreCase))
                {
                    match = o;
                    break;
                }
            }
            var gpu = match ?? first;
            if (gpu is not null)
            {
                subtitle = Str(gpu, "Name", subtitle);
                string resolution = "";
                string h = Str(gpu, "CurrentHorizontalResolution");
                string v = Str(gpu, "CurrentVerticalResolution");
                if (h != "" && v != "") resolution = $"{h} × {v}";

                var items = new List<DetailItem>
                {
                    new("Modelo", Str(gpu, "Name")),
                    new("Fabricante", Str(gpu, "AdapterCompatibility")),
                    new("Processador de vídeo", Str(gpu, "VideoProcessor")),
                    new("Memória dedicada", VramText(gpu)),
                    new("Resolução atual", resolution),
                    new("Taxa de atualização", Hz(gpu, "CurrentRefreshRate")),
                    new("Versão do driver", Str(gpu, "DriverVersion")),
                    new("Data do driver", DriverDate(gpu)),
                };
                groups.Add(new DetailGroup("Especificações", Clean(items)));
            }
        }
        catch { /* ignora */ }

        // Sensores ao vivo (se disponíveis).
        try
        {
            var live = _hardware.ReadGpus().FirstOrDefault(g =>
                string.IsNullOrEmpty(gpuName) || g.Name.Contains(gpuName, StringComparison.OrdinalIgnoreCase));
            if (live is not null)
            {
                var liveItems = new List<DetailItem>
                {
                    new("Uso atual", $"{live.UsagePercent:0.#}%"),
                    new("Temperatura", live.TemperatureCelsius is { } t ? $"{t:0.#} °C" : "N/D"),
                    new("Clock (ao vivo)", live.ClockMHz is { } c ? $"{c:0} MHz" : "N/D"),
                };
                groups.Add(new DetailGroup("Em tempo real", liveItems));
            }
        }
        catch { /* ignora */ }

        return new ComponentDetails("Placa de vídeo", subtitle, groups);
    }

    // ==================================================================== DISCO
    private ComponentDetails GetDiskDetails(string driveLetter)
    {
        var groups = new List<DetailGroup>();
        string subtitle = driveLetter;

        // Volume lógico (rápido, via .NET).
        try
        {
            var di = new DriveInfo(driveLetter);
            if (di.IsReady)
            {
                subtitle = string.IsNullOrWhiteSpace(di.VolumeLabel) ? driveLetter : $"{di.VolumeLabel} ({driveLetter})";
                var volItems = new List<DetailItem>
                {
                    new("Letra", driveLetter),
                    new("Rótulo", di.VolumeLabel),
                    new("Sistema de arquivos", di.DriveFormat),
                    new("Tipo de unidade", DriveTypeText(di.DriveType)),
                    new("Capacidade total", Bytes((ulong)di.TotalSize)),
                    new("Espaço livre", Bytes((ulong)di.AvailableFreeSpace)),
                    new("Espaço usado", Bytes((ulong)(di.TotalSize - di.AvailableFreeSpace))),
                };
                groups.Add(new DetailGroup("Volume", Clean(volItems)));
            }
        }
        catch { /* ignora */ }

        // Disco físico associado (Win32 + MSFT_PhysicalDisk para SSD/HDD e barramento).
        try
        {
            string letter = driveLetter.TrimEnd('\\', '/');
            using var partSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
            foreach (ManagementObject partition in partSearcher.Get())
            {
                using var driveSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{Str(partition, "DeviceID")}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject disk in driveSearcher.Get())
                {
                    string index = Str(disk, "Index");
                    var items = new List<DetailItem>
                    {
                        new("Modelo", Str(disk, "Model")),
                        new("Interface", Str(disk, "InterfaceType")),
                        new("Mídia", MediaTypeText(disk, index)),
                        new("Barramento", BusType(index)),
                        new("Capacidade", Bytes(ULong(disk, "Size"))),
                        new("Partições", Str(disk, "Partitions")),
                        new("Firmware", Str(disk, "FirmwareRevision").Trim()),
                        new("Número de série", Str(disk, "SerialNumber").Trim()),
                    };
                    groups.Add(new DetailGroup("Disco físico", Clean(items)));
                    break;
                }
                break;
            }
        }
        catch { /* ignora */ }

        return new ComponentDetails("Armazenamento", subtitle, groups);
    }

    // ==================================================================== REDE
    private ComponentDetails GetNetworkDetails()
    {
        var groups = new List<DetailGroup>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = nic.GetIPProperties();
                string ipv4 = string.Join(", ", ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));

                var items = new List<DetailItem>
                {
                    new("Tipo", NicTypeText(nic.NetworkInterfaceType)),
                    new("Velocidade", nic.Speed > 0 ? $"{nic.Speed / 1_000_000.0:0} Mbps" : ""),
                    new("Endereço MAC", FormatMac(nic.GetPhysicalAddress().ToString())),
                    new("IPv4", ipv4),
                };
                groups.Add(new DetailGroup(nic.Name, Clean(items)));
            }
        }
        catch { /* ignora */ }

        if (groups.Count == 0)
            groups.Add(new DetailGroup("Rede", new[] { new DetailItem("Status", "Nenhum adaptador ativo") }));

        return new ComponentDetails("Rede", "Adaptadores ativos", groups);
    }

    // ============================================================ Helpers WMI
    private static string Str(ManagementBaseObject o, string prop, string fallback = "")
    {
        try { return o[prop]?.ToString()?.Trim() ?? fallback; }
        catch { return fallback; }
    }

    private static ulong ULong(ManagementBaseObject o, string prop)
    {
        try { return o[prop] is null ? 0 : Convert.ToUInt64(o[prop]); }
        catch { return 0; }
    }

    private static string Mhz(ManagementBaseObject o, string prop)
    {
        var v = Str(o, prop);
        return v == "" ? "" : $"{v} MHz";
    }

    private static string Hz(ManagementBaseObject o, string prop)
    {
        var v = Str(o, prop);
        return v == "" || v == "0" ? "" : $"{v} Hz";
    }

    private static string Millivolts(ManagementBaseObject o, string prop)
    {
        var mv = ULong(o, prop);
        return mv == 0 ? "" : $"{mv / 1000.0:0.##} V";
    }

    private static string Bool(ManagementBaseObject o, string prop)
    {
        try { return o[prop] is bool b ? (b ? "Ativada" : "Desativada") : ""; }
        catch { return ""; }
    }

    private static string KbToReadable(ManagementBaseObject o, string prop)
    {
        var kb = ULong(o, prop);
        if (kb == 0) return "";
        return kb >= 1024 ? $"{kb / 1024.0:0.##} MB" : $"{kb} KB";
    }

    private static string AddressWidth(ManagementBaseObject o)
    {
        var w = Str(o, "AddressWidth");
        return w == "" ? "" : $"{w} bits";
    }

    private static string AdapterRam(ManagementBaseObject o)
    {
        // AdapterRAM é uint32 e estoura em GPUs com 4 GB+; só usado como último recurso.
        var b = ULong(o, "AdapterRAM");
        return b == 0 ? "" : Bytes(b);
    }

    private static string VramText(ManagementBaseObject gpu)
    {
        ulong bytes = GpuVramReader.ReadVramBytes(Str(gpu, "Name"));
        return bytes > 0 ? Bytes(bytes) : AdapterRam(gpu);
    }

    // Win32_Processor.L2CacheSize/L3CacheSize costumam vir zerados em vários processadores
    // (especialmente AMD multi-CCD); Win32_CacheMemory é mais confiável como fallback.
    private static string CacheSize(ManagementBaseObject o, string prop, int level)
    {
        var fromProcessor = KbToReadable(o, prop);
        return fromProcessor != "" ? fromProcessor : ReadCacheFromWmi(level);
    }

    private static string ReadCacheFromWmi(int level)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT InstalledSize FROM Win32_CacheMemory WHERE Level = {level}");
            ulong totalKb = 0;
            foreach (ManagementObject o in searcher.Get())
                totalKb += ULong(o, "InstalledSize");
            if (totalKb == 0) return "";
            return totalKb >= 1024 ? $"{totalKb / 1024.0:0.##} MB" : $"{totalKb} KB";
        }
        catch
        {
            return "";
        }
    }

    private static string DriverDate(ManagementBaseObject o)
    {
        var raw = Str(o, "DriverDate");
        if (raw.Length >= 8 && DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd",
            null, System.Globalization.DateTimeStyles.None, out var d))
            return d.ToString("dd/MM/yyyy");
        return "";
    }

    private static string Bytes(ulong bytes)
    {
        if (bytes == 0) return "";
        double gb = bytes / (1024d * 1024 * 1024);
        return gb >= 1024 ? $"{gb / 1024:0.##} TB" : $"{gb:0.##} GB";
    }

    private static string SmbiosMemoryType(ManagementBaseObject o)
    {
        // SMBIOSMemoryType é o campo confiável; MemoryType (legado) costuma vir 0.
        var t = (int)ULong(o, "SMBIOSMemoryType");
        return t switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            30 => "LPDDR4",
            _ => t > 0 ? $"Tipo {t}" : ""
        };
    }

    private static string FormFactor(ManagementBaseObject o)
    {
        var f = (int)ULong(o, "FormFactor");
        return f switch
        {
            8 => "DIMM",
            12 => "SODIMM",
            13 => "Micro-DIMM",
            _ => f > 0 ? $"Formato {f}" : ""
        };
    }

    private static string CleanManufacturer(string m)
    {
        // Alguns módulos retornam códigos JEDEC; mantém legível o que já vier por extenso.
        return string.IsNullOrWhiteSpace(m) || m.StartsWith("0x") ? m.Trim() : m.Trim();
    }

    private static string MediaTypeText(ManagementBaseObject disk, string index)
    {
        // Tenta MSFT_PhysicalDisk (root\Microsoft\Windows\Storage): MediaType 3=HDD, 4=SSD.
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{index}'");
            foreach (ManagementObject p in s.Get())
            {
                var mt = (int)ULong(p, "MediaType");
                if (mt == 4) return "SSD";
                if (mt == 3) return "HDD";
            }
        }
        catch { /* ignora */ }

        var media = Str(disk, "MediaType");
        return media;
    }

    private static string BusType(string index)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                $"SELECT BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{index}'");
            foreach (ManagementObject p in s.Get())
            {
                return (int)ULong(p, "BusType") switch
                {
                    3 => "ATA",
                    7 => "USB",
                    8 => "RAID",
                    11 => "SATA",
                    17 => "NVMe",
                    _ => ""
                };
            }
        }
        catch { /* ignora */ }
        return "";
    }

    private static string DriveTypeText(DriveType t) => t switch
    {
        DriveType.Fixed => "Fixo (interno)",
        DriveType.Removable => "Removível",
        DriveType.Network => "Rede",
        DriveType.CDRom => "CD/DVD",
        DriveType.Ram => "Disco RAM",
        _ => t.ToString()
    };

    private static string NicTypeText(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Ethernet => "Ethernet (cabo)",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.GigabitEthernet => "Gigabit Ethernet",
        _ => t.ToString()
    };

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length != 12) return raw;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    // Remove itens sem valor para não poluir a tela.
    private static IReadOnlyList<DetailItem> Clean(IEnumerable<DetailItem> items) =>
        items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList();
}
