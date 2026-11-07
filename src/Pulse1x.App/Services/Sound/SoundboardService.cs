using System.Collections.Concurrent;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pulse1x.App.Services.Sound;

/// <summary>Um som da mesa: embutido (sintetizado) ou importado pelo usuário.</summary>
public sealed record SoundboardItem(string Key, string? FilePath)
{
    public bool IsCustom => FilePath is not null;
}

/// <summary>Resultado de um disparo: se chegou ao microfone (o plugin consumiu) ou não.</summary>
public enum SoundboardDelivery { Sent, NotConsumed, NoChannel }

/// <summary>
/// Mesa de sons: dispara sons na call. As amostras vão para a memória compartilhada lida pelo plugin
/// de voz no microfone (ver <see cref="SoundboardChannel"/>), e, se o usuário quiser, também tocam
/// na saída para ele mesmo ouvir.
/// </summary>
public sealed class SoundboardService : IDisposable
{
    public const int MaxSeconds = 20;
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".flac" };

    private readonly ConcurrentDictionary<(string Key, int Rate), float[]> _cache = new();
    private SoundboardChannel? _channel;
    private readonly object _gate = new();
    private WasapiOut? _monitor;

    public string CustomFolder { get; }

    public SoundboardService(string? customFolder = null)
    {
        CustomFolder = customFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", "Soundboard");
    }

    public IReadOnlyList<SoundboardItem> Items()
    {
        var items = SoundboardSynth.Keys.Select(k => new SoundboardItem(k, null)).ToList();
        if (Directory.Exists(CustomFolder))
        {
            items.AddRange(Directory.GetFiles(CustomFolder)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(File.GetCreationTimeUtc)
                .Select(f => new SoundboardItem("custom:" + Path.GetFileName(f), f)));
        }
        return items;
    }

    /// <summary>Copia o arquivo para a pasta da mesa (para continuar existindo se o original sumir).</summary>
    public SoundboardItem Import(string sourcePath)
    {
        if (!AudioExtensions.Contains(Path.GetExtension(sourcePath).ToLowerInvariant()))
            throw new InvalidDataException("Formato de áudio não suportado.");
        // Decodifica antes de copiar: um arquivo que o Windows não lê nem entra na mesa.
        _ = Decode(sourcePath, 48000);
        Directory.CreateDirectory(CustomFolder);
        string name = Path.GetFileName(sourcePath);
        string dest = Path.Combine(CustomFolder, name);
        for (int i = 2; File.Exists(dest); i++)
            dest = Path.Combine(CustomFolder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        File.Copy(sourcePath, dest);
        return new SoundboardItem("custom:" + Path.GetFileName(dest), dest);
    }

    public void Remove(SoundboardItem item)
    {
        if (item.FilePath is null) return;
        try { File.Delete(item.FilePath); } catch { }
        foreach (var key in _cache.Keys.Where(k => k.Key == item.Key).ToList()) _cache.TryRemove(key, out _);
    }

    public float[] Samples(SoundboardItem item, int rate) =>
        _cache.GetOrAdd((item.Key, rate), _ => item.FilePath is string path
            ? Decode(path, rate)
            : SoundboardSynth.Generate(item.Key, rate));

    /// <summary>Lê um arquivo de áudio qualquer, mistura para mono, ajusta a taxa e normaliza o pico.</summary>
    public static float[] Decode(string path, int rate)
    {
        using var reader = new MediaFoundationReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels > 1) source = new MonoMix(source);
        if (source.WaveFormat.SampleRate != rate) source = new WdlResamplingSampleProvider(source, rate);

        var output = new List<float>(rate * 4);
        var buffer = new float[rate];
        int max = rate * MaxSeconds;
        int read;
        while (output.Count < max && (read = source.Read(buffer, 0, buffer.Length)) > 0)
            output.AddRange(buffer.Take(Math.Min(read, max - output.Count)));

        var samples = output.ToArray();
        float peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
        if (peak > 1e-4f)
            for (int i = 0; i < samples.Length; i++) samples[i] *= 0.9f / peak;
        return samples;
    }

    /// <summary>
    /// Toca o som na call (pelo plugin no microfone) e, opcionalmente, na saída local. Espera um
    /// instante para conferir se o plugin consumiu as amostras — senão o som não chegou à call.
    /// </summary>
    public async Task<SoundboardDelivery> PlayAsync(SoundboardItem item, int micRate, float volume,
        string? monitorOutputId, float monitorVolume)
    {
        var channel = EnsureChannel();
        SoundboardDelivery result = SoundboardDelivery.NoChannel;
        if (channel is not null)
        {
            int rate = channel.ConsumerRate > 0 ? channel.ConsumerRate : micRate;
            var samples = await Task.Run(() => Samples(item, rate));
            channel.Volume = volume;
            long before = channel.Consumed;
            channel.Play(samples, rate);
            if (monitorOutputId is not null) StartMonitor(item, monitorOutputId, monitorVolume);
            await Task.Delay(250);
            result = channel.Consumed != before ? SoundboardDelivery.Sent : SoundboardDelivery.NotConsumed;
        }
        else if (monitorOutputId is not null)
        {
            StartMonitor(item, monitorOutputId, monitorVolume);
        }
        return result;
    }

    public void StopAll()
    {
        _channel?.StopAll();
        lock (_gate)
        {
            try { _monitor?.Stop(); } catch { }
        }
    }

    private SoundboardChannel? EnsureChannel()
    {
        lock (_gate)
        {
            if (_channel is not null) return _channel;
            try { _channel = new SoundboardChannel(); }
            catch { _channel = null; }
            return _channel;
        }
    }

    private void StartMonitor(SoundboardItem item, string outputId, float volume)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(outputId);
            var mix = device.AudioClient.MixFormat;
            var samples = Samples(item, mix.SampleRate);
            var provider = new MonoToMany(samples, mix.SampleRate, mix.Channels, volume);
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
            output.Init(provider);
            lock (_gate)
            {
                try { _monitor?.Stop(); _monitor?.Dispose(); } catch { }
                _monitor = output;
            }
            output.Play();
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _monitor?.Dispose(); } catch { }
            _monitor = null;
            _channel?.Dispose();
            _channel = null;
        }
    }

    // ------------------------------------------------------------------ provedores de amostras

    private sealed class MonoMix : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _buffer = Array.Empty<float>();
        public MonoMix(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int need = count * _channels;
            if (_buffer.Length < need) _buffer = new float[need];
            int read = _source.Read(_buffer, 0, need) / _channels;
            for (int i = 0; i < read; i++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++) sum += _buffer[i * _channels + c];
                buffer[offset + i] = sum / _channels;
            }
            return read;
        }
    }

    private sealed class MonoToMany : ISampleProvider
    {
        private readonly float[] _samples;
        private readonly int _channels;
        private readonly float _volume;
        private int _pos;
        public MonoToMany(float[] samples, int rate, int channels, float volume)
        {
            _samples = samples;
            _channels = channels;
            _volume = volume;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = Math.Min(count / _channels, _samples.Length - _pos);
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < _channels; c++)
                    buffer[offset + f * _channels + c] = _samples[_pos + f] * _volume;
            _pos += frames;
            return frames * _channels;
        }
    }
}
