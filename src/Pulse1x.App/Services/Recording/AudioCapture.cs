using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Pulse1x.App.Services.Recording;

/// <summary>Microfone disponível para a gravação.</summary>
public sealed record MicrophoneInfo(string Id, string Name);

/// <summary>
/// Grava uma faixa de áudio (som do sistema por loopback, ou um microfone) em PCM bruto, sem
/// cabeçalho — um WAV pararia em 4 GB, e o ffmpeg lê o PCM direto informando formato e taxa.
///
/// A captura fica ligada desde o início; o que liga e desliga é a escrita (<see cref="Writing"/>),
/// para pausar sem perder sincronia e começar a escrever exatamente quando o vídeo começa.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    private readonly WasapiCapture _capture;
    private readonly WasapiOut? _silence;
    private readonly FileStream _file;
    private readonly object _gate = new();
    private byte[] _zeros = Array.Empty<byte>();
    private volatile bool _writing;
    private bool _disposed;

    public AudioTrack Track { get; }

    /// <summary>Quando ligado, os dados recebidos vão para o arquivo.</summary>
    public bool Writing
    {
        get => _writing;
        set => _writing = value;
    }

    /// <summary>Grava silêncio no lugar do som (botão "mudo" do microfone no menu flutuante).</summary>
    public bool Muted { get; set; }

    private AudioCapture(WasapiCapture capture, WasapiOut? silence, string path, double volume)
    {
        _capture = capture;
        _silence = silence;
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        var format = capture.WaveFormat;
        Track = new AudioTrack(path, SampleFormatOf(format), format.SampleRate, format.Channels, volume);
        _capture.DataAvailable += OnData;
    }

    /// <summary>Som que sai no dispositivo de reprodução padrão.</summary>
    public static AudioCapture CreateSystem(string path, double volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var capture = new WasapiLoopbackCapture(device);

        // O loopback do WASAPI não entrega nada enquanto nenhum app toca som: o arquivo ficaria
        // mais curto que o vídeo e o áudio sairia adiantado. Tocar silêncio no mesmo dispositivo
        // mantém o fluxo contínuo (técnica padrão; não muda nada que se ouça).
        WasapiOut? silence = null;
        try
        {
            silence = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
            silence.Init(new SilenceProvider(device.AudioClient.MixFormat));
        }
        catch { silence?.Dispose(); silence = null; }

        return new AudioCapture(capture, silence, path, volume);
    }

    /// <summary>Microfone escolhido (ou o padrão quando o Id é nulo ou não existe mais).</summary>
    public static AudioCapture CreateMicrophone(string path, string? deviceId, double volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        if (!string.IsNullOrEmpty(deviceId))
        {
            try
            {
                device = enumerator.GetDevice(deviceId);
                if (device.State != DeviceState.Active) device = null;
            }
            catch { device = null; }
        }
        device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        return new AudioCapture(new WasapiCapture(device), null, path, volume);
    }

    public static List<MicrophoneInfo> ListMicrophones()
    {
        var list = new List<MicrophoneInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try { list.Add(new MicrophoneInfo(d.ID, d.FriendlyName)); }
                catch { }
                finally { d.Dispose(); }
            }
        }
        catch { }
        return list;
    }

    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Formato do ffmpeg para o PCM entregue pelo WASAPI.</summary>
    public static string SampleFormatOf(WaveFormat format)
    {
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubtype);
        if (isFloat) return format.BitsPerSample == 64 ? "f64le" : "f32le";
        return format.BitsPerSample switch
        {
            8 => "u8",
            24 => "s24le",
            32 => "s32le",
            _ => "s16le",
        };
    }

    public void Start()
    {
        _silence?.Play();
        _capture.StartRecording();
    }

    /// <summary>Quantos bytes já foram escritos.</summary>
    public long Position
    {
        get { lock (_gate) return _disposed ? 0 : _file.Position; }
    }

    /// <summary>Joga fora o que foi escrito depois de <paramref name="position"/> (tentativa que falhou).</summary>
    public void TruncateTo(long position)
    {
        lock (_gate)
        {
            if (_disposed || position > _file.Length) return;
            _file.SetLength(position);
            _file.Position = position;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!_writing || e.BytesRecorded <= 0) return;
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                if (Muted)
                {
                    if (_zeros.Length < e.BytesRecorded) _zeros = new byte[e.BytesRecorded];
                    _file.Write(_zeros, 0, e.BytesRecorded);
                }
                else
                {
                    _file.Write(e.Buffer, 0, e.BytesRecorded);
                }
            }
            catch { /* disco cheio: o vídeo também vai parar, e o erro aparece por lá */ }
        }
    }

    public void Dispose()
    {
        _writing = false;
        try { _capture.StopRecording(); } catch { }
        try { _silence?.Stop(); } catch { }
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _file.Flush(); } catch { }
            _file.Dispose();
        }
        _capture.DataAvailable -= OnData;
        _capture.Dispose();
        _silence?.Dispose();
    }
}
