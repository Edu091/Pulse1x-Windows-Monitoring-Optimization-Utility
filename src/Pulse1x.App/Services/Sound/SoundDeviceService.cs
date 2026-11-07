using NAudio.CoreAudioApi;

namespace Pulse1x.App.Services.Sound;

/// <summary>Um dispositivo de som como aparece nos seletores da categoria Som.</summary>
public sealed record SoundDevice(string Id, string Name, bool IsInput, bool IsDefault)
{
    public string Display => IsDefault ? $"{Name}  ★" : Name;
}

/// <summary>A amplificação de hardware do microfone, quando o driver a expõe.</summary>
public sealed record HardwareBoostInfo(float MinDb, float MaxDb, float StepDb, float CurrentDb)
{
    /// <summary>Os degraus possíveis (normalmente 0, +10, +20 e +30 dB).</summary>
    public IReadOnlyList<float> Steps
    {
        get
        {
            var steps = new List<float>();
            float step = StepDb <= 0.01f ? Math.Max(1, (MaxDb - MinDb) / 3) : StepDb;
            // Drivers que expõem passos de 0,5 dB geram dezenas de opções; agrupa em até ~7.
            while ((MaxDb - MinDb) / step > 6) step *= 2;
            for (float v = MinDb; v <= MaxDb + 0.01f; v += step) steps.Add((float)Math.Round(v, 1));
            if (Math.Abs(steps[^1] - MaxDb) > 0.05f) steps.Add(MaxDb);
            return steps;
        }
    }
}

/// <summary>
/// Volume, mudo, dispositivo padrão, amplificação de hardware ("Microphone Boost") e medidores de
/// nível, pela Core Audio do Windows (via NAudio). É a mesma API do mixer do sistema, então tudo
/// o que muda aqui aparece imediatamente no ícone de som e nas Configurações do Windows.
/// </summary>
public class SoundDeviceService
{
    private readonly Profiles.AudioService _legacy = new();

    public IReadOnlyList<SoundDevice> List(bool input)
    {
        var result = new List<SoundDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var flow = input ? DataFlow.Capture : DataFlow.Render;
            string? defaultId = DefaultId(enumerator, flow);
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    try { result.Add(new SoundDevice(device.ID, device.FriendlyName, input, device.ID == defaultId)); }
                    catch { }
                }
            }
        }
        catch { }
        return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    public string? GetDefaultId(bool input)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return DefaultId(enumerator, input ? DataFlow.Capture : DataFlow.Render);
        }
        catch { return null; }
    }

    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)) return null;
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch { return null; }
    }

    /// <summary>Resolve o Id escolhido (ou o padrão, se null/desconectado).</summary>
    public string? Resolve(string? id, bool input)
    {
        if (!string.IsNullOrEmpty(id) && List(input).Any(d => d.Id == id)) return id;
        return GetDefaultId(input);
    }

    public bool SetDefault(string id) => _legacy.SetDefaultDevice(id);

    // ---------------- Volume e mudo ----------------

    public int? GetVolume(string? id) => With(id, d => (int?)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100));

    public bool SetVolume(string? id, int percent) => With(id, d =>
    {
        d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
        return true;
    });

    public bool? GetMute(string? id) => With(id, d => (bool?)d.AudioEndpointVolume.Mute);

    public bool SetMute(string? id, bool mute) => With(id, d =>
    {
        d.AudioEndpointVolume.Mute = mute;
        return true;
    });

    /// <summary>Taxa de amostragem em que o Windows processa o dispositivo (formato de mixagem).</summary>
    public int? GetSampleRate(string? id) => With(id, d => (int?)d.AudioClient.MixFormat.SampleRate);

    /// <summary>
    /// Tira o dispositivo de padrão: o Windows sempre tem um padrão, então outro assume — o anterior
    /// (se ainda estiver conectado) ou o primeiro disponível. Devolve o Id que virou padrão.
    /// </summary>
    public string? RemoveDefault(string id, bool input, string? preferred)
    {
        var others = List(input).Where(d => d.Id != id).ToList();
        var next = others.FirstOrDefault(d => d.Id == preferred) ?? others.FirstOrDefault();
        return next is not null && SetDefault(next.Id) ? next.Id : null;
    }

    /// <summary>Nível de pico atual (0 a 1) medido pelo próprio Windows no dispositivo.</summary>
    public float GetPeak(string? id) => With(id, d => d.AudioMeterInformation.MasterPeakValue);

    // ---------------- Amplificação de hardware do microfone ----------------

    /// <summary>
    /// Procura, na topologia do driver, o controle de amplificação do microfone (o mesmo da aba
    /// "Níveis" do painel de som antigo). Nem todo microfone tem: USB, Bluetooth e arrays digitais
    /// costumam não ter, e então volta null.
    /// </summary>
    public HardwareBoostInfo? GetHardwareBoost(string? id) => With(id, d =>
    {
        var level = FindBoost(d);
        if (level is null) return null;
        level.GetLevelRange(0, out float min, out float max, out float step);
        return new HardwareBoostInfo(min, max, step, level.GetLevel(0));
    });

    public bool SetHardwareBoost(string? id, float db) => With(id, d =>
    {
        var level = FindBoost(d);
        if (level is null) return false;
        level.GetLevelRange(0, out float min, out float max, out _);
        level.SetLevelUniform(Math.Clamp(db, min, max));
        return true;
    });

    private static NAudio.Wasapi.CoreAudioApi.AudioVolumeLevel? FindBoost(MMDevice device)
    {
        DeviceTopology topology;
        try { topology = device.DeviceTopology; }
        catch { return null; }

        var visited = new HashSet<string>();
        for (uint c = 0; c < topology.ConnectorCount; c++)
        {
            Part? start;
            try
            {
                var connector = topology.GetConnector(c);
                if (!connector.IsConnected) continue;
                start = connector.ConnectedTo.Part;
            }
            catch { continue; }

            var found = Walk(start, visited, 0);
            if (found is not null) return found;
        }
        return null;
    }

    // Anda pela topologia "para trás" (do conector do endpoint em direção ao microfone físico).
    private static NAudio.Wasapi.CoreAudioApi.AudioVolumeLevel? Walk(Part? part, HashSet<string> visited, int depth)
    {
        if (part is null || depth > 24) return null;
        string key;
        try { key = part.GlobalId; } catch { return null; }
        if (!visited.Add(key)) return null;

        try
        {
            string name = part.Name ?? "";
            if (IsBoostName(name))
            {
                var level = part.AudioVolumeLevel;
                if (level is not null && level.ChannelCount > 0) return level;
            }
        }
        catch { }

        PartsList? incoming = null;
        try { incoming = part.PartsIncoming; } catch { }
        if (incoming is null) return null;
        for (uint i = 0; i < incoming.Count; i++)
        {
            Part? next;
            try { next = incoming[i]; } catch { continue; }
            var found = Walk(next, visited, depth + 1);
            if (found is not null) return found;
        }
        return null;
    }

    internal static bool IsBoostName(string name) =>
        name.Contains("boost", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("reforço", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("amplifica", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("aumento", StringComparison.OrdinalIgnoreCase);

    /// <summary>Medidor de nível que mantém o dispositivo aberto (para ler várias vezes por segundo).</summary>
    public LevelMeter? OpenMeter(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return new LevelMeter(id, enumerator.GetDevice(id));
        }
        catch { return null; }
    }

    public sealed class LevelMeter : IDisposable
    {
        private readonly MMDevice _device;
        public string Id { get; }
        internal LevelMeter(string id, MMDevice device) { Id = id; _device = device; }
        public float Peak
        {
            get
            {
                try { return _device.AudioMeterInformation.MasterPeakValue; }
                catch { return 0; }
            }
        }
        public void Dispose() { try { _device.Dispose(); } catch { } }
    }

    // ---------------- Infra ----------------

    private static T? With<T>(string? id, Func<MMDevice, T?> action)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (string.IsNullOrEmpty(id)) return default;
            using var device = enumerator.GetDevice(id);
            return action(device);
        }
        catch { return default; }
    }

    private static bool With(string? id, Func<MMDevice, bool> action) => With<bool>(id, action);
}
