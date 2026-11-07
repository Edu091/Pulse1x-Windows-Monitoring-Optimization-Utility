using System.Globalization;
using System.Text;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Monta o arquivo de configuração que o Equalizer APO lê (pulse1x.txt). Função pura — recebe os
/// ajustes e os GUIDs dos dispositivos e devolve o texto —, para ser testável sem o motor instalado.
///
/// Cada bloco começa com "Device: {GUID}" (desde a 1.2.1 um padrão com chave casa só com o GUID do
/// dispositivo, então o efeito nunca vaza para outro aparelho) e o arquivo termina com
/// "Device: all"/"Channel: all", para não contaminar linhas que o usuário tenha no config.txt.
/// </summary>
public static class EqualizerApoConfig
{
    public const string Header = "# Gerado pelo Pulse1x (categoria Som). Alteracoes feitas a mao aqui serao sobrescritas.";

    public static string Build(SoundSettings settings, string? outputGuid, string? inputGuid, int inputSampleRate = 0)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);

        if (settings.OutputEffectsEnabled && !string.IsNullOrEmpty(outputGuid))
            AppendOutput(sb, settings, outputGuid);

        if (settings.MicEffectsEnabled && !string.IsNullOrEmpty(inputGuid))
            AppendInput(sb, settings, inputGuid, inputSampleRate);

        sb.AppendLine("Device: all");
        sb.AppendLine("Channel: all");
        return sb.ToString();
    }

    private static void AppendOutput(StringBuilder sb, SoundSettings s, string guid)
    {
        bool anyEq = s.Eq.Any(g => Math.Abs(g) >= 0.05);
        bool bass = s.BassBoostDb >= 0.05;
        bool surround = s.Surround >= 0.01;

        sb.AppendLine();
        sb.AppendLine("# Saida: equalizador, graves e som 3D");
        sb.AppendLine($"Device: {guid}");
        sb.AppendLine($"Preamp: {N(OutputPreampDb(s))} dB");

        if (surround)
        {
            // Ampliação estéreo por meio/lado: L' = M + k*S. O que está no centro (voz, tiros à
            // frente) não muda; o que está nas laterais se abre. k vai de 1 (natural) a 2,5.
            double k = 1 + 1.5 * s.Surround;
            double a = (1 + k) / 2, b = (1 - k) / 2;
            sb.AppendLine($"Copy: L={N(a)}*L+{N(b)}*R R={N(a)}*R+{N(b)}*L");
        }

        if (anyEq)
        {
            var bands = SoundPresets.EqFrequencies.Select((f, i) => $"{f} {N(s.Eq[i])}");
            sb.AppendLine("GraphicEQ: " + string.Join("; ", bands));
        }

        if (bass)
            sb.AppendLine($"Filter: ON LSC Fc 110 Hz Gain {N(s.BassBoostDb)} dB Q 0.7");
    }

    private static void AppendInput(StringBuilder sb, SoundSettings s, string guid, int sampleRate)
    {
        var voice = SoundPresets.FindVoice(s.VoiceEffect);
        double gain = s.MicLevelPercent is int level ? SoundPresets.SplitMicLevel(level).ExtraGainDb : 0;
        if (gain <= 0 && voice.Filters.Length == 0 && voice.Impulse is null && !s.MicLowCut && !s.MicClarity) return;

        sb.AppendLine();
        sb.AppendLine("# Microfone: volume acima de 100%, filtros e modificador de voz");
        sb.AppendLine($"Device: {guid}");
        double preamp = gain + voice.PreampDb + (s.MicClarity ? -1 : 0);
        if (Math.Abs(preamp) >= 0.01)
            sb.AppendLine($"Preamp: {N(preamp)} dB");
        if (s.MicLowCut)
            sb.AppendLine("Filter: ON HPQ Fc 90 Hz Q 0.7");
        if (s.MicClarity)
            sb.AppendLine("Filter: ON PK Fc 3200 Hz Gain 4 dB Q 0.9");
        foreach (var filter in voice.Filters)
            sb.AppendLine("Filter: " + filter);
        // A resposta ao impulso só vale na taxa exata do microfone; sem saber a taxa, fica de fora.
        if (voice.Impulse is string impulse && sampleRate > 0)
            sb.AppendLine("Convolution: " + ImpulseResponses.FileName(impulse, sampleRate));
    }

    /// <summary>
    /// Pré-amplificação da saída: abaixa o volume o quanto o equalizador e os graves sobem, para o
    /// som não estourar (clipping), e soma o ganho manual escolhido pelo usuário.
    /// </summary>
    public static double OutputPreampDb(SoundSettings s)
    {
        double maxEq = Math.Max(0, s.Eq.DefaultIfEmpty(0).Max());
        // O shelf de graves soma por cima das bandas mais baixas.
        double lowBands = Math.Max(0, Math.Max(s.Eq.ElementAtOrDefault(0), s.Eq.ElementAtOrDefault(1)));
        double boost = Math.Max(maxEq, lowBands + s.BassBoostDb);
        double surround = s.Surround > 0 ? 20 * Math.Log10(1 + 0.75 * s.Surround) : 0;
        return Math.Round(-Math.Min(boost + surround, 24) + s.OutputGainDb, 1);
    }

    /// <summary>
    /// Extrai o GUID do Id de um dispositivo do Windows ("{0.0.1.00000000}.{guid}" → "{guid}"),
    /// que é a forma estável de apontar o dispositivo no Equalizer APO.
    /// </summary>
    public static string? ExtractGuid(string? endpointId)
    {
        if (string.IsNullOrEmpty(endpointId)) return null;
        int start = endpointId.LastIndexOf('{');
        int end = endpointId.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        string guid = endpointId.Substring(start, end - start + 1);
        return Guid.TryParse(guid, out _) ? guid.ToLowerInvariant() : null;
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
