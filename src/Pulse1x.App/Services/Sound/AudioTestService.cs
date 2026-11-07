using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pulse1x.App.Services.Sound;

public enum TestTone { Left, Right, Both, Sweep, Bass }

/// <summary>
/// Testes ao vivo da categoria Som. A captura é feita em modo compartilhado, que é o mesmo caminho
/// usado por Discord e WhatsApp — então o que se ouve aqui já inclui o volume acima de 100%, os
/// filtros e o modificador de voz aplicados pelo motor de áudio.
/// </summary>
public sealed class AudioTestService : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private string? _captureId;
    private BufferedWaveProvider? _monitorBuffer;
    private WasapiOut? _monitorOut;
    private WasapiOut? _playback;
    private MemoryStream? _recording;
    private int _recordLimitBytes;
    private TaskCompletionSource<bool>? _recordDone;

    /// <summary>Pico do último bloco capturado (0 a 1), atualizado várias vezes por segundo.</summary>
    public float MicPeak { get; private set; }

    public bool IsCapturing => _capture is not null;
    public bool IsMonitoring => _monitorOut is not null;

    /// <summary>Disparado (em thread de áudio) quando a captura ou a reprodução para sozinha.</summary>
    public event Action? StateChanged;

    // =====================================================================================
    //  Medidor do microfone
    // =====================================================================================

    public void StartMeter(string micId)
    {
        lock (_gate)
        {
            if (_capture is not null && _captureId == micId) return;
            StopCaptureLocked();

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(micId);
            var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 30);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, _) => StateChanged?.Invoke();
            capture.StartRecording();
            _capture = capture;
            _captureId = micId;
        }
    }

    public void StopMeter()
    {
        lock (_gate)
        {
            StopMonitorLocked();
            StopCaptureLocked();
        }
        MicPeak = 0;
    }

    private void StopCaptureLocked()
    {
        if (_capture is null) return;
        try { _capture.DataAvailable -= OnData; _capture.StopRecording(); } catch { }
        try { _capture.Dispose(); } catch { }
        _capture = null;
        _captureId = null;
        _recordDone?.TrySetResult(false);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        if (capture is null || e.BytesRecorded == 0) return;
        MicPeak = Peak(e.Buffer, e.BytesRecorded, capture.WaveFormat);

        lock (_gate)
        {
            _monitorBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            if (_recording is not null)
            {
                int room = _recordLimitBytes - (int)_recording.Length;
                if (room > 0) _recording.Write(e.Buffer, 0, Math.Min(room, e.BytesRecorded));
                if (_recording.Length >= _recordLimitBytes) _recordDone?.TrySetResult(true);
            }
        }
    }

    internal static float Peak(byte[] buffer, int count, WaveFormat format)
    {
        float peak = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            for (int i = 0; i + 3 < count; i += 4)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer, i)));
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0; i + 1 < count; i += 2)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f));
        }
        else if (format.BitsPerSample == 24)
        {
            for (int i = 0; i + 2 < count; i += 3)
            {
                int sample = (buffer[i] | buffer[i + 1] << 8 | (sbyte)buffer[i + 2] << 16);
                peak = Math.Max(peak, Math.Abs(sample / 8388608f));
            }
        }
        return Math.Min(1, peak);
    }

    // =====================================================================================
    //  Gravar e ouvir
    // =====================================================================================

    /// <summary>Grava alguns segundos do microfone e toca de volta na saída escolhida.</summary>
    public async Task RecordAndPlayAsync(string micId, string outputId, int seconds, CancellationToken ct)
    {
        StartMeter(micId);
        WaveFormat format;
        Task<bool> done;
        lock (_gate)
        {
            format = _capture!.WaveFormat;
            _recording = new MemoryStream();
            _recordLimitBytes = format.AverageBytesPerSecond * seconds;
            _recordLimitBytes -= _recordLimitBytes % format.BlockAlign;
            _recordDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            done = _recordDone.Task;
        }

        using (ct.Register(() => _recordDone?.TrySetCanceled()))
        {
            try { await done.ConfigureAwait(false); }
            finally { }
        }

        byte[] data;
        lock (_gate)
        {
            data = _recording?.ToArray() ?? Array.Empty<byte>();
            _recording = null;
            _recordDone = null;
        }
        if (data.Length == 0) return;

        var source = new RawSourceWaveStream(new MemoryStream(data), format).ToSampleProvider();
        await PlayAsync(outputId, source, ct).ConfigureAwait(false);
    }

    /// <summary>Toca um tom de teste: esquerdo, direito, ambos, varredura de frequências ou graves.</summary>
    public Task PlayToneAsync(string outputId, TestTone tone, CancellationToken ct)
    {
        var generator = new SignalGenerator(48000, 1)
        {
            Gain = 0.25,
            Frequency = tone == TestTone.Bass ? 60 : 440,
            Type = SignalGeneratorType.Sin,
        };
        if (tone == TestTone.Sweep)
        {
            generator.Type = SignalGeneratorType.Sweep;
            generator.Frequency = 30;
            generator.FrequencyEnd = 16000;
            generator.SweepLengthSecs = 6;
        }

        double seconds = tone == TestTone.Sweep ? 6 : 1.6;
        ISampleProvider source = new OffsetSampleProvider(generator) { Take = TimeSpan.FromSeconds(seconds) };
        int channelMask = tone switch { TestTone.Left => 1, TestTone.Right => 2, _ => -1 };
        return PlayAsync(outputId, source, ct, channelMask);
    }

    private async Task PlayAsync(string outputId, ISampleProvider source, CancellationToken ct, int channelMask = -1)
    {
        StopPlayback();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(outputId);
        var mix = device.AudioClient.MixFormat;
        var adapted = Adapt(source, mix.SampleRate, mix.Channels, channelMask);

        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
        output.PlaybackStopped += (_, _) => done.TrySetResult(true);
        output.Init(adapted);
        lock (_gate) _playback = output;
        output.Play();

        using (ct.Register(() => { try { output.Stop(); } catch { } }))
            await done.Task.ConfigureAwait(false);

        lock (_gate)
        {
            if (ReferenceEquals(_playback, output)) _playback = null;
        }
        try { output.Dispose(); } catch { }
    }

    public void StopPlayback()
    {
        WasapiOut? playback;
        lock (_gate) { playback = _playback; _playback = null; }
        try { playback?.Stop(); } catch { }
    }

    // =====================================================================================
    //  Ouvir a própria voz ao vivo
    // =====================================================================================

    public void StartMonitor(string micId, string outputId)
    {
        StartMeter(micId);
        lock (_gate)
        {
            StopMonitorLocked();
            var format = _capture!.WaveFormat;
            _monitorBuffer = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(400),
            };

            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(outputId);
            var mix = device.AudioClient.MixFormat;
            var adapted = Adapt(_monitorBuffer.ToSampleProvider(), mix.SampleRate, mix.Channels, -1);
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            output.Init(adapted);
            output.Play();
            _monitorOut = output;
        }
    }

    public void StopMonitor()
    {
        lock (_gate) StopMonitorLocked();
    }

    private void StopMonitorLocked()
    {
        if (_monitorOut is not null)
        {
            try { _monitorOut.Stop(); } catch { }
            try { _monitorOut.Dispose(); } catch { }
        }
        _monitorOut = null;
        _monitorBuffer = null;
    }

    // =====================================================================================
    //  Conversão de formato
    // =====================================================================================

    /// <summary>
    /// Ajusta taxa de amostragem e canais ao formato de mixagem da saída (o modo compartilhado do
    /// Windows só aceita esse formato). Mono vai para todos os canais; channelMask escolhe os canais
    /// que tocam (bit 0 = esquerdo, bit 1 = direito; -1 = todos).
    /// </summary>
    private static IWaveProvider Adapt(ISampleProvider source, int sampleRate, int channels, int channelMask)
    {
        if (source.WaveFormat.SampleRate != sampleRate)
            source = new WdlResamplingSampleProvider(source, sampleRate);
        return new ChannelAdapter(source, channels, channelMask).ToWaveProvider();
    }

    private sealed class ChannelAdapter : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _inChannels;
        private readonly int _outChannels;
        private readonly int _mask;
        private float[] _buffer = Array.Empty<float>();

        public ChannelAdapter(ISampleProvider source, int outChannels, int mask)
        {
            _source = source;
            _inChannels = source.WaveFormat.Channels;
            _outChannels = outChannels;
            _mask = mask;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _outChannels;
            int needed = frames * _inChannels;
            if (_buffer.Length < needed) _buffer = new float[needed];
            int read = _source.Read(_buffer, 0, needed);
            int framesRead = read / _inChannels;

            for (int f = 0; f < framesRead; f++)
            {
                for (int c = 0; c < _outChannels; c++)
                {
                    float sample = _inChannels == 1 ? _buffer[f] : _buffer[f * _inChannels + Math.Min(c, _inChannels - 1)];
                    bool on = _mask < 0 || (c < 2 && (_mask & (1 << c)) != 0);
                    buffer[offset + f * _outChannels + c] = on ? sample : 0;
                }
            }
            return framesRead * _outChannels;
        }
    }

    public void Dispose()
    {
        StopPlayback();
        StopMeter();
    }
}
