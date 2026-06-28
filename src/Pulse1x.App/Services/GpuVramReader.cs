using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>
/// Win32_VideoController.AdapterRAM é um DWORD de 32 bits e estoura (overflow) em GPUs com
/// 4 GB+ de VRAM, reportando valores errados (ex.: uma 8 GB aparecendo como poucos MB).
///
/// A fonte mais confiável é HKLM\SOFTWARE\Microsoft\DirectX\{GUID}\DedicatedVideoMemory:
/// é o valor que o próprio DirectX consulta do driver a cada boot, então reflete a VRAM real
/// detectada na sessão atual. HardwareInformation.qwMemorySize (chave do driver) é usado como
/// fallback, mas pode ficar desatualizado em subchaves de versões antigas do driver.
/// </summary>
internal static class GpuVramReader
{
    private const string DirectXKeyPath = @"SOFTWARE\Microsoft\DirectX";
    private const string ClassKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static ulong ReadVramBytes(string? gpuName)
    {
        ulong fromDirectX = ReadFromDirectX(gpuName);
        if (fromDirectX > 0) return fromDirectX;

        return ReadFromDeviceClass(gpuName);
    }

    private static ulong ReadFromDirectX(string? gpuName)
    {
        try
        {
            using var dxKey = Registry.LocalMachine.OpenSubKey(DirectXKeyPath);
            if (dxKey is null) return 0;

            ulong? fallback = null;
            foreach (var subKeyName in dxKey.GetSubKeyNames())
            {
                // As subchaves de adaptador são GUIDs; ignora outras entradas (ex.: "UserGDIAcceleration").
                if (!subKeyName.StartsWith('{')) continue;

                using var subKey = dxKey.OpenSubKey(subKeyName);
                if (subKey?.GetValue("DedicatedVideoMemory") is not { } dvm) continue;

                ulong bytes;
                try { bytes = Convert.ToUInt64(dvm); } catch { continue; }
                if (bytes == 0) continue;

                fallback ??= bytes;

                var desc = subKey.GetValue("Description") as string;
                if (!string.IsNullOrWhiteSpace(gpuName) && !string.IsNullOrWhiteSpace(desc) &&
                    desc.Contains(gpuName, StringComparison.OrdinalIgnoreCase))
                    return bytes;
            }

            return fallback ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static ulong ReadFromDeviceClass(string? gpuName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(ClassKeyPath);
            if (classKey is null) return 0;

            ulong? fallback = null;
            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                using var subKey = classKey.OpenSubKey(subKeyName);
                if (subKey?.GetValue("HardwareInformation.qwMemorySize") is not { } qw) continue;

                ulong bytes;
                try { bytes = Convert.ToUInt64(qw); } catch { continue; }
                if (bytes == 0) continue;

                fallback ??= bytes;

                var desc = subKey.GetValue("DriverDesc") as string;
                if (!string.IsNullOrWhiteSpace(gpuName) && !string.IsNullOrWhiteSpace(desc) &&
                    desc.Contains(gpuName, StringComparison.OrdinalIgnoreCase))
                    return bytes;
            }

            return fallback ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
