using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pulse1x.App.Services.GameHub;

internal static class GamepadReceiverDetector
{
    private const int CrSuccess = 0;
    private static readonly (string HardwareId, string Name)[] KnownReceivers =
    {
        ("VID_3537&PID_103E", "GameSir T3 Pro 2.4 GHz"),
    };

    internal static string? DetectKnownReceiver()
    {
        foreach (var receiver in KnownReceivers)
        {
            string registryPath = $@"SYSTEM\CurrentControlSet\Enum\USB\{receiver.HardwareId}";
            using var deviceKey = Registry.LocalMachine.OpenSubKey(registryPath);
            if (deviceKey is null) continue;

            foreach (string instance in deviceKey.GetSubKeyNames())
            {
                string deviceId = $@"USB\{receiver.HardwareId}\{instance}";
                uint deviceNode = 0;
                if (CM_Locate_DevNodeW(ref deviceNode, deviceId, 0) == CrSuccess)
                    return receiver.Name;
            }
        }
        return null;
    }

    internal static string? NameForHardwareId(string hardwareId) =>
        KnownReceivers.FirstOrDefault(receiver =>
            hardwareId.Contains(receiver.HardwareId, StringComparison.OrdinalIgnoreCase)).Name;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(ref uint deviceNode, string deviceId, uint flags);
}
