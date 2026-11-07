namespace Pulse1x.App.Services.Sound;

/// <summary>Um perfil de som pronto: as dez bandas do equalizador, os graves e o som 3D.</summary>
public sealed record SoundProfile(string Key, double[] Eq, double BassBoostDb, double Surround);

/// <summary>
/// Um efeito de voz do modificador. É feito só com filtros (equalização), o que roda dentro do
/// próprio driver de áudio do Windows com latência praticamente zero — por isso funciona em
/// qualquer app (Discord, WhatsApp, Meet, jogos) sem trocar o microfone escolhido neles.
/// </summary>
public sealed record VoiceEffect(string Key, double PreampDb, string[] Filters, string? Impulse = null);

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
    /// Efeitos do modificador de voz. Os filtros seguem a sintaxe do Equalizer APO; filtros iguais
    /// em sequência ficam mais íngremes (dois passa-altas = 24 dB/oitava), o que dá o som "de
    /// aparelho" do rádio e do telefone. Robô, caverna, eco e alienígena usam também uma resposta
    /// ao impulso (ver <see cref="ImpulseResponses"/>), que faz o que filtro nenhum faz: repetir a voz.
    /// </summary>
    public static readonly IReadOnlyList<VoiceEffect> VoiceEffects = new[]
    {
        new VoiceEffect("None", 0, Array.Empty<string>()),

        // Voz grossa: muito corpo nos graves e quase nada de brilho.
        new VoiceEffect("Deep", -7, new[]
        {
            "ON LS Fc 280 Hz Gain 11 dB",
            "ON PK Fc 140 Hz Gain 4 dB Q 1",
            "ON HS Fc 1800 Hz Gain -12 dB",
            "ON LPQ Fc 4500 Hz Q 0.7",
        }),

        // Voz fina: sem corpo e com agudos bem abertos.
        new VoiceEffect("Thin", 0, new[]
        {
            "ON HPQ Fc 550 Hz Q 0.9",
            "ON HPQ Fc 550 Hz Q 0.9",
            "ON HS Fc 2500 Hz Gain 10 dB",
            "ON PK Fc 6000 Hz Gain 4 dB Q 1",
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

        // Robô: a voz repetida 26 vezes a cada 9 ms vira um zumbido metálico de 110 Hz.
        new VoiceEffect("Robot", -3, new[]
        {
            "ON HPQ Fc 180 Hz Q 0.7",
            "ON LPQ Fc 6000 Hz Q 0.7",
        }, Impulse: "robot"),

        // Abafado: como se a pessoa falasse do outro lado da parede.
        new VoiceEffect("Muffled", 4, new[]
        {
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON LPQ Fc 700 Hz Q 0.7",
            "ON PK Fc 250 Hz Gain 3 dB Q 1",
        }),

        // Caverna: reflexões nas paredes e uma cauda longa de reverberação.
        new VoiceEffect("Cave", -6, new[]
        {
            "ON HPQ Fc 120 Hz Q 0.7",
        }, Impulse: "cave"),

        // Eco: a frase volta quatro vezes, cada vez mais baixa.
        new VoiceEffect("Echo", -3, Array.Empty<string>(), Impulse: "echo"),

        // Alienígena: voz fina e metálica ao mesmo tempo.
        new VoiceEffect("Alien", -2, new[]
        {
            "ON HPQ Fc 600 Hz Q 1",
            "ON HPQ Fc 600 Hz Q 1",
            "ON HS Fc 3000 Hz Gain 8 dB",
        }, Impulse: "robot"),
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
