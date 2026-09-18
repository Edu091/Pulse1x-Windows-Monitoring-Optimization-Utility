using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Profiles;

/// <summary>Um dispositivo de áudio do Windows, como aparece no seletor do editor de perfil.</summary>
public record AudioDevice(string Id, string Name, bool IsDefault, bool IsInput);

/// <summary>
/// Volume, mudo e dispositivo padrão de áudio, pela API oficial do Windows (Core Audio). É a mesma
/// interface usada pelo mixer do sistema, então o que o perfil altera aparece imediatamente no
/// ícone de som — e volta ao normal na restauração, porque o valor anterior vai para o snapshot.
///
/// A troca do dispositivo padrão usa IPolicyConfig: não há API pública para isso (nem o painel de
/// Configurações usa outra coisa), mas ela existe desde o Windows 7 e é estável. Se um dia não
/// estiver disponível, a chamada apenas devolve false e o resto do perfil continua funcionando.
/// </summary>
public class AudioService
{
    // =====================================================================================
    //  Volume / mudo (dispositivo de saída padrão)
    // =====================================================================================

    /// <summary>Volume principal atual, de 0 a 100 (null se não houver dispositivo de saída).</summary>
    public int? GetVolume()
    {
        try
        {
            var volume = GetEndpointVolume(EDataFlow.Render);
            if (volume is null) return null;
            try
            {
                volume.GetMasterVolumeLevelScalar(out float scalar);
                return (int)Math.Round(scalar * 100);
            }
            finally { Marshal.ReleaseComObject(volume); }
        }
        catch { return null; }
    }

    /// <summary>Define o volume principal (0 a 100).</summary>
    public bool SetVolume(int percent)
    {
        try
        {
            var volume = GetEndpointVolume(EDataFlow.Render);
            if (volume is null) return false;
            try
            {
                var guid = Guid.Empty;
                volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref guid);
                return true;
            }
            finally { Marshal.ReleaseComObject(volume); }
        }
        catch { return false; }
    }

    public bool? GetMuted()
    {
        try
        {
            var volume = GetEndpointVolume(EDataFlow.Render);
            if (volume is null) return null;
            try
            {
                volume.GetMute(out bool muted);
                return muted;
            }
            finally { Marshal.ReleaseComObject(volume); }
        }
        catch { return null; }
    }

    public bool SetMuted(bool muted)
    {
        try
        {
            var volume = GetEndpointVolume(EDataFlow.Render);
            if (volume is null) return false;
            try
            {
                var guid = Guid.Empty;
                volume.SetMute(muted, ref guid);
                return true;
            }
            finally { Marshal.ReleaseComObject(volume); }
        }
        catch { return false; }
    }

    // =====================================================================================
    //  Dispositivos
    // =====================================================================================

    /// <summary>Lista os dispositivos ativos de saída (e de entrada, se pedido).</summary>
    public IReadOnlyList<AudioDevice> ListDevices(bool input = false)
    {
        var result = new List<AudioDevice>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var flow = input ? EDataFlow.Capture : EDataFlow.Render;

            string? defaultId = null;
            try
            {
                enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var defaultDevice);
                if (defaultDevice is not null)
                {
                    defaultDevice.GetId(out defaultId);
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }
            catch { }

            enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection);
            collection.GetCount(out uint count);

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    collection.Item(i, out device);
                    device.GetId(out string id);
                    result.Add(new AudioDevice(id, ReadFriendlyName(device) ?? id, id == defaultId, input));
                }
                catch { }
                finally { if (device is not null) Marshal.ReleaseComObject(device); }
            }
        }
        catch { }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }

        return result;
    }

    /// <summary>Id do dispositivo padrão atual (para guardar no snapshot antes de trocar).</summary>
    public string? GetDefaultDeviceId(bool input = false)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(input ? EDataFlow.Capture : EDataFlow.Render, ERole.Multimedia, out var device);
            if (device is null) return null;
            try
            {
                device.GetId(out string id);
                return id;
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        catch { return null; }
        finally { if (enumerator is not null) Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>
    /// Define o dispositivo padrão. Os três papéis (Console, Multimídia e Comunicação) são
    /// definidos juntos — é o que o Windows faz quando você escolhe um dispositivo nas
    /// Configurações, e evita o caso estranho de o jogo sair pelo headset e o chat pelo alto-falante.
    /// </summary>
    public bool SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        IPolicyConfig? config = null;
        try
        {
            config = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                config.SetDefaultEndpoint(deviceId, role);
            return true;
        }
        catch { return false; }
        finally { if (config is not null) Marshal.ReleaseComObject(config); }
    }

    // =====================================================================================
    //  Interop
    // =====================================================================================

    private const uint DeviceStateActive = 0x00000001;
    private const uint StgmRead = 0x00000000;

    private static IAudioEndpointVolume? GetEndpointVolume(EDataFlow flow)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device);
            if (device is null) return null;

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object instance);
            return instance as IAudioEndpointVolume;
        }
        catch { return null; }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private const uint ClsCtxAll = 0x17;

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            device.OpenPropertyStore(StgmRead, out store);
            var key = PropertyKeyFriendlyName;
            store.GetValue(ref key, out PropVariant value);
            try { return value.AsString(); }
            finally { value.Clear(); }
        }
        catch { return null; }
        finally { if (store is not null) Marshal.ReleaseComObject(store); }
    }

    private static readonly PropertyKey PropertyKeyFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
        public PropertyKey(Guid formatId, int propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    /// <summary>
    /// Versão mínima de PROPVARIANT: só precisamos ler strings (o nome do dispositivo). O campo de
    /// dados é lido como ponteiro quando o tipo é VT_LPWSTR (31).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VarType;
        private readonly ushort _reserved1;
        private readonly ushort _reserved2;
        private readonly ushort _reserved3;
        public IntPtr Data;
        private readonly IntPtr _data2;

        public string? AsString() => VarType == 31 ? Marshal.PtrToStringUni(Data) : null;

        public void Clear()
        {
            try { PropVariantClear(ref this); } catch { }
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice(string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>
    /// Interface interna do Windows usada para escolher o dispositivo padrão. A ordem dos métodos é
    /// parte do contrato COM — só o último (SetDefaultEndpoint/SetEndpointVisibility) nos interessa,
    /// mas todos precisam estar declarados para a tabela virtual bater.
    /// </summary>
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string deviceName, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string deviceName, bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string deviceName);
        [PreserveSig] int SetDeviceFormat(string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string deviceName, bool defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string deviceName, IntPtr period);
        [PreserveSig] int GetShareMode(string deviceName, IntPtr mode);
        [PreserveSig] int SetShareMode(string deviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string deviceName, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue(string deviceName, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility(string deviceName, bool visible);
    }
}
