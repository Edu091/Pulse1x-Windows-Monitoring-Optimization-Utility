namespace Pulse1x.App.Services;

public record RamReading(double UsedGb, double TotalGb, double UsagePercent);
// IsInternal: independente do idioma (TypeText é traduzido e não serve para comparar).
public record DiskReading(string Name, string Label, string TypeText, double UsedGb, double FreeGb, double TotalGb, double UsagePercent,
    bool IsInternal = false);
public record NetworkReading(double DownloadKbps, double UploadKbps);

public interface ISystemMetricsService
{
    RamReading ReadRam();
    IReadOnlyList<DiskReading> ReadDisks();
    NetworkReading ReadNetwork(TimeSpan elapsedSinceLastRead);
}
