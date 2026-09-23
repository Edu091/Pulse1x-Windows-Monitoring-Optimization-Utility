namespace Pulse1x.App.Services;

public sealed record InputPollingEstimate(double Hertz, string Profile);

public static class InputPollingEstimator
{
    public static InputPollingEstimate? EstimateKeyboard(string deviceName, string devicePath)
    {
        bool gk68MixReceiver = devicePath.Contains("VID_3151&PID_5038", StringComparison.OrdinalIgnoreCase)
            && deviceName.Contains("2.4G Wireless Keyboard", StringComparison.OrdinalIgnoreCase);
        bool namedGk68Mix = deviceName.Contains("GK68", StringComparison.OrdinalIgnoreCase)
            && deviceName.Contains("MIX", StringComparison.OrdinalIgnoreCase);

        return gk68MixReceiver || namedGk68Mix
            ? new(8000, "GK68 Mix HE 2.4 GHz")
            : null;
    }
}
