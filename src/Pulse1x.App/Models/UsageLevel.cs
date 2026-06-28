namespace Pulse1x.App.Models;

public enum UsageLevel
{
    Normal,
    Elevado,
    Critico
}

public static class UsageLevelHelper
{
    public static UsageLevel FromPercent(double percent, double warnThreshold = 60, double criticalThreshold = 85)
    {
        if (percent >= criticalThreshold) return UsageLevel.Critico;
        if (percent >= warnThreshold) return UsageLevel.Elevado;
        return UsageLevel.Normal;
    }
}
