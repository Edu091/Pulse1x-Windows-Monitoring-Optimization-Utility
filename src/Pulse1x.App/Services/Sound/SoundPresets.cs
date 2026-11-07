namespace Pulse1x.App.Services.Sound;

/// <summary>Um perfil de som pronto: as dez bandas do equalizador, os graves e o som 3D.</summary>
public sealed record SoundProfile(string Key, double[] Eq, double BassBoostDb, double Surround);

/// <summary>
/// Um efeito de voz do modificador. É feito só com filtros (equalização), o que roda dentro do
/// próprio driver de áudio do Windows com latência praticamente zero — por isso funciona em
/// qualquer app (Discord, WhatsApp, Meet, jogos) sem trocar o microfone escolhido neles.
/// </summary>
public sealed record VoiceEffect(string Key, double PreampDb, string[] Filters, string? Impulse = null, string? Dsp = null);

public static class SoundPresets
{
    /// <summary>Frequências centrais do equalizador gráfico de 10 bandas (padrão ISO por oitava).</summary>
    public static readonly int[] EqFrequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// <summary>Teto do volume do microfone (1000% = +20 dB de ganho digital).</summary>
    public const int MaxMicPercent = 1000;

    public static readonly IReadOnlyList<SoundProfile> Profiles = new[]
    {
        new SoundProfile("Flat",    new double[] {  0,    0,    0,  0,    0,   0,   0,   0,   0,   0   }, 0, 0),
        new SoundProfile("Music",   new double[] {  3,    2.5,  1.5, 0,  -0.5, 0,   1,   2,   2.5, 3   }, 2, 0.25),
        new SoundProfile("Movies",  new double[] {  4,    3,    1,   0,   0,   1.5, 2.5, 2,   1.5, 1   }, 3, 0.5),
        new SoundProfile("Games",   new double[] { -2,   -1.5, -1,   0,   1,   2,   4,   5,   3,   1   }, 0, 0.35),
        new SoundProfile("Voice",   new double[] { -6,   -4,   -2,   0,   2,   3.5, 3.5, 2,   0,  -1   }, 0, 0),
        new SoundProfile("Bass",    new double[] {  7,    6,    4,   1,   0,   0,   0,   0.5, 1,   1.5 }, 6, 0.2),
        new SoundProfile("Treble",  new double[] {  0,    0,    0,   0,   0,   0.5, 1.5, 3,   4,   4.5 }, 0, 0.1),
        new SoundProfile("Night",   new double[] { -4,   -3,   -1,   0,   1,   2,   2,   0,  -1,  -2   }, 0, 0),
    };

    public static SoundProfile? FindProfile(string key) => Profiles.FirstOrDefault(p => p.Key == key);

    /// <summary>
    /// Efeitos do modificador de voz. Cada um combina até três partes, nesta ordem:
    /// filtros do Equalizer APO (o "aparelho": rádio, telefone, megafone), uma resposta ao impulso
    /// (ver <see cref="ImpulseResponses"/>) e o plugin de voz do Pulse1x (<c>Dsp</c>), que faz o que
    /// filtro nenhum faz — mudar o tom, robô, sussurro, anel, distorção, 8-bit e eco.
    /// </summary>
    public static readonly IReadOnlyList<VoiceEffect> VoiceEffects = new[]
    {
        new VoiceEffect("None", 0, Array.Empty<string>()),

        // ---- Tom ----
        new VoiceEffect("Deep", 0, new[] { "ON LS Fc 200 Hz Gain 3 dB" }, Dsp: "pitch=-4"),
        new VoiceEffect("Monster", -2, new[] { "ON LS Fc 180 Hz Gain 4 dB" }, Dsp: "pitch=-9;drive=2.5"),
        new VoiceEffect("Thin", 0, Array.Empty<string>(), Dsp: "pitch=4"),
        new VoiceEffect("Chipmunk", 0, new[] { "ON HPQ Fc 150 Hz Q 0.7" }, Dsp: "pitch=9"),

        // ---- Máquinas e criaturas ----
        new VoiceEffect("Robot", 0, new[] { "ON HPQ Fc 150 Hz Q 0.7" }, Dsp: "robot=110"),
        new VoiceEffect("Cyborg", -3, new[] { "ON HPQ Fc 180 Hz Q 0.7" }, Impulse: "robot", Dsp: "ring=55;ringmix=0.8"),
        new VoiceEffect("Alien", 0, new[] { "ON HPQ Fc 200 Hz Q 0.7" }, Dsp: "pitch=5;ring=420;ringmix=0.5"),
        new VoiceEffect("Retro", 0, new[] { "ON HPQ Fc 200 Hz Q 0.7" }, Dsp: "pitch=2;crush=5500;bits=5"),

        // ---- Clima ----
        new VoiceEffect("Whisper", 2, new[] { "ON HPQ Fc 300 Hz Q 0.7" }, Dsp: "whisper=1"),
        new VoiceEffect("Ghost", 0, new[] { "ON HPQ Fc 250 Hz Q 0.7" }, Dsp: "pitch=-3;whisper=1;echo=180;echofb=0.5;echomix=0.5"),
        new VoiceEffect("Drunk", 0, Array.Empty<string>(), Dsp: "pitch=-1;vib=1.2;vibrate=0.7"),
        new VoiceEffect("Cave", -6, new[] { "ON HPQ Fc 120 Hz Q 0.7" }, Impulse: "cave"),
        new VoiceEffect("Echo", -2, Array.Empty<string>(), Dsp: "echo=300;echofb=0.45;echomix=0.55"),

        // ---- Aparelhos ----
        new VoiceEffect("Radio", 3, new[]
        {
            "ON HPQ Fc 450 Hz Q 0.8",
            "ON HPQ Fc 450 Hz Q 0.8",
            "ON LPQ Fc 3200 Hz Q 0.8",
            "ON LPQ Fc 3200 Hz Q 0.8",
            "ON PK Fc 1700 Hz Gain 8 dB Q 1.2",
        }, Dsp: "drive=2"),

        // Telefone: a banda de 300 a 3400 Hz e a "compressão" de um codec antigo (8 kHz, 8 bits).
        new VoiceEffect("Phone", 4, new[]
        {
            "ON HPQ Fc 350 Hz Q 0.7",
            "ON HPQ Fc 350 Hz Q 0.7",
            "ON LPQ Fc 3000 Hz Q 0.7",
            "ON LPQ Fc 3000 Hz Q 0.7",
            "ON PK Fc 1500 Hz Gain 6 dB Q 1",
        }, Dsp: "crush=8000;bits=7;drive=1.8"),

        new VoiceEffect("Megaphone", 6, new[]
        {
            "ON HPQ Fc 750 Hz Q 1",
            "ON HPQ Fc 750 Hz Q 1",
            "ON LPQ Fc 4000 Hz Q 1",
            "ON LPQ Fc 4000 Hz Q 1",
            "ON PK Fc 2200 Hz Gain 10 dB Q 1.5",
        }, Dsp: "drive=6"),

        new VoiceEffect("Muffled", 4, new[]
        {
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON PK Fc 250 Hz Gain 3 dB Q 1",
        }),
    };
    public static VoiceEffect FindVoice(string key) =>
        VoiceEffects.FirstOrDefault(v => v.Key == key) ?? VoiceEffects[0];

    /// <summary>
    /// Converte o volume do microfone (0 a 400%) nas duas partes que o compõem: o volume do Windows
    /// (0 a 100%) e o ganho digital extra em dB (0 dB até 100%; 1000% = +20 dB).
    /// </summary>
    public static (int WindowsPercent, double ExtraGainDb) SplitMicLevel(int percent)
    {
        percent = Math.Clamp(percent, 0, MaxMicPercent);
        if (percent <= 100) return (percent, 0);
        return (100, Math.Round(20 * Math.Log10(percent / 100.0), 2));
    }
}
