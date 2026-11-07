namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Tudo o que a categoria Som guarda no settings.json. Os ajustes valem para um dispositivo de
/// saída e um microfone escolhidos (pelo Id do Windows); null quer dizer "o padrão do momento".
/// </summary>
public class SoundSettings
{
    // ---------------- Saída (alto-falantes / fones) ----------------

    public string? OutputDeviceId { get; set; }

    /// <summary>Liga o equalizador, o reforço de graves e o som 3D no dispositivo de saída.</summary>
    public bool OutputEffectsEnabled { get; set; }

    /// <summary>Chave do perfil de som (ver <see cref="SoundPresets"/>); "Custom" quando mexido à mão.</summary>
    public string OutputProfile { get; set; } = "Flat";

    /// <summary>Ganho de cada banda do equalizador, em dB (mesma ordem de <see cref="SoundPresets.EqFrequencies"/>).</summary>
    public double[] Eq { get; set; } = new double[SoundPresets.EqFrequencies.Length];

    /// <summary>Reforço de graves, em dB (0 a 12).</summary>
    public double BassBoostDb { get; set; }

    /// <summary>Som 3D (ampliação estéreo), de 0 (desligado) a 1 (máximo).</summary>
    public double Surround { get; set; }

    /// <summary>Volume extra na saída, em dB (-12 a +12; acima de 0 passa dos 100% do Windows), somado à compensação automática anticlipping.</summary>
    public double OutputGainDb { get; set; }

    // ---------------- Microfone ----------------

    public string? InputDeviceId { get; set; }

    /// <summary>
    /// Volume do microfone de 0 a 1000%. Até 100% é o volume do próprio Windows; acima disso o
    /// Windows fica em 100% e o resto vira ganho digital no motor de áudio. null = não gerenciado.
    /// </summary>
    public int? MicLevelPercent { get; set; }

    /// <summary>Amplificação de hardware do driver ("Microphone Boost"), em dB. null = não mexer.</summary>
    public float? MicHardwareBoostDb { get; set; }

    /// <summary>Liga o processamento do microfone (ganho acima de 100%, filtros e modificador de voz).</summary>
    public bool MicEffectsEnabled { get; set; } = true;

    /// <summary>Chave do efeito de voz (ver <see cref="SoundPresets.VoiceEffects"/>).</summary>
    public string VoiceEffect { get; set; } = "None";

    /// <summary>Corta ronco e vibração abaixo de ~90 Hz (mesa, ventoinha, ar-condicionado).</summary>
    public bool MicLowCut { get; set; }

    /// <summary>Realça a faixa de inteligibilidade da voz (~3 kHz).</summary>
    public bool MicClarity { get; set; }

    // ---------------- Dispositivo padrão ----------------

    /// <summary>Padrão anterior de cada tipo, para "Remover padrão" devolver o Windows a ele.</summary>
    public string? PreviousDefaultOutputId { get; set; }
    public string? PreviousDefaultInputId { get; set; }

    // ---------------- Inicialização ----------------

    /// <summary>Reaplica volume do microfone, amplificação e efeitos a cada logon do Windows.</summary>
    public bool ApplyOnStartup { get; set; }

    /// <summary>Enquanto o Pulse1x estiver aberto, devolve o volume do microfone se outro app mexer nele.</summary>
    public bool LockMicLevel { get; set; }

    public void Normalize()
    {
        if (Eq is null || Eq.Length != SoundPresets.EqFrequencies.Length)
        {
            var fixedEq = new double[SoundPresets.EqFrequencies.Length];
            if (Eq is not null) Array.Copy(Eq, fixedEq, Math.Min(Eq.Length, fixedEq.Length));
            Eq = fixedEq;
        }
        for (int i = 0; i < Eq.Length; i++) Eq[i] = Clamp(Eq[i], -12, 12);
        BassBoostDb = Clamp(BassBoostDb, 0, 12);
        Surround = Clamp(Surround, 0, 1);
        OutputGainDb = Clamp(OutputGainDb, -12, 12);
        if (MicLevelPercent is int level) MicLevelPercent = Math.Clamp(level, 0, SoundPresets.MaxMicPercent);
        OutputProfile = string.IsNullOrWhiteSpace(OutputProfile) ? "Flat" : OutputProfile;
        VoiceEffect = SoundPresets.VoiceEffects.Any(v => v.Key == VoiceEffect) ? VoiceEffect : "None";
    }

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : 0;
}
