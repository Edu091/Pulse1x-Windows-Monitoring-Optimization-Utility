using System.Globalization;
using System.IO;

namespace Pulse1x.App.Services.Recording;

public enum CaptureMethod { Ddagrab, Gdigrab }

/// <summary>Retângulo de captura em pixels físicos (relativo ao monitor) e tamanho final do vídeo.</summary>
public readonly record struct CaptureGeometry(int X, int Y, int Width, int Height, int OutWidth, int OutHeight, bool IsFullMonitor)
{
    public bool Scaled => OutWidth != Width || OutHeight != Height;
}

/// <summary>Uma faixa de áudio bruta (PCM sem cabeçalho) gravada ao lado do vídeo.</summary>
public sealed record AudioTrack(string Path, string SampleFormat, int SampleRate, int Channels, double Volume);

/// <summary>
/// Monta as linhas de comando do ffmpeg. É código puro (sem processo, sem disco) para os testes
/// poderem conferir cada combinação de encoder, qualidade, área e escala.
/// </summary>
public static class RecordingCommandBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Nome do encoder do ffmpeg para o codec e o fabricante ("NVENC", "AMF", "QSV" ou "CPU").</summary>
    public static string EncoderName(string codec, string vendor) => (codec, vendor) switch
    {
        ("HEVC", "NVENC") => "hevc_nvenc",
        ("HEVC", "AMF") => "hevc_amf",
        ("HEVC", "QSV") => "hevc_qsv",
        ("HEVC", _) => "libx265",
        ("AV1", "NVENC") => "av1_nvenc",
        ("AV1", "AMF") => "av1_amf",
        ("AV1", "QSV") => "av1_qsv",
        ("AV1", _) => "libsvtav1",
        (_, "NVENC") => "h264_nvenc",
        (_, "AMF") => "h264_amf",
        (_, "QSV") => "h264_qsv",
        _ => "libx264",
    };

    public static string VendorOf(string encoder) =>
        encoder.EndsWith("_nvenc", StringComparison.Ordinal) ? "NVENC"
        : encoder.EndsWith("_amf", StringComparison.Ordinal) ? "AMF"
        : encoder.EndsWith("_qsv", StringComparison.Ordinal) ? "QSV"
        : "CPU";

    /// <summary>
    /// Ordem de preferência no modo Automático: primeiro a GPU onde o monitor está ligado (os
    /// quadros já estão na memória dela), depois as outras, e a CPU por último.
    /// </summary>
    public static IEnumerable<string> AutoVendorOrder(uint monitorVendor)
    {
        var first = monitorVendor switch
        {
            CaptureMonitor.VendorNvidia => "NVENC",
            CaptureMonitor.VendorAmd => "AMF",
            CaptureMonitor.VendorIntel => "QSV",
            _ => null,
        };
        if (first is not null) yield return first;
        foreach (var v in new[] { "NVENC", "AMF", "QSV" })
            if (v != first) yield return v;
        yield return "CPU";
    }

    /// <summary>Calcula a área (cortada ao monitor, com lados pares) e o tamanho final do vídeo.</summary>
    public static CaptureGeometry Geometry(RecordingSettings s, int monitorWidth, int monitorHeight)
    {
        int x = 0, y = 0, w = monitorWidth, h = monitorHeight;
        bool full = true;
        if (s.CaptureTarget == "Region" && s.RegionWidth >= 16 && s.RegionHeight >= 16)
        {
            x = Math.Clamp(s.RegionX, 0, Math.Max(0, monitorWidth - 16));
            y = Math.Clamp(s.RegionY, 0, Math.Max(0, monitorHeight - 16));
            w = Math.Min(s.RegionWidth, monitorWidth - x);
            h = Math.Min(s.RegionHeight, monitorHeight - y);
            full = x == 0 && y == 0 && w == monitorWidth && h == monitorHeight;
        }
        // YUV 4:2:0 exige lados pares.
        w &= ~1;
        h &= ~1;

        int outW = w, outH = h;
        if (int.TryParse(s.Resolution, out int maxH) && h > maxH)
        {
            outH = maxH & ~1;
            outW = (int)Math.Round(w * (double)maxH / h) & ~1;
        }
        return new CaptureGeometry(x, y, w, h, Math.Max(2, outW), Math.Max(2, outH), full);
    }

    /// <summary>
    /// Os quadros do ddagrab ficam na GPU. O NVENC os lê ali mesmo, sem cópia; os demais encoders
    /// recebem uma cópia na memória do sistema (hwdownload).
    /// </summary>
    public static bool UsesDirectGpuFrames(CaptureMethod method, string encoder, uint monitorVendor, bool scaled) =>
        method == CaptureMethod.Ddagrab && !scaled && VendorOf(encoder) == "NVENC" && monitorVendor == CaptureMonitor.VendorNvidia;

    /// <summary>Comando de um segmento de vídeo (sem áudio: o áudio é gravado à parte e juntado no fim).</summary>
    public static string SegmentArgs(RecordingSettings s, CaptureMonitor monitor, CaptureGeometry g,
        CaptureMethod method, string encoder, string outputPath)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "info", "-stats_period", "0.5", "-y" };
        bool direct = UsesDirectGpuFrames(method, encoder, monitor.VendorId, g.Scaled);
        string pixFmt = encoder is "libx264" or "libx265" or "libsvtav1" ? "yuv420p" : "nv12";

        if (method == CaptureMethod.Ddagrab)
        {
            var src = $"ddagrab=output_idx={monitor.OutputIndex}:framerate={s.Fps}:draw_mouse={(s.DrawMouse ? 1 : 0)}";
            if (!g.IsFullMonitor)
                src += $":offset_x={g.X}:offset_y={g.Y}:video_size={g.Width}x{g.Height}";

            // Direto: o NVENC recebe a textura da GPU como está (sem escala; o scale_d3d11 não
            // aceita o BGRA do ddagrab). Nos demais casos, cópia para a memória e conversão.
            var chain = new List<string>();
            if (!direct)
            {
                chain.Add("hwdownload");
                chain.Add("format=bgra");
                if (g.Scaled) chain.Add($"scale={g.OutWidth}:{g.OutHeight}:flags=bicubic");
                chain.Add($"format={pixFmt}");
            }
            string graph = src + (chain.Count > 0 ? "," + string.Join(",", chain) : "") + "[v]";

            args.AddRange(new[]
            {
                "-init_hw_device", $"d3d11va=pulsecap:{monitor.AdapterIndex}",
                "-filter_hw_device", "pulsecap",
                "-filter_complex", Quote(graph),
                "-map", Quote("[v]"),
            });
        }
        else
        {
            args.AddRange(new[]
            {
                "-f", "gdigrab", "-framerate", s.Fps.ToString(Inv),
                "-draw_mouse", s.DrawMouse ? "1" : "0",
                "-offset_x", (monitor.Left + g.X).ToString(Inv),
                "-offset_y", (monitor.Top + g.Y).ToString(Inv),
                "-video_size", $"{g.Width}x{g.Height}",
                "-i", "desktop",
            });
            var vf = new List<string>();
            if (g.Scaled) vf.Add($"scale={g.OutWidth}:{g.OutHeight}:flags=bicubic");
            vf.Add($"format={pixFmt}");
            args.AddRange(new[] { "-vf", Quote(string.Join(",", vf)) });
        }

        args.AddRange(EncoderArgs(s, encoder));
        args.Add("-an");
        args.Add(Quote(outputPath));
        return string.Join(" ", args);
    }

    /// <summary>Parâmetros do encoder: qualidade constante (padrão) ou taxa fixa (Personalizada).</summary>
    public static IEnumerable<string> EncoderArgs(RecordingSettings s, string encoder)
    {
        var a = new List<string> { "-c:v", encoder, "-g", (s.Fps * 2).ToString(Inv) };
        string vendor = VendorOf(encoder);
        int qi = s.Quality switch { "Low" => 0, "Medium" => 1, "Ultra" => 3, _ => 2 };

        if (s.Quality == "Custom")
        {
            int m = s.CustomBitrateMbps;
            string rate = $"{m}M", max = $"{m * 3 / 2}M", buf = $"{m * 2}M";
            switch (vendor)
            {
                case "NVENC": a.AddRange(new[] { "-preset", "p4", "-tune", "hq", "-rc", "vbr" }); break;
                case "AMF": a.AddRange(new[] { "-quality", "balanced", "-rc", "vbr_peak" }); break;
                case "QSV": a.AddRange(new[] { "-preset", "faster" }); break;
                default: a.AddRange(CpuPreset(encoder)); break;
            }
            a.AddRange(new[] { "-b:v", rate, "-maxrate", max, "-bufsize", buf });
            return a;
        }

        switch (vendor)
        {
            case "NVENC":
                int cq = new[] { 32, 27, 23, 19 }[qi];
                a.AddRange(new[] { "-preset", "p4", "-tune", "hq", "-rc", "vbr", "-cq", cq.ToString(Inv), "-b:v", "0" });
                break;
            case "AMF":
                int qp = new[] { 32, 27, 23, 19 }[qi];
                a.AddRange(new[] { "-quality", "balanced", "-rc", "cqp", "-qp_i", qp.ToString(Inv), "-qp_p", qp.ToString(Inv) });
                break;
            case "QSV":
                int gq = new[] { 32, 27, 23, 19 }[qi];
                a.AddRange(new[] { "-preset", "faster", "-global_quality", gq.ToString(Inv) });
                break;
            default:
                int crf = encoder switch
                {
                    "libx265" => new[] { 30, 27, 24, 20 }[qi],
                    "libsvtav1" => new[] { 42, 36, 30, 26 }[qi],
                    _ => new[] { 28, 24, 21, 18 }[qi],
                };
                a.AddRange(CpuPreset(encoder));
                a.AddRange(new[] { "-crf", crf.ToString(Inv) });
                break;
        }
        return a;
    }

    private static string[] CpuPreset(string encoder) => encoder switch
    {
        "libx265" => new[] { "-preset", "superfast" },
        "libsvtav1" => new[] { "-preset", "10" },
        _ => new[] { "-preset", "veryfast" },
    };

    /// <summary>Lista de segmentos no formato do demuxer concat.</summary>
    public static string ConcatList(IEnumerable<string> segments) =>
        string.Join("\n", segments.Select(p => "file '" + p.Replace("\\", "/").Replace("'", "'\\''") + "'")) + "\n";

    /// <summary>
    /// Junta os segmentos (sem recodificar o vídeo) e mistura as faixas de áudio num arquivo final.
    /// </summary>
    public static string FinalizeArgs(string concatListPath, IReadOnlyList<AudioTrack> audio, int audioOffsetMs,
        string codec, string container, string outputPath)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", Quote(concatListPath) };
        foreach (var t in audio)
            args.AddRange(new[] { "-f", t.SampleFormat, "-ar", t.SampleRate.ToString(Inv), "-ac", t.Channels.ToString(Inv), "-i", Quote(t.Path) });

        args.AddRange(new[] { "-map", "0:v" });
        if (audio.Count > 0)
        {
            var parts = new List<string>();
            for (int i = 0; i < audio.Count; i++)
            {
                var chain = new List<string> { $"volume={audio[i].Volume.ToString("0.###", Inv)}" };
                if (audioOffsetMs > 0) chain.Add($"adelay=delays={audioOffsetMs}:all=1");
                else if (audioOffsetMs < 0) chain.Add($"atrim=start={(-audioOffsetMs / 1000.0).ToString("0.###", Inv)},asetpts=PTS-STARTPTS");
                parts.Add($"[{i + 1}:a]{string.Join(",", chain)}[a{i}]");
            }
            string mix = audio.Count == 1
                ? "[a0]apad[aout]"
                : string.Concat(Enumerable.Range(0, audio.Count).Select(i => $"[a{i}]"))
                  + $"amix=inputs={audio.Count}:duration=longest:normalize=0,apad[aout]";
            args.AddRange(new[] { "-filter_complex", Quote(string.Join(";", parts) + ";" + mix), "-map", Quote("[aout]"),
                "-c:a", "aac", "-b:a", "192k", "-shortest" });
        }
        args.AddRange(new[] { "-c:v", "copy" });
        if (codec == "HEVC" && container == "mp4") args.AddRange(new[] { "-tag:v", "hvc1" });
        if (container == "mp4") args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(Quote(outputPath));
        return string.Join(" ", args);
    }

    public static string ThumbnailArgs(string videoPath, string jpgPath, double seekSeconds) =>
        $"-hide_banner -y -ss {seekSeconds.ToString("0.##", Inv)} -i {Quote(videoPath)} -frames:v 1 -vf scale=320:-2 -q:v 4 {Quote(jpgPath)}";

    /// <summary>Nome do arquivo: Pulse1x_2026-11-12_21-05-33.mp4 (sem caracteres proibidos).</summary>
    public static string FileName(DateTime when, string container) =>
        $"Pulse1x_{when:yyyy-MM-dd_HH-mm-ss}.{container}";

    public static bool IsRecordingFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("Pulse1x_", StringComparison.OrdinalIgnoreCase)
               && (name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
