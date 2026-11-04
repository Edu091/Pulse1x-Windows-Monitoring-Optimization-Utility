using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Pulse1x.App.Localization;

namespace Pulse1x.App.Services;

/// <summary>Como o item está: define a cor e o ícone do cartão.</summary>
public enum FirmwareState
{
    /// <summary>No estado recomendado para desempenho/uso.</summary>
    Good,
    /// <summary>Vale mudar (está desligado mas o PC suporta, ou custa desempenho).</summary>
    Attention,
    /// <summary>Informativo: nem bom nem ruim (ex.: virtualização desligada para quem não usa VMs).</summary>
    Info,
    /// <summary>O hardware não suporta.</summary>
    NotSupported,
    /// <summary>Não foi possível verificar.</summary>
    Unknown,
}

public enum FirmwareGroup { Bios, Windows }

/// <summary>Um item do painel: valor detectado, compatibilidade do aparelho e o que fazer.</summary>
public sealed record FirmwareItem(
    string Id,
    FirmwareGroup Group,
    string Title,
    string Value,
    FirmwareState State,
    string Compatibility,
    string Tip);

/// <summary>
/// Lê opções de BIOS/UEFI e do Windows que mais pesam em desempenho e uso: Resizable BAR, XMP/EXPO,
/// Secure Boot, TPM, virtualização, modo de boot, SMT, canais de memória, ventoinhas, integridade
/// de memória, agendamento de GPU, Modo de Jogo, gravação em segundo plano e plano de energia.
/// Tudo é só leitura — o painel explica onde mudar, nunca altera a BIOS.
/// </summary>
public sealed class FirmwareStatusService
{
    private readonly IHardwareMonitorService _hardware;
    private readonly Profiles.PowerPlanService? _powerPlans;

    public FirmwareStatusService(IHardwareMonitorService hardware, Profiles.PowerPlanService? powerPlans = null)
    {
        _hardware = hardware;
        _powerPlans = powerPlans;
    }

    public async Task<IReadOnlyList<FirmwareItem>> ReadAsync()
    {
        var items = await Task.Run(ReadAll);
        string? plan = null;
        try { plan = _powerPlans is null ? null : (await _powerPlans.GetActivePlanAsync())?.Name; }
        catch { /* powercfg indisponível */ }
        items.Add(PowerPlan(plan));
        return items;
    }

    private List<FirmwareItem> ReadAll()
    {
        bool uefi = IsUefi();
        bool laptop = IsLaptop();
        var gpus = VideoControllers();
        var list = new List<FirmwareItem>();
        void Add(Func<FirmwareItem> read, string id, FirmwareGroup group)
        {
            try { list.Add(read()); }
            catch { list.Add(new FirmwareItem(id, group, Loc.S("Fw_" + id), Loc.S("Fw_Unknown"), FirmwareState.Unknown, "", "")); }
        }

        Add(() => ResizableBar(gpus, uefi), "ReBar", FirmwareGroup.Bios);
        Add(() => MemoryProfile(laptop), "Xmp", FirmwareGroup.Bios);
        Add(() => SecureBoot(uefi), "SecureBoot", FirmwareGroup.Bios);
        Add(Tpm, "Tpm", FirmwareGroup.Bios);
        Add(Virtualization, "Vt", FirmwareGroup.Bios);
        Add(() => BootMode(uefi), "Uefi", FirmwareGroup.Bios);
        Add(Smt, "Smt", FirmwareGroup.Bios);
        Add(MemoryChannels, "Channels", FirmwareGroup.Bios);
        Add(() => Fans(laptop), "Fans", FirmwareGroup.Bios);
        Add(MemoryIntegrity, "Hvci", FirmwareGroup.Windows);
        Add(() => GpuScheduling(gpus), "Hags", FirmwareGroup.Windows);
        Add(GameMode, "GameMode", FirmwareGroup.Windows);
        Add(BackgroundRecording, "GameDvr", FirmwareGroup.Windows);
        return list;
    }

    private static FirmwareItem Item(string id, FirmwareGroup group, string value, FirmwareState state, string compat, string tip) =>
        new(id, group, Loc.S("Fw_" + id), value, state, compat, tip);

    // =====================================================================================
    //  BIOS / UEFI
    // =====================================================================================

    /// <summary>
    /// Resizable BAR / Smart Access Memory: com ele a CPU enxerga toda a VRAM de uma vez (BAR1 do
    /// tamanho da memória de vídeo) em vez de janelas de 256 MB. NVIDIA: pelo nvidia-smi; AMD: pela
    /// chave do driver. O WMI não serve — ele omite justamente o BAR da VRAM.
    /// </summary>
    private static FirmwareItem ResizableBar(List<string> gpus, bool uefi)
    {
        const string id = "ReBar";
        var g = FirmwareGroup.Bios;
        string? dedicated = gpus.FirstOrDefault(IsNvidia) ?? gpus.FirstOrDefault(IsAmdDiscrete) ?? gpus.FirstOrDefault(IsIntelArc);
        if (dedicated is null)
            return Item(id, g, Loc.S("Fw_NoDedicatedGpu"), FirmwareState.NotSupported, Loc.S("Fw_ReBarCompatNoGpu"), "");

        bool? gpuSupports = ReBarGpuSupport(dedicated);
        string compat = gpuSupports switch
        {
            true when uefi => Loc.F("Fw_ReBarCompatYes", dedicated),
            true => Loc.S("Fw_ReBarCompatNeedsUefi"),
            false => Loc.F("Fw_ReBarCompatNo", dedicated),
            _ => Loc.F("Fw_ReBarCompatMaybe", dedicated),
        };

        bool? enabled = null;
        string detail = "";
        if (IsNvidia(dedicated))
        {
            var bar1 = NvidiaBar1MiB();
            if (bar1 is not null)
            {
                enabled = bar1 > 256;
                detail = $" (BAR1 {bar1:N0} MiB)";
            }
        }
        else if (IsAmdDiscrete(dedicated))
        {
            enabled = AmdSmartAccessMemory();
        }

        return enabled switch
        {
            true => Item(id, g, Loc.S("Fw_Enabled") + detail, FirmwareState.Good, compat, Loc.S("Fw_ReBarTipOn")),
            false when gpuSupports != false => Item(id, g, Loc.S("Fw_Disabled") + detail, FirmwareState.Attention, compat, Loc.S("Fw_ReBarTipOff")),
            false => Item(id, g, Loc.S("Fw_Disabled") + detail, FirmwareState.NotSupported, compat, ""),
            _ => Item(id, g, Loc.S("Fw_Unknown"), FirmwareState.Unknown, compat, Loc.S("Fw_ReBarTipUnknown")),
        };
    }

    /// <summary>RTX 30+ / RX 6000+ / Intel Arc têm suporte; GTX e RTX 20 não; o resto, incerto.</summary>
    internal static bool? ReBarGpuSupport(string gpu)
    {
        var g = gpu.ToUpperInvariant();
        if (g.Contains("ARC")) return true;
        var rtx = Regex.Match(g, @"RTX\s*(?:A)?(\d{2})\d{2}");
        if (rtx.Success) return int.Parse(rtx.Groups[1].Value) >= 30;
        if (g.Contains("GTX") || g.Contains("GT ") || g.Contains("MX")) return false;
        var rx = Regex.Match(g, @"RX\s*(\d)\d{3}");
        if (rx.Success) return int.Parse(rx.Groups[1].Value) >= 6;
        // RX 400/500 (3 dígitos) e Vega não têm Smart Access Memory.
        if (Regex.IsMatch(g, @"RX\s*(\d{3}\b|VEGA)")) return false;
        return null;
    }

    private static int? NvidiaBar1MiB()
    {
        string? exe = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        }.FirstOrDefault(File.Exists);
        if (exe is null) return null;

        var psi = new ProcessStartInfo(exe, "-q -d MEMORY")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
        // "BAR1 Memory Usage" seguido de "Total : 8192 MiB".
        var m = Regex.Match(output.Result, @"BAR1 Memory Usage\s+Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Smart Access Memory: o driver da AMD grava o estado em KMD_RebarControlMode (1 = ligado).</summary>
    private static bool? AmdSmartAccessMemory()
    {
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return null;
        foreach (var name in cls.GetSubKeyNames())
        {
            using var k = cls.OpenSubKey(name);
            if (k?.GetValue("DriverDesc") is not string desc || !desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) continue;
            if (k.GetValue("KMD_RebarControlMode") is int mode) return mode == 1;
        }
        return null;
    }

    /// <summary>
    /// XMP (Intel) / EXPO (AMD): perfil de fábrica que leva a RAM à frequência da etiqueta. O Windows
    /// informa a velocidade padrão JEDEC do módulo e a configurada agora: configurada acima da JEDEC
    /// = perfil ligado.
    /// </summary>
    private static FirmwareItem MemoryProfile(bool laptop)
    {
        const string id = "Xmp";
        var g = FirmwareGroup.Bios;
        var modules = Query("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory").ToList();
        if (modules.Count == 0) return Item(id, g, Loc.S("Fw_Unknown"), FirmwareState.Unknown, "", "");

        uint rated = modules.Max(m => ToUInt(m["Speed"]));
        uint configured = modules.Max(m => ToUInt(m["ConfiguredClockSpeed"]));
        uint type = modules.Select(m => ToUInt(m["SMBIOSMemoryType"])).FirstOrDefault();
        string gen = type switch { 34 => "DDR5", 26 => "DDR4", 24 => "DDR3", 35 or 36 => "LPDDR5", 30 => "LPDDR4", _ => "RAM" };
        uint jedecMax = type switch { 34 => 5600, 26 => 3200, 24 => 1600, _ => uint.MaxValue };
        string value = $"{gen} {configured:N0} MT/s";
        if (configured == 0) return Item(id, g, Loc.S("Fw_Unknown"), FirmwareState.Unknown, "", "");

        if (configured > rated + 50 || configured > jedecMax)
            return Item(id, g, Loc.F("Fw_XmpOn", value), FirmwareState.Good, Loc.S("Fw_XmpCompatYes"), Loc.S("Fw_XmpTipOn"));
        if (laptop || type is 30 or 35 or 36)
            return Item(id, g, Loc.F("Fw_XmpJedec", value), FirmwareState.Info, Loc.S("Fw_XmpCompatLaptop"),
                rated > configured ? Loc.F("Fw_XmpLimited", rated) : "");
        if (rated > configured)
            return Item(id, g, Loc.F("Fw_XmpJedec", value), FirmwareState.Info, Loc.S("Fw_XmpCompatMaybe"), Loc.F("Fw_XmpLimited", rated));
        return Item(id, g, Loc.F("Fw_XmpJedec", value), FirmwareState.Attention, Loc.S("Fw_XmpCompatMaybe"), Loc.S("Fw_XmpTipOff"));
    }

    private static FirmwareItem SecureBoot(bool uefi)
    {
        const string id = "SecureBoot";
        var g = FirmwareGroup.Bios;
        if (!uefi) return Item(id, g, Loc.S("Fw_NotAvailable"), FirmwareState.NotSupported, Loc.S("Fw_NeedsUefi"), Loc.S("Fw_SecureBootTipOff"));
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return k?.GetValue("UEFISecureBootEnabled") is int v
            ? v == 1
                ? Item(id, g, Loc.S("Fw_Enabled"), FirmwareState.Good, Loc.S("Fw_CompatUefiYes"), Loc.S("Fw_SecureBootTipOn"))
                : Item(id, g, Loc.S("Fw_Disabled"), FirmwareState.Attention, Loc.S("Fw_CompatUefiYes"), Loc.S("Fw_SecureBootTipOff"))
            : Item(id, g, Loc.S("Fw_Unknown"), FirmwareState.Unknown, Loc.S("Fw_CompatUefiYes"), Loc.S("Fw_SecureBootTipOff"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo { public uint StructVersion, TpmVersion, TpmInterfaceType, TpmImpRevision; }

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, ref TpmDeviceInfo info);

    /// <summary>TPM pela API do TBS (não exige o WMI de segurança). fTPM (AMD) / PTT (Intel) contam.</summary>
    private static FirmwareItem Tpm()
    {
        const string id = "Tpm";
        var g = FirmwareGroup.Bios;
        var info = new TpmDeviceInfo { StructVersion = 1 };
        uint hr = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), ref info);
        if (hr != 0)
            return Item(id, g, Loc.S("Fw_TpmNotFound"), FirmwareState.Attention, Loc.S("Fw_TpmCompat"), Loc.S("Fw_TpmTipOff"));
        return info.TpmVersion >= 2
            ? Item(id, g, Loc.S("Fw_Enabled") + " (2.0)", FirmwareState.Good, Loc.S("Fw_TpmCompat"), Loc.S("Fw_TpmTipOn"))
            : Item(id, g, Loc.S("Fw_Enabled") + " (1.2)", FirmwareState.Attention, Loc.S("Fw_TpmCompat"), Loc.S("Fw_TpmTipOld"));
    }

    /// <summary>
    /// VT-x / AMD-V. Com o hipervisor do Windows rodando (VBS, WSL, Hyper-V), o processador relata
    /// "desligado na BIOS" mesmo ligado — o hipervisor ativo já é a prova de que está ligado.
    /// </summary>
    private static FirmwareItem Virtualization()
    {
        const string id = "Vt";
        var g = FirmwareGroup.Bios;
        var cpu = Query("SELECT VirtualizationFirmwareEnabled, VMMonitorModeExtensions FROM Win32_Processor").FirstOrDefault();
        bool hypervisor = Query("SELECT HypervisorPresent FROM Win32_ComputerSystem").FirstOrDefault()?["HypervisorPresent"] is true;
        bool firmware = cpu?["VirtualizationFirmwareEnabled"] is true;
        bool supported = cpu?["VMMonitorModeExtensions"] is true || hypervisor || firmware;
        if (hypervisor || firmware)
            return Item(id, g, Loc.S("Fw_Enabled"), FirmwareState.Good, Loc.S("Fw_VtCompatYes"), Loc.S("Fw_VtTipOn"));
        return supported
            ? Item(id, g, Loc.S("Fw_Disabled"), FirmwareState.Info, Loc.S("Fw_VtCompatYes"), Loc.S("Fw_VtTipOff"))
            : Item(id, g, Loc.S("Fw_NotAvailable"), FirmwareState.NotSupported, Loc.S("Fw_VtCompatNo"), "");
    }

    private static FirmwareItem BootMode(bool uefi) => uefi
        ? Item("Uefi", FirmwareGroup.Bios, "UEFI", FirmwareState.Good, Loc.S("Fw_UefiCompat"), Loc.S("Fw_UefiTipOn"))
        : Item("Uefi", FirmwareGroup.Bios, "Legacy (CSM)", FirmwareState.Attention, Loc.S("Fw_UefiCompat"), Loc.S("Fw_UefiTipOff"));

    private static FirmwareItem Smt()
    {
        const string id = "Smt";
        var cpus = Query("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").ToList();
        uint cores = (uint)cpus.Sum(c => ToUInt(c["NumberOfCores"]));
        uint logical = (uint)cpus.Sum(c => ToUInt(c["NumberOfLogicalProcessors"]));
        string value = Loc.F("Fw_SmtValue", cores, logical);
        return logical > cores
            ? Item(id, FirmwareGroup.Bios, Loc.S("Fw_Enabled") + " — " + value, FirmwareState.Good, Loc.S("Fw_SmtCompat"), Loc.S("Fw_SmtTipOn"))
            : Item(id, FirmwareGroup.Bios, value, FirmwareState.Info, Loc.S("Fw_SmtCompat"), Loc.S("Fw_SmtTipOff"));
    }

    /// <summary>Dois ou mais módulos = dual-channel provável; um só = single-channel (perde banda).</summary>
    private static FirmwareItem MemoryChannels()
    {
        const string id = "Channels";
        var modules = Query("SELECT Capacity, DeviceLocator FROM Win32_PhysicalMemory").ToList();
        double totalGb = modules.Sum(m => m["Capacity"] is ulong c ? c / (1024d * 1024 * 1024) : 0);
        string value = Loc.F("Fw_ChannelsValue", modules.Count, Math.Round(totalGb));
        return modules.Count >= 2
            ? Item(id, FirmwareGroup.Bios, Loc.S("Fw_DualChannel") + " — " + value, FirmwareState.Good, Loc.S("Fw_ChannelsCompat"), Loc.S("Fw_ChannelsTipOn"))
            : Item(id, FirmwareGroup.Bios, Loc.S("Fw_SingleChannel") + " — " + value, FirmwareState.Attention, Loc.S("Fw_ChannelsCompat"), Loc.S("Fw_ChannelsTipOff"));
    }

    private FirmwareItem Fans(bool laptop)
    {
        const string id = "Fans";
        var fans = _hardware.ReadFans();
        if (fans.Count == 0)
            return Item(id, FirmwareGroup.Bios, Loc.S("Fw_FansNone"), FirmwareState.Info,
                laptop ? Loc.S("Fw_FansCompatLaptop") : Loc.S("Fw_FansCompatDesktop"), Loc.S("Fw_FansTip"));
        string value = string.Join(" · ", fans.Take(4).Select(f => $"{f.Name}: {f.Value:N0} {f.Unit}"));
        return Item(id, FirmwareGroup.Bios, value, FirmwareState.Info, Loc.S("Fw_FansCompatYes"), Loc.S("Fw_FansTip"));
    }

    // =====================================================================================
    //  Windows
    // =====================================================================================

    /// <summary>Integridade de memória (HVCI): protege o kernel, mas custa ~5-10% de FPS em vários jogos.</summary>
    private FirmwareItem MemoryIntegrity()
    {
        const string id = "Hvci";
        bool? running = null;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");
            foreach (ManagementObject mo in s.Get())
                if (mo["SecurityServicesRunning"] is uint[] services) running = services.Contains(2u);
        }
        catch { /* sem DeviceGuard: usa a leitura do monitor de hardware */ }
        running ??= _hardware.IsMemoryIntegrityEnabled;
        return running.Value
            ? Item(id, FirmwareGroup.Windows, Loc.S("Fw_Enabled"), FirmwareState.Attention, Loc.S("Fw_HvciCompat"), Loc.S("Fw_HvciTipOn"))
            : Item(id, FirmwareGroup.Windows, Loc.S("Fw_Disabled"), FirmwareState.Good, Loc.S("Fw_HvciCompat"), Loc.S("Fw_HvciTipOff"));
    }

    /// <summary>Agendamento de GPU acelerado por hardware (HAGS) — exigido pela geração de quadros do DLSS 3.</summary>
    private static FirmwareItem GpuScheduling(List<string> gpus)
    {
        const string id = "Hags";
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
        object? mode = k?.GetValue("HwSchMode");
        string compat = Loc.S("Fw_HagsCompat");
        return mode switch
        {
            2 => Item(id, FirmwareGroup.Windows, Loc.S("Fw_Enabled"), FirmwareState.Good, compat, Loc.S("Fw_HagsTipOn")),
            1 => Item(id, FirmwareGroup.Windows, Loc.S("Fw_Disabled"), FirmwareState.Attention, compat, Loc.S("Fw_HagsTipOff")),
            _ => gpus.Any(g => IsNvidia(g) || IsAmdDiscrete(g) || IsIntelArc(g))
                ? Item(id, FirmwareGroup.Windows, Loc.S("Fw_DefaultOff"), FirmwareState.Attention, compat, Loc.S("Fw_HagsTipOff"))
                : Item(id, FirmwareGroup.Windows, Loc.S("Fw_NotAvailable"), FirmwareState.NotSupported, compat, ""),
        };
    }

    private static FirmwareItem GameMode()
    {
        const string id = "GameMode";
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
        // Sem o valor, o Windows 10/11 deixa o Modo de Jogo ligado.
        bool on = k?.GetValue("AutoGameModeEnabled") is not int v || v != 0;
        return on
            ? Item(id, FirmwareGroup.Windows, Loc.S("Fw_Enabled"), FirmwareState.Good, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_GameModeTipOn"))
            : Item(id, FirmwareGroup.Windows, Loc.S("Fw_Disabled"), FirmwareState.Attention, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_GameModeTipOff"));
    }

    /// <summary>Gravação contínua em segundo plano da Xbox Game Bar ("gravar o que aconteceu").</summary>
    private static FirmwareItem BackgroundRecording()
    {
        const string id = "GameDvr";
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
        bool on = k?.GetValue("HistoricalCaptureEnabled") is int v && v == 1;
        return on
            ? Item(id, FirmwareGroup.Windows, Loc.S("Fw_Enabled"), FirmwareState.Attention, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_GameDvrTipOn"))
            : Item(id, FirmwareGroup.Windows, Loc.S("Fw_Disabled"), FirmwareState.Good, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_GameDvrTipOff"));
    }

    private static FirmwareItem PowerPlan(string? name)
    {
        const string id = "PowerPlan";
        if (string.IsNullOrWhiteSpace(name)) return Item(id, FirmwareGroup.Windows, Loc.S("Fw_Unknown"), FirmwareState.Unknown, "", "");
        string n = name.ToLowerInvariant();
        bool performance = n.Contains("desempenho") || n.Contains("performance") || n.Contains("ultimate") || n.Contains("máximo") || n.Contains("alto");
        return performance
            ? Item(id, FirmwareGroup.Windows, name, FirmwareState.Good, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_PowerTipOn"))
            : Item(id, FirmwareGroup.Windows, name, FirmwareState.Info, Loc.S("Fw_WindowsCompat"), Loc.S("Fw_PowerTipOff"));
    }

    // =====================================================================================
    //  Auxiliares
    // =====================================================================================

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int firmwareType);

    private static bool IsUefi()
    {
        try { return GetFirmwareType(out int type) && type == 2; } // 1 = BIOS, 2 = UEFI
        catch { return true; }
    }

    private static bool IsLaptop() =>
        Query("SELECT ChassisTypes FROM Win32_SystemEnclosure")
            .Any(m => m["ChassisTypes"] is ushort[] t && t.Any(x => x is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32));

    private static List<string> VideoControllers() =>
        Query("SELECT Name FROM Win32_VideoController").Select(m => m["Name"] as string ?? "").Where(n => n.Length > 0).ToList();

    private static bool IsNvidia(string n) => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || n.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
    private static bool IsAmdDiscrete(string n) => Regex.IsMatch(n, @"Radeon.*\bRX\b", RegexOptions.IgnoreCase);
    private static bool IsIntelArc(string n) => Regex.IsMatch(n, @"\bArc\b.*[AB]\d{3}", RegexOptions.IgnoreCase);

    private static IEnumerable<ManagementBaseObject> Query(string wql)
    {
        var list = new List<ManagementBaseObject>();
        try
        {
            using var s = new ManagementObjectSearcher(wql);
            foreach (var mo in s.Get()) list.Add(mo);
        }
        catch { /* WMI indisponível: o item sai como "desconhecido" */ }
        return list;
    }

    private static uint ToUInt(object? value) => value switch
    {
        uint u => u,
        ushort s => s,
        int i => (uint)Math.Max(0, i),
        _ => 0,
    };

    /// <summary>
    /// Reinicia direto na tela de configuração da BIOS/UEFI (o mesmo que Configurações > Recuperação >
    /// Inicialização avançada > Configurações de firmware UEFI). Só existe em PCs UEFI.
    /// </summary>
    public static bool RestartToFirmware()
    {
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool SupportsFirmwareRestart => IsUefi();
}
