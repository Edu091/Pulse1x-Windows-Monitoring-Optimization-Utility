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
            TestSuite.Equal((100, 12.04), SoundPresets.SplitMicLevel(999));
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

        suite.Run("Factory Equalizer APO config is recognized; user configs are kept", () =>
        {
            TestSuite.Equal(true, EqualizerApoService.IsFactoryConfig(""));
            TestSuite.Equal(true, EqualizerApoService.IsFactoryConfig("Preamp: -6 dB\r\nInclude: example.txt\r\n"));
            TestSuite.Equal(false, EqualizerApoService.IsFactoryConfig("Preamp: -6 dB\nInclude: my-room.txt\n"));
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
