namespace Pulse1x.App.Services;

public record RamReading(double UsedGb, double TotalGb, double UsagePercent);
public record DiskReading(string Name, string Label, string TypeText, double UsedGb, double FreeGb, double TotalGb, double UsagePercent);
public record NetworkReading(double DownloadKbps, double UploadKbps);

public interface ISystemMetricsService
{
    RamReading ReadRam();
    IReadOnlyList<DiskReading> ReadDisks();
    NetworkReading ReadNetwork(TimeSpan elapsedSinceLastRead);
}
