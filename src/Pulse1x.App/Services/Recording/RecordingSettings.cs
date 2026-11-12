using System.IO;

namespace Pulse1x.App.Services.Recording;

/// <summary>Combinação de teclas de um atalho global (modificadores do RegisterHotKey + tecla virtual).</summary>
public sealed class HotkeyBinding
{
    public const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008;

    public uint Modifiers { get; set; }

    /// <summary>Código de tecla virtual (VK_*). 0 = atalho desligado.</summary>
    public uint Key { get; set; }

    public HotkeyBinding() { }
    public HotkeyBinding(uint modifiers, uint key) { Modifiers = modifiers; Key = key; }

    public bool IsEmpty => Key == 0;

    public bool SameAs(HotkeyBinding? other) =>
        other is not null && !IsEmpty && Key == other.Key && Modifiers == other.Modifiers;

    public HotkeyBinding Clone() => new(Modifiers, Key);

    /// <summary>Texto como "Ctrl + Alt + R".</summary>
    public string Display()
    {
        if (IsEmpty) return "—";
        var parts = new List<string>();
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        try
        {
            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)vk);
            return key switch
            {
                System.Windows.Input.Key.Snapshot => "PrtScn",
                System.Windows.Input.Key.Scroll => "ScrollLock",
                System.Windows.Input.Key.Pause => "Pause",
                System.Windows.Input.Key.Insert => "Ins",
                System.Windows.Input.Key.Delete => "Del",
                System.Windows.Input.Key.PageUp => "PgUp",
                System.Windows.Input.Key.Next => "PgDn",
                _ => key.ToString(),
            };
        }
        catch { return "0x" + vk.ToString("X2"); }
    }
}

/// <summary>
/// Tudo o que a categoria Gravação guarda no settings.json. Os valores em texto (codec, encoder,
/// qualidade...) são chaves estáveis, não traduzidas, para o arquivo sobreviver à troca de idioma.
/// </summary>
public class RecordingSettings
{
    public static readonly int[] FpsOptions = { 24, 30, 60, 120, 144 };

    // ---------------- Arquivo ----------------

    /// <summary>Pasta dos vídeos. null/vazio = Downloads do usuário.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>"mp4" (abre em qualquer lugar) ou "mkv" (não corrompe se o PC desligar no meio).</summary>
    public string Container { get; set; } = "mp4";

    // ---------------- O que gravar ----------------

    /// <summary>"Monitor" (tela inteira) ou "Region" (área retangular de um monitor).</summary>
    public string CaptureTarget { get; set; } = "Monitor";

    /// <summary>Nome do monitor no Windows (\\.\DISPLAY1). null = monitor principal.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Área a gravar, em pixels físicos relativos ao canto do monitor escolhido.</summary>
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }

    public bool DrawMouse { get; set; } = true;

    // ---------------- Vídeo ----------------

    public int Fps { get; set; } = 60;

    /// <summary>"Native", "2160", "1440", "1080" ou "720" (altura máxima do vídeo).</summary>
    public string Resolution { get; set; } = "Native";

    /// <summary>"H264", "HEVC" ou "AV1".</summary>
    public string Codec { get; set; } = "H264";

    /// <summary>"Auto", "NVENC", "AMF", "QSV" ou "CPU".</summary>
    public string Encoder { get; set; } = "Auto";

    /// <summary>"Low", "Medium", "High", "Ultra" ou "Custom" (taxa fixa em Mbps).</summary>
    public string Quality { get; set; } = "High";

    public int CustomBitrateMbps { get; set; } = 20;

    // ---------------- Áudio ----------------

    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }

    /// <summary>Id do microfone no Windows. null = microfone padrão.</summary>
    public string? MicrophoneId { get; set; }

    /// <summary>Volume de cada faixa, de 0 a 2 (200%).</summary>
    public double SystemVolume { get; set; } = 1.0;
    public double MicrophoneVolume { get; set; } = 1.0;

    /// <summary>Ajuste fino da sincronia do áudio, em ms (positivo atrasa o áudio).</summary>
    public int AudioOffsetMs { get; set; }

    // ---------------- Comportamento ----------------

    /// <summary>Contagem regressiva antes de começar (0, 3, 5 ou 10 s).</summary>
    public int CountdownSeconds { get; set; }

    /// <summary>Mostra o indicador "REC" com o tempo no canto da tela (ele não aparece no vídeo).</summary>
    public bool ShowIndicator { get; set; } = true;

    /// <summary>Toca um bipe curto ao iniciar, pausar e terminar.</summary>
    public bool SoundCues { get; set; } = true;

    /// <summary>Abre a pasta com o vídeo selecionado ao terminar.</summary>
    public bool OpenFolderWhenDone { get; set; }

    // ---------------- Atalhos ----------------

    public bool HotkeysEnabled { get; set; } = true;
    public HotkeyBinding StartHotkey { get; set; } = new(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x52); // R
    public HotkeyBinding StopHotkey { get; set; } = new(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x53);  // S
    public HotkeyBinding PauseHotkey { get; set; } = new(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x50); // P
    public HotkeyBinding OverlayHotkey { get; set; } = new(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x47); // G

    // ---------------- Motor ----------------

    /// <summary>Caminho do ffmpeg.exe escolhido à mão. null = procurar sozinho.</summary>
    public string? FfmpegPath { get; set; }

    public string EffectiveOutputFolder =>
        string.IsNullOrWhiteSpace(OutputFolder) ? DefaultOutputFolder() : OutputFolder!;

    public static string DefaultOutputFolder()
    {
        try
        {
            // Downloads não tem SpecialFolder próprio no .NET: usa a Known Folder do Shell.
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var path) == 0
                && !string.IsNullOrEmpty(path))
                return path;
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid rfid,
        uint flags, IntPtr token,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string path);

    public void Normalize()
    {
        if (!FpsOptions.Contains(Fps)) Fps = 60;
        Container = Container is "mkv" ? "mkv" : "mp4";
        CaptureTarget = CaptureTarget is "Region" ? "Region" : "Monitor";
        Resolution = Resolution is "2160" or "1440" or "1080" or "720" ? Resolution : "Native";
        Codec = Codec is "HEVC" or "AV1" ? Codec : "H264";
        Encoder = Encoder is "NVENC" or "AMF" or "QSV" or "CPU" ? Encoder : "Auto";
        Quality = Quality is "Low" or "Medium" or "Ultra" or "Custom" ? Quality : "High";
        CustomBitrateMbps = Math.Clamp(CustomBitrateMbps, 2, 150);
        SystemVolume = double.IsFinite(SystemVolume) ? Math.Clamp(SystemVolume, 0, 2) : 1;
        MicrophoneVolume = double.IsFinite(MicrophoneVolume) ? Math.Clamp(MicrophoneVolume, 0, 2) : 1;
        AudioOffsetMs = Math.Clamp(AudioOffsetMs, -1000, 1000);
        CountdownSeconds = CountdownSeconds is 3 or 5 or 10 ? CountdownSeconds : 0;
        RegionX = Math.Max(0, RegionX);
        RegionY = Math.Max(0, RegionY);
        RegionWidth = Math.Max(0, RegionWidth);
        RegionHeight = Math.Max(0, RegionHeight);
        StartHotkey ??= new HotkeyBinding();
        StopHotkey ??= new HotkeyBinding();
        PauseHotkey ??= new HotkeyBinding();
        OverlayHotkey ??= new HotkeyBinding();
    }
}
