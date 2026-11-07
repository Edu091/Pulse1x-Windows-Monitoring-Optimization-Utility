namespace Pulse1x.App.Services.Sound;

/// <summary>Um perfil de som pronto: as dez bandas do equalizador, os graves e o som 3D.</summary>
public sealed record SoundProfile(string Key, double[] Eq, double BassBoostDb, double Surround);

/// <summary>
/// Um efeito de voz do modificador. É feito só com filtros (equalização), o que roda dentro do
/// próprio driver de áudio do Windows com latência praticamente zero — por isso funciona em
/// qualquer app (Discord, WhatsApp, Meet, jogos) sem trocar o microfone escolhido neles.
/// </summary>
public sealed record VoiceEffect(string Key, double PreampDb, string[] Filters);

public static class SoundPresets
{
    /// <summary>Frequências centrais do equalizador gráfico de 10 bandas (padrão ISO por oitava).</summary>
    public static readonly int[] EqFrequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// <summary>Teto do volume do microfone (400% = +12 dB de ganho digital).</summary>
    public const int MaxMicPercent = 400;

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
    /// Efeitos do modificador de voz. Os filtros seguem a sintaxe do Equalizer APO. Filtros iguais
    /// em sequência ficam mais íngremes (ex.: dois passa-altas = corte de 24 dB/oitava), o que dá o
    /// som "de aparelho" do rádio e do telefone.
    /// </summary>
    public static readonly IReadOnlyList<VoiceEffect> VoiceEffects = new[]
    {
        new VoiceEffect("None", 0, Array.Empty<string>()),

        // Voz grossa: mais corpo nos graves e menos brilho em cima.
        new VoiceEffect("Deep", -4, new[]
        {
            "ON LS Fc 220 Hz Gain 9 dB",
            "ON PK Fc 120 Hz Gain 3 dB Q 1",
            "ON HS Fc 2500 Hz Gain -7 dB",
        }),

        // Voz fina: tira o corpo e abre os agudos.
        new VoiceEffect("Thin", 2, new[]
        {
            "ON HPQ Fc 380 Hz Q 0.9",
            "ON HPQ Fc 380 Hz Q 0.9",
            "ON HS Fc 3000 Hz Gain 8 dB",
            "ON PK Fc 5500 Hz Gain 3 dB Q 1",
        }),

        // Rádio: faixa estreita com uma "buzina" nos médios.
        new VoiceEffect("Radio", 3, new[]
        {
            "ON HPQ Fc 450 Hz Q 0.8",
            "ON HPQ Fc 450 Hz Q 0.8",
            "ON LPQ Fc 3200 Hz Q 0.8",
            "ON LPQ Fc 3200 Hz Q 0.8",
            "ON PK Fc 1700 Hz Gain 8 dB Q 1.2",
        }),

        // Telefone: a banda clássica de 300 a 3400 Hz.
        new VoiceEffect("Phone", 2, new[]
        {
            "ON HPQ Fc 320 Hz Q 0.7",
            "ON HPQ Fc 320 Hz Q 0.7",
            "ON LPQ Fc 3400 Hz Q 0.7",
            "ON LPQ Fc 3400 Hz Q 0.7",
            "ON PK Fc 1000 Hz Gain 4 dB Q 1",
        }),

        // Megafone: banda bem estreita, médios estourados e ganho alto (a saturação faz parte).
        new VoiceEffect("Megaphone", 9, new[]
        {
            "ON HPQ Fc 750 Hz Q 1",
            "ON HPQ Fc 750 Hz Q 1",
            "ON LPQ Fc 4000 Hz Q 1",
            "ON LPQ Fc 4000 Hz Q 1",
            "ON PK Fc 2200 Hz Gain 12 dB Q 1.5",
        }),

        // Robô: ressonâncias estreitas em série harmônica dão o timbre metálico de "lata".
        new VoiceEffect("Robot", -10, new[]
        {
            "ON HPQ Fc 200 Hz Q 0.7",
            "ON PK Fc 300 Hz Gain 12 dB Q 18",
            "ON PK Fc 600 Hz Gain 12 dB Q 18",
            "ON PK Fc 900 Hz Gain 12 dB Q 18",
            "ON PK Fc 1200 Hz Gain 12 dB Q 18",
            "ON PK Fc 1500 Hz Gain 12 dB Q 18",
            "ON PK Fc 1800 Hz Gain 12 dB Q 18",
            "ON PK Fc 2400 Hz Gain 12 dB Q 18",
            "ON PK Fc 3000 Hz Gain 12 dB Q 18",
            "ON LPQ Fc 5000 Hz Q 0.7",
        }),

        // Abafado: como se a pessoa falasse do outro lado da parede.
        new VoiceEffect("Muffled", 4, new[]
        {
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON PK Fc 250 Hz Gain 3 dB Q 1",
        }),

        // Alienígena: graves fora, um vale nos médios e agudos ressonantes.
        new VoiceEffect("Alien", 0, new[]
        {
            "ON HPQ Fc 500 Hz Q 1.5",
            "ON NO Fc 1200 Hz Q 2",
            "ON PK Fc 2800 Hz Gain 12 dB Q 6",
            "ON PK Fc 4200 Hz Gain 10 dB Q 6",
            "ON HS Fc 6000 Hz Gain 4 dB",
        }),
    };

    public static VoiceEffect FindVoice(string key) =>
        VoiceEffects.FirstOrDefault(v => v.Key == key) ?? VoiceEffects[0];

    /// <summary>
    /// Converte o volume do microfone (0 a 400%) nas duas partes que o compõem: o volume do Windows
    /// (0 a 100%) e o ganho digital extra em dB (0 dB até 100%; 400% = +12 dB).
    /// </summary>
    public static (int WindowsPercent, double ExtraGainDb) SplitMicLevel(int percent)
    {
        percent = Math.Clamp(percent, 0, MaxMicPercent);
        if (percent <= 100) return (percent, 0);
        return (100, Math.Round(20 * Math.Log10(percent / 100.0), 2));
    }
}
