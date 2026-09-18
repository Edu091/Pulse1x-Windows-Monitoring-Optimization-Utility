using System.Management;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Profiles;

/// <summary>Um monitor ativo, com as taxas de atualização que ele aceita na resolução atual.</summary>
public record DisplayInfo(string DeviceName, string FriendlyName, int Width, int Height, int RefreshRate, int[] AvailableRefreshRates, bool IsPrimary);

/// <summary>
/// Brilho, taxa de atualização e HDR, usando as APIs do próprio Windows:
///
/// • Brilho  — WMI (WmiMonitorBrightnessMethods) na tela integrada do notebook e, como alternativa,
///             DDC/CI (dxva2.dll) em monitores externos que aceitam controle por cabo.
/// • Taxa    — EnumDisplaySettings/ChangeDisplaySettingsEx, o mesmo caminho das Configurações de Vídeo.
/// • HDR     — DisplayConfigSetDeviceInfo (SET_ADVANCED_COLOR_STATE), como o botão de HDR do Windows 11.
///
/// Toda leitura acontece antes de qualquer escrita, para o snapshot conseguir devolver o estado.
/// Quando algo não é suportado pela máquina, o método devolve false/null e a sequência do perfil
/// segue em frente — uma tela sem HDR nunca impede o jogo de abrir.
/// </summary>
public class DisplayService
{
    // =====================================================================================
    //  Monitores
    // =====================================================================================

    /// <summary>Lista os monitores ativos e as taxas de atualização possíveis em cada um.</summary>
    public IReadOnlyList<DisplayInfo> ListDisplays()
    {
        var result = new List<DisplayInfo>();

        var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
            {
                device.cb = Marshal.SizeOf<DisplayDevice>();
                continue;
            }

            string name = device.DeviceName;
            bool primary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0;

            var current = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (EnumDisplaySettings(name, EnumCurrentSettings, ref current))
            {
                var rates = new SortedSet<int>();
                var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                for (int m = 0; EnumDisplaySettings(name, m, ref mode); m++)
                {
                    if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                        rates.Add(mode.dmDisplayFrequency);
                    mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                }

                result.Add(new DisplayInfo(
                    name,
                    string.IsNullOrWhiteSpace(device.DeviceString) ? name : device.DeviceString,
                    current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency,
                    rates.ToArray(), primary));
            }

            device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        }

        return result;
    }

    /// <summary>Nome do monitor principal (usado quando o perfil não especifica um).</summary>
    public string? PrimaryDeviceName() =>
        ListDisplays().FirstOrDefault(d => d.IsPrimary)?.DeviceName ?? ListDisplays().FirstOrDefault()?.DeviceName;

    // =====================================================================================
    //  Taxa de atualização
    // =====================================================================================

    public int? GetRefreshRate(string? deviceName = null)
    {
        deviceName ??= PrimaryDeviceName();
        if (deviceName is null) return null;

        var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode) ? mode.dmDisplayFrequency : null;
    }

    /// <summary>
    /// Troca a taxa de atualização mantendo resolução e profundidade de cor. A alteração é aplicada
    /// só nesta sessão do Windows (sem CDS_UPDATEREGISTRY), de modo que um encerramento inesperado
    /// do Pulse1x jamais deixe a tela presa numa taxa diferente da escolhida pelo usuário.
    /// </summary>
    public bool SetRefreshRate(int hz, string? deviceName = null)
    {
        deviceName ??= PrimaryDeviceName();
        if (deviceName is null) return false;

        var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return false;

        mode.dmDisplayFrequency = hz;
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency | DmBitsPerPel;

        int result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (result != DispChangeSuccessful) return false;

        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero) == DispChangeSuccessful;
    }

    // =====================================================================================
    //  Brilho
    // =====================================================================================

    /// <summary>Brilho atual em % (tela integrada via WMI; monitores externos via DDC/CI).</summary>
    public int? GetBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject mo in searcher.Get())
                using (mo)
                    return Convert.ToInt32(mo["CurrentBrightness"]);
        }
        catch { }

        return GetBrightnessDdc();
    }

    /// <summary>Define o brilho em % (0 a 100).</summary>
    public bool SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    // Timeout 1 s: o valor persiste até alguém mudar de novo (não volta sozinho).
                    mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                    return true;
                }
            }
        }
        catch { }

        return SetBrightnessDdc(percent);
    }

    // ---- DDC/CI (monitores externos) ----

    private int? GetBrightnessDdc()
    {
        foreach (var handle in EnumeratePhysicalMonitors())
        {
            try
            {
                if (GetMonitorBrightness(handle.Handle, out uint min, out uint current, out uint max) && max > min)
                    return (int)Math.Round((current - min) * 100.0 / (max - min));
            }
            catch { }
            finally { DestroyPhysicalMonitor(handle.Handle); }
        }
        return null;
    }

    private bool SetBrightnessDdc(int percent)
    {
        bool any = false;
        foreach (var handle in EnumeratePhysicalMonitors())
        {
            try
            {
                if (GetMonitorBrightness(handle.Handle, out uint min, out uint _, out uint max) && max >= min)
                    any |= SetMonitorBrightness(handle.Handle, (uint)(min + (max - min) * percent / 100.0));
            }
            catch { }
            finally { DestroyPhysicalMonitor(handle.Handle); }
        }
        return any;
    }

    private static IEnumerable<PhysicalMonitor> EnumeratePhysicalMonitors()
    {
        var monitors = new List<PhysicalMonitor>();
        try
        {
            var handles = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                handles.Add(hMonitor);
                return true;
            }, IntPtr.Zero);

            foreach (var hMonitor in handles)
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) continue;
                var array = new PhysicalMonitor[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                    monitors.AddRange(array);
            }
        }
        catch { }
        return monitors;
    }

    // =====================================================================================
    //  HDR
    // =====================================================================================

    /// <summary>HDR ligado agora? null quando a tela não suporta (ou não foi possível consultar).</summary>
    public bool? GetHdrEnabled()
    {
        foreach (var (adapterId, targetId) in QueryActiveTargets())
        {
            var info = new AdvancedColorInfo
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DeviceInfoGetAdvancedColorInfo,
                    size = Marshal.SizeOf<AdvancedColorInfo>(),
                    adapterId = adapterId,
                    id = targetId,
                },
            };

            if (DisplayConfigGetDeviceInfo(ref info) != 0) continue;
            bool supported = (info.value & 0x1) != 0;
            if (!supported) continue;
            return (info.value & 0x2) != 0;
        }
        return null;
    }

    /// <summary>Liga/desliga o HDR em todas as telas que o suportam.</summary>
    public bool SetHdrEnabled(bool enabled)
    {
        bool any = false;
        foreach (var (adapterId, targetId) in QueryActiveTargets())
        {
            var query = new AdvancedColorInfo
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DeviceInfoGetAdvancedColorInfo,
                    size = Marshal.SizeOf<AdvancedColorInfo>(),
                    adapterId = adapterId,
                    id = targetId,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref query) != 0) continue;
            if ((query.value & 0x1) == 0) continue;   // esta tela não suporta HDR

            var state = new SetAdvancedColorState
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DeviceInfoSetAdvancedColorState,
                    size = Marshal.SizeOf<SetAdvancedColorState>(),
                    adapterId = adapterId,
                    id = targetId,
                },
                value = enabled ? 1u : 0u,
            };

            any |= DisplayConfigSetDeviceInfo(ref state) == 0;
        }
        return any;
    }

    /// <summary>Alvos (monitores) ativos na configuração de vídeo atual.</summary>
    private static IEnumerable<(Luid adapterId, uint targetId)> QueryActiveTargets()
    {
        var targets = new List<(Luid, uint)>();
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0)
                return targets;

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];

            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return targets;

            for (int i = 0; i < pathCount; i++)
                targets.Add((paths[i].targetInfo.adapterId, paths[i].targetInfo.id));
        }
        catch { }
        return targets;
    }

    // =====================================================================================
    //  Interop
    // =====================================================================================

    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const int CdsTest = 0x00000002;
    private const int DmBitsPerPel = 0x00040000;
    private const int DmPelsWidth = 0x00080000;
    private const int DmPelsHeight = 0x00100000;
    private const int DmDisplayFrequency = 0x00400000;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const uint QdcOnlyActivePaths = 2;
    private const int DeviceInfoGetAdvancedColorInfo = 9;
    private const int DeviceInfoSetAdvancedColorState = 10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DisplayConfigRational refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo sourceInfo;
        public DisplayConfigPathTargetInfo targetInfo;
        public uint flags;
    }

    // O conteúdo do modo não nos interessa — só o tamanho precisa bater com o da API.
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint infoType;
        public uint id;
        public Luid adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public uint[] data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader { public int type; public int size; public Luid adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader header;
        public uint value;             // bit 0 = suportado, bit 1 = ligado
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SetAdvancedColorState { public DisplayConfigDeviceInfoHeader header; public uint value; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DisplayDevice info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, IntPtr hwnd, int flags, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref SetAdvancedColorState requestPacket);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint arraySize, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr handle, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr handle, uint brightness);
}
