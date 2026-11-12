using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulse1x.App.Services.Recording;

/// <summary>Um vídeo gravado pelo Pulse1x.</summary>
public sealed record RecordingFile(string Path, DateTime Created, long SizeBytes);

/// <summary>
/// As gravações do Pulse1x na pasta escolhida (só os arquivos "Pulse1x_*.mp4/mkv", para não listar
/// os outros vídeos da pasta Downloads), com miniatura e duração em cache.
/// </summary>
public static class RecordingLibrary
{
    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    public static string CacheFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse1x", "RecordingThumbs");

    public static List<RecordingFile> List(string folder, int max = 60)
    {
        try
        {
            if (!Directory.Exists(folder)) return new List<RecordingFile>();
            return new DirectoryInfo(folder)
                .EnumerateFiles("Pulse1x_*")
                .Where(f => RecordingCommandBuilder.IsRecordingFile(f.FullName))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(max)
                .Select(f => new RecordingFile(f.FullName, f.LastWriteTime, f.Length))
                .ToList();
        }
        catch { return new List<RecordingFile>(); }
    }

    /// <summary>
    /// Gera (ou reaproveita) a miniatura e lê a duração. Devolve (jpg ou null, duração ou null).
    /// </summary>
    public static async Task<(string? Thumbnail, TimeSpan? Duration)> DescribeAsync(string ffmpeg, RecordingFile file)
    {
        Directory.CreateDirectory(CacheFolder);
        string key = Hash($"{file.Path}|{file.SizeBytes}|{file.Created.Ticks}");
        string jpg = System.IO.Path.Combine(CacheFolder, key + ".jpg");
        string meta = System.IO.Path.Combine(CacheFolder, key + ".txt");

        TimeSpan? duration = null;
        if (File.Exists(meta) && double.TryParse(await File.ReadAllTextAsync(meta), NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
            duration = TimeSpan.FromSeconds(secs);
        if (File.Exists(jpg) && duration is not null) return (jpg, duration);

        var result = await FfmpegService.RunAsync(ffmpeg, RecordingCommandBuilder.ThumbnailArgs(file.Path, jpg, 1), TimeSpan.FromSeconds(20));
        duration ??= ParseDuration(result.Output);
        if (!File.Exists(jpg))
            await FfmpegService.RunAsync(ffmpeg, RecordingCommandBuilder.ThumbnailArgs(file.Path, jpg, 0), TimeSpan.FromSeconds(20));
        if (duration is { } d)
            await File.WriteAllTextAsync(meta, d.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        return (File.Exists(jpg) ? jpg : null, duration);
    }

    public static TimeSpan? ParseDuration(string ffmpegOutput)
    {
        var m = DurationRegex.Match(ffmpegOutput);
        if (!m.Success) return null;
        return TimeSpan.FromHours(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
             + TimeSpan.FromMinutes(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
             + TimeSpan.FromSeconds(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Manda para a Lixeira (dá para recuperar). Devolve false se o Windows recusou.</summary>
    public static bool Delete(string path)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return !File.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>Tira do cache as miniaturas de vídeos que não existem mais.</summary>
    public static void PruneCache(IEnumerable<RecordingFile> alive)
    {
        try
        {
            if (!Directory.Exists(CacheFolder)) return;
            var keep = alive.Select(f => Hash($"{f.Path}|{f.SizeBytes}|{f.Created.Ticks}")).ToHashSet();
            foreach (var file in Directory.EnumerateFiles(CacheFolder))
                if (!keep.Contains(System.IO.Path.GetFileNameWithoutExtension(file))
                    && File.GetLastWriteTime(file) < DateTime.Now.AddDays(-30))
                    File.Delete(file);
        }
        catch { }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB",
    };

    private static string Hash(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant())))[..20];
}
