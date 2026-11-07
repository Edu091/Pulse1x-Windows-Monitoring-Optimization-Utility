using System.IO;
using System.Text.Json;
using NAudio.Wave;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Sound;

namespace Pulse1x.Hub.Tests;

/// <summary>Categoria Som: divisão do volume do microfone, arquivo do motor de áudio e persistência.</summary>
internal static class SoundTests
{
    private const string OutId = "{0.0.0.00000000}.{11111111-2222-3333-4444-555555555555}";
    private const string MicId = "{0.0.1.00000000}.{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    public static void Register(TestSuite suite)
    {
        suite.Run("Mic level splits into Windows volume and digital gain", () =>
        {
            TestSuite.Equal((0, 0.0), SoundPresets.SplitMicLevel(0));
            TestSuite.Equal((75, 0.0), SoundPresets.SplitMicLevel(75));
            TestSuite.Equal((100, 0.0), SoundPresets.SplitMicLevel(100));
            TestSuite.Equal((100, 6.02), SoundPresets.SplitMicLevel(200));
            TestSuite.Equal((100, 12.04), SoundPresets.SplitMicLevel(400));
            TestSuite.Equal((100, 20.0), SoundPresets.SplitMicLevel(1000));
            TestSuite.Equal((100, 20.0), SoundPresets.SplitMicLevel(5000));
        });

        suite.Run("Endpoint ids map to Equalizer APO device GUIDs", () =>
        {
            TestSuite.Equal("{11111111-2222-3333-4444-555555555555}", EqualizerApoConfig.ExtractGuid(OutId));
            TestSuite.Equal("{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}", EqualizerApoConfig.ExtractGuid(MicId));
            TestSuite.Equal<string?>(null, EqualizerApoConfig.ExtractGuid("garbage"));
            TestSuite.Equal<string?>(null, EqualizerApoConfig.ExtractGuid(null));
        });

        suite.Run("Config with everything off only resets device and channel", () =>
        {
            var s = new SoundSettings();
            string text = EqualizerApoConfig.Build(s, "{o}", "{m}");
            var lines = Lines(text);
            TestSuite.SequenceEqual(new[] { "Device: all", "Channel: all" }, lines);
        });

        suite.Run("Output config scopes EQ, bass and 3D to the chosen device with headroom", () =>
        {
            var s = new SoundSettings { OutputEffectsEnabled = true, BassBoostDb = 6, Surround = 1 };
            s.Eq[0] = 3;
            s.Eq[5] = -2;
            var lines = Lines(EqualizerApoConfig.Build(s, "{out-guid}", null));

            TestSuite.Equal("Device: {out-guid}", lines[0]);
            // Graves (3 + 6 dB) + 3D (20·log10(1,75) ≈ 4,86 dB) = -13,9 dB de compensação.
            TestSuite.Equal("Preamp: -13.9 dB", lines[1]);
            TestSuite.Equal("Copy: L=1.75*L+-0.75*R R=1.75*R+-0.75*L", lines[2]);
            TestSuite.Equal("GraphicEQ: 31 3; 62 0; 125 0; 250 0; 500 0; 1000 -2; 2000 0; 4000 0; 8000 0; 16000 0", lines[3]);
            TestSuite.Equal("Filter: ON LSC Fc 110 Hz Gain 6 dB Q 0.7", lines[4]);
            TestSuite.Equal("Device: all", lines[^2]);
        });

        suite.Run("Mic config adds gain above 100%, cleanup filters and the voice effect", () =>
        {
            var s = new SoundSettings { MicLevelPercent = 200, MicLowCut = true, VoiceEffect = "Phone" };
            var lines = Lines(EqualizerApoConfig.Build(s, "{out}", "{mic}"));
            var phone = SoundPresets.FindVoice("Phone");

            TestSuite.Equal("Device: {mic}", lines[0]);
            TestSuite.Equal($"Preamp: {(6.02 + phone.PreampDb).ToString(System.Globalization.CultureInfo.InvariantCulture)} dB", lines[1]);
            TestSuite.Equal("Filter: ON HPQ Fc 90 Hz Q 0.7", lines[2]);
            TestSuite.Equal(phone.Filters.Length, lines.Count(l => l.StartsWith("Filter: ON") && !l.Contains("90 Hz")));
            // Saída desligada: nenhum bloco para o dispositivo de saída.
            TestSuite.Equal(false, lines.Any(l => l == "Device: {out}"));
        });

        suite.Run("Every preset voice effect uses valid Equalizer APO filter syntax", () =>
        {
            var pattern = new System.Text.RegularExpressions.Regex(
                @"^ON (PK|LS|HS|LSC|HSC|LP|HP|LPQ|HPQ|NO|BP) Fc \d+(\.\d+)? Hz( Gain -?\d+(\.\d+)? dB)?( Q \d+(\.\d+)?)?$");
            foreach (var voice in SoundPresets.VoiceEffects)
                foreach (var filter in voice.Filters)
                    if (!pattern.IsMatch(filter)) throw new Exception($"{voice.Key}: '{filter}'");
            foreach (var profile in SoundPresets.Profiles)
                TestSuite.Equal(SoundPresets.EqFrequencies.Length, profile.Eq.Length);
        });

        suite.Run("Impulse-response voice effects add a Convolution at the mic sample rate", () =>
        {
            var s = new SoundSettings { VoiceEffect = "Cyborg" };
            var lines = Lines(EqualizerApoConfig.Build(s, null, "{mic}", 48000));
            TestSuite.Equal(true, lines.Contains("Convolution: pulse1x-ir-robot-48000.wav"));
            // Taxa desconhecida: sem a convolução (o motor recusaria um arquivo com a taxa errada).
            var unknown = Lines(EqualizerApoConfig.Build(s, null, "{mic}"));
            TestSuite.Equal(false, unknown.Any(l => l.StartsWith("Convolution:")));
            // Eco não tem filtros, só a resposta ao impulso — o bloco do microfone ainda aparece.
            var cave = Lines(EqualizerApoConfig.Build(new SoundSettings { VoiceEffect = "Cave" }, null, "{mic}", 44100));
            TestSuite.Equal("Device: {mic}", cave[0]);
            TestSuite.Equal(true, cave.Contains("Convolution: pulse1x-ir-cave-44100.wav"));
        });

        suite.Run("Impulse responses start with the direct sound and write a float WAV", () =>
        {
            foreach (var kind in ImpulseResponses.Kinds)
            {
                var ir = ImpulseResponses.Generate(kind, 48000);
                TestSuite.Equal(true, ir[0] > 0.1f);
                TestSuite.Equal(true, ir.All(float.IsFinite) && ir.Max(Math.Abs) <= 1.6f);
                var wav = ImpulseResponses.ToWav(ir, 48000);
                TestSuite.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
                TestSuite.Equal(44 + ir.Length * 4, wav.Length);
                TestSuite.Equal((short)3, BitConverter.ToInt16(wav, 20));
                TestSuite.Equal(48000, BitConverter.ToInt32(wav, 24));
            }
            var robot = ImpulseResponses.Generate("robot", 48000);
            TestSuite.Equal(true, robot[436] > 0.1f); // repetição a cada ~9 ms (110 Hz)
        });
        suite.Run("Voice plugin line carries the effect, the gain above 100% and the gate", () =>
        {
            var s = new SoundSettings { MicLevelPercent = 200, VoiceEffect = "Deep", MicNoiseGate = true };
            var lines = Lines(EqualizerApoConfig.Build(s, null, "{mic}", 48000, @"Pulse1x\Pulse1xVoice-abc.dll"));
            var plugin = lines.Single(l => l.StartsWith("VSTPlugin:"));
            TestSuite.Equal(true, plugin.StartsWith("VSTPlugin: Library \"Pulse1x\\Pulse1xVoice-abc.dll\" ChunkData \""));
            string b64 = plugin.Split('"')[3];
            TestSuite.Equal("pitch=-4;gain=6.02;gate=-52", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64)));
            // O ganho foi para o plugin (com limitador): nenhum Preamp positivo sobra.
            TestSuite.Equal(false, lines.Any(l => l.StartsWith("Preamp:") && !l.Contains("-")));
            TestSuite.Equal("VSTPlugin:", lines[^3][..10]); // plugin por último no bloco do microfone

            // Sem o plugin instalado: o ganho volta a ser Preamp e o efeito fica só nos filtros.
            var fallback = Lines(EqualizerApoConfig.Build(s, null, "{mic}", 48000));
            TestSuite.Equal(false, fallback.Any(l => l.StartsWith("VSTPlugin:")));
            TestSuite.Equal(true, fallback.Contains("Preamp: 6.02 dB"));

            // Saída acima de 100%: o ganho positivo também vai para o limitador.
            var loud = new SoundSettings { OutputEffectsEnabled = true, OutputGainDb = 8 };
            var outLines = Lines(EqualizerApoConfig.Build(loud, "{out}", null, 0, "p.dll"));
            TestSuite.Equal("Preamp: 0 dB", outLines[1]);
            TestSuite.Equal(true, outLines.Any(l => l.StartsWith("VSTPlugin: Library \"p.dll\"")));
        });

        suite.Run("Every voice effect's DSP parameters are known to the plugin", () =>
        {
            var known = new HashSet<string> { "pitch", "vib", "vibrate", "robot", "whisper", "ring", "ringmix",
                "drive", "crush", "bits", "echo", "echofb", "echomix", "gain", "gate" };
            foreach (var voice in SoundPresets.VoiceEffects.Where(v => v.Dsp is not null))
                foreach (var pair in voice.Dsp!.Split(';'))
                {
                    var kv = pair.Split('=');
                    if (kv.Length != 2 || !known.Contains(kv[0]) ||
                        !double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                        throw new Exception($"{voice.Key}: '{pair}'");
                }
            TestSuite.Equal(SoundPresets.VoiceEffects.Count, SoundPresets.VoiceEffects.Select(v => v.Key).Distinct().Count());
        });

        suite.Run("Mic FxProperties opens for writing with only the rights the key grants", () =>
        {
            // Regressão da 1.17.3: OpenSubKey(path, writable: true) pede também "criar subchave",
            // que essa chave nega até a administradores, e o botão "Ativar em apps de chamada" falhava.
            var mic = new SoundDeviceService().List(input: true).FirstOrDefault();
            if (mic is null) return; // máquina sem microfone: nada a verificar
            using var key = EqualizerApoService.OpenFx(mic.Id, writable: true);
            TestSuite.Equal(true, key is not null);
        });
        suite.Run("Embedded voice plugin loads as a VST2 and shifts pitch", () => VoicePluginProbe.Run());
        suite.Run("Factory Equalizer APO config is recognized; user configs are kept", () =>
        {
            TestSuite.Equal(true, EqualizerApoService.IsFactoryConfig(""));
            TestSuite.Equal(true, EqualizerApoService.IsFactoryConfig("Preamp: -6 dB\r\nInclude: example.txt\r\n"));
            TestSuite.Equal(false, EqualizerApoService.IsFactoryConfig("Preamp: -6 dB\nInclude: my-room.txt\n"));
            // Instalador 1.4.2: acrescenta um GraphicEQ zerado; e a 1.17.0 já tinha posto o Include.
            TestSuite.Equal(true, EqualizerApoService.IsFactoryConfig(
                "Preamp: -6 dB\nInclude: example.txt\nGraphicEQ: 25 0; 40 0; 63 0; 1000 0; 16000 0\nInclude: pulse1x.txt\n"));
            TestSuite.Equal(false, EqualizerApoService.IsFactoryConfig("GraphicEQ: 25 0; 40 3\n"));
        });

        suite.Run("Sound settings survive a settings.json round-trip and are clamped", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"pulse1x-sound-{Guid.NewGuid():N}.json");
            try
            {
                var service = new SettingsService(path);
                service.Current.Sound.MicLevelPercent = 250;
                service.Current.Sound.VoiceEffect = "Robot";
                service.Current.Sound.Eq[2] = 4.5;
                service.Save();
                var reloaded = new SettingsService(path).Current.Sound;
                TestSuite.Equal<int?>(250, reloaded.MicLevelPercent);
                TestSuite.Equal("Robot", reloaded.VoiceEffect);
                TestSuite.Equal(4.5, reloaded.Eq[2]);

                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    Sound = new { MicLevelPercent = 5000, Eq = new[] { 99.0 }, VoiceEffect = "Nope", Surround = 7.0 },
                }));
                var clamped = new SettingsService(path).Current.Sound;
                TestSuite.Equal<int?>(SoundPresets.MaxMicPercent, clamped.MicLevelPercent);
                TestSuite.Equal(SoundPresets.EqFrequencies.Length, clamped.Eq.Length);
                TestSuite.Equal(12.0, clamped.Eq[0]);
                TestSuite.Equal("None", clamped.VoiceEffect);
                TestSuite.Equal(1.0, clamped.Surround);
            }
            finally { File.Delete(path); }
        });

        suite.Run("Peak meter reads float and 16-bit PCM buffers", () =>
        {
            var floats = new[] { 0.1f, -0.6f, 0.3f };
            var bytes = floats.SelectMany(BitConverter.GetBytes).ToArray();
            TestSuite.Equal(0.6f, AudioTestService.Peak(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)));
            var pcm = new short[] { 1000, -16384 }.SelectMany(BitConverter.GetBytes).ToArray();
            TestSuite.Equal(0.5f, AudioTestService.Peak(pcm, pcm.Length, new WaveFormat(48000, 16, 1)));
        });

        suite.Run("Hardware boost steps cover the driver range", () =>
        {
            TestSuite.SequenceEqual(new[] { 0f, 10f, 20f, 30f }, new HardwareBoostInfo(0, 30, 10, 0).Steps);
            var fine = new HardwareBoostInfo(0, 36, 0.5f, 0).Steps;
            TestSuite.Equal(true, fine.Count <= 8);
            TestSuite.Equal(36f, fine[^1]);
            TestSuite.Equal(true, SoundDeviceService.IsBoostName("Microphone Boost"));
            TestSuite.Equal(false, SoundDeviceService.IsBoostName("Master Volume"));
        });
    }

    private static string[] Lines(string text) => text.Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .ToArray();
}
