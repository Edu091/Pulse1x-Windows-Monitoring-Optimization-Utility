using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Pulse1x.App.Services.Recording;

/// <summary>
/// Encontra (ou baixa) o ffmpeg que faz a captura e a codificação, e descobre o que ele consegue
/// fazer nesta máquina: captura por Desktop Duplication (ddagrab) e quais encoders de GPU abrem de
/// verdade — um encoder compilado no ffmpeg não quer dizer que a placa ou o driver o suportam.
/// </summary>
public sealed class FfmpegService
{
    /// <summary>Build estático completo (inclui NVENC, AMF, QuickSync, x264, x265 e SVT-AV1).</summary>
    private static readonly string[] DownloadUrls =
    {
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
    };

    public static string InstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse1x", "ffmpeg");

    private readonly Func<string?> _customPath;
    private readonly Dictionary<string, bool> _encoderCache = new(StringComparer.Ordinal);
    private string? _probedPath;

    public FfmpegService(Func<string?> customPath) => _customPath = customPath;

    /// <summary>Caminho do ffmpeg.exe encontrado, ou null.</summary>
    public string? ExePath { get; private set; }

    public bool HasDdagrab { get; private set; }
    public string? Version { get; private set; }

    /// <summary>Procura o ffmpeg nos lugares de sempre. Devolve true quando achou.</summary>
    public bool Locate()
    {
        ExePath = null;
        foreach (var candidate in Candidates())
        {
            string? real = ResolveRealFile(candidate);
            if (real is null) continue;
            ExePath = real;
            break;
        }
        if (ExePath != _probedPath) _probedPath = null;
        RecordingLog.Write($"ffmpeg: {ExePath ?? "not found"}");
        return ExePath is not null;
    }

    /// <summary>
    /// Segue links simbólicos até o arquivo de verdade. O winget (e o scoop) põem no PATH um link
    /// criado pelo usuário, e o Windows impede um processo elevado — o Pulse1x roda como
    /// administrador — de executar através dele ("ponto de montagem não confiável"). Executando o
    /// .exe final, a proteção não entra em jogo. Devolve null se o arquivo não existe.
    /// </summary>
    public static string? ResolveRealFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.LinkTarget is null) return info.FullName;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is { Exists: true } ? target.FullName : null;
        }
        catch { return null; }
    }

    private IEnumerable<string> Candidates()
    {
        var custom = _customPath();
        if (!string.IsNullOrWhiteSpace(custom)) yield return custom!;
        yield return Path.Combine(InstallFolder, "ffmpeg.exe");

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"); } catch { continue; }
            yield return candidate;
        }

        // O app roda elevado: o PATH do usuário pode não ter sido herdado. Os instaladores comuns:
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        foreach (var found in SafeFind(Path.Combine(local, "Microsoft", "WinGet", "Packages"), "*FFmpeg*"))
            yield return found;
        yield return Path.Combine(profile, "scoop", "shims", "ffmpeg.exe");
        yield return @"C:\ProgramData\chocolatey\bin\ffmpeg.exe";
        yield return @"C:\ffmpeg\bin\ffmpeg.exe";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe");
    }

    private static IEnumerable<string> SafeFind(string root, string dirPattern)
    {
        string[] dirs;
        try { dirs = Directory.Exists(root) ? Directory.GetDirectories(root, dirPattern) : Array.Empty<string>(); }
        catch { yield break; }
        foreach (var dir in dirs)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var f in files) yield return f;
        }
    }

    /// <summary>
    /// Lê a versão e se há ddagrab. Rápido (um processo); refeito só quando o caminho muda.
    /// </summary>
    public async Task ProbeAsync()
    {
        if (ExePath is null) { HasDdagrab = false; Version = null; return; }
        if (_probedPath == ExePath) return;
        _encoderCache.Clear();

        var version = await RunAsync(ExePath, "-hide_banner -version", TimeSpan.FromSeconds(8));
        var firstLine = version.Output.Split('\n').FirstOrDefault() ?? "";
        Version = firstLine.StartsWith("ffmpeg version ", StringComparison.Ordinal)
            ? firstLine["ffmpeg version ".Length..].Split(' ')[0]
            : null;

        var filters = await RunAsync(ExePath, "-hide_banner -filters", TimeSpan.FromSeconds(8));
        HasDdagrab = filters.Output.Contains(" ddagrab ", StringComparison.Ordinal);
        RecordingLog.Write($"ffmpeg version {Version ?? "?"} (exit {version.ExitCode}), ddagrab={HasDdagrab}"
            + (version.ExitCode == 0 ? "" : " | " + version.Output.Trim()));
        // Só fica em cache quando o ffmpeg rodou: uma falha ao abrir o processo é tentada de novo.
        _probedPath = version.ExitCode == 0 ? ExePath : null;
    }

    /// <summary>Testa se o encoder abre de verdade (codifica um quadro sintético).</summary>
    public async Task<bool> EncoderWorksAsync(string encoder)
    {
        if (ExePath is null) return false;
        lock (_encoderCache)
            if (_encoderCache.TryGetValue(encoder, out var cached)) return cached;

        string pixFmt = encoder is "libx265" or "libsvtav1" or "libx264" ? "yuv420p" : "nv12";
        var result = await RunAsync(ExePath,
            $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=640x360:r=30:d=0.2 -frames:v 3 -pix_fmt {pixFmt} -c:v {encoder} -f null -",
            TimeSpan.FromSeconds(15));
        bool ok = result.ExitCode == 0;
        RecordingLog.Write($"encoder {encoder}: {(ok ? "ok" : "fails (exit " + result.ExitCode + ") " + LastLines(result.Output))}");
        // Processo que nem abriu (-1) não diz nada sobre o encoder: não guarda.
        if (result.ExitCode != -1)
            lock (_encoderCache) _encoderCache[encoder] = ok;
        return ok;
    }

    /// <summary>
    /// Baixa o ffmpeg (só o ffmpeg.exe sai do zip) para a pasta do Pulse1x. Progresso de 0 a 1.
    /// </summary>
    public async Task<string> InstallAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(InstallFolder);
        string zipPath = Path.Combine(InstallFolder, "download.zip");
        Exception? last = null;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse1x");

        foreach (var url in DownloadUrls)
        {
            try
            {
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    long? total = response.Content.Headers.ContentLength;
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var target = File.Create(zipPath);
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if (total > 0) progress?.Report(Math.Min(0.98, (double)done / total.Value));
                    }
                }

                string exePath = Path.Combine(InstallFolder, "ffmpeg.exe");
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("ffmpeg.exe not found in archive");
                    string tmp = exePath + ".tmp";
                    entry.ExtractToFile(tmp, overwrite: true);
                    File.Move(tmp, exePath, overwrite: true);
                }
                File.Delete(zipPath);
                progress?.Report(1);
                ExePath = exePath;
                _probedPath = null;
                return exePath;
            }
            catch (OperationCanceledException) { TryDelete(zipPath); throw; }
            catch (Exception ex) { last = ex; TryDelete(zipPath); }
        }
        throw last ?? new InvalidOperationException("download failed");
    }

    public static string LastLines(string text, int count = 3) =>
        string.Join(" / ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(count));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public readonly record struct RunResult(int ExitCode, string Output);

    /// <summary>Roda o ffmpeg até o fim e devolve stdout+stderr juntos.</summary>
    public static async Task<RunResult> RunAsync(string exe, string args, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        try { process.Start(); }
        catch (Exception ex) { return new RunResult(-1, ex.Message); }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            lock (output) return new RunResult(-2, output.ToString());
        }
        process.WaitForExit(); // esvazia os eventos de saída pendentes
        lock (output) return new RunResult(process.ExitCode, output.ToString());
    }
}
