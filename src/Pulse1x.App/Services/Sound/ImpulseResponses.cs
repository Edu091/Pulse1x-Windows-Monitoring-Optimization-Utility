using System.IO;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Respostas ao impulso dos efeitos de voz que filtros comuns não conseguem fazer: robô (filtro
/// pente com repetições a cada ~9 ms, que impõe um zumbido metálico de 110 Hz à voz), caverna
/// (reflexões + cauda de reverberação) e eco (repetições a cada 280 ms). O Equalizer APO aplica com
/// o comando Convolution, e o arquivo precisa ter a MESMA taxa de amostragem do dispositivo — por
/// isso ele é gerado na hora, para a taxa do microfone escolhido.
///
/// Todas começam com o impulso direto na amostra 0 (fase mínima): a voz não ganha atraso.
/// </summary>
public static class ImpulseResponses
{
    public static readonly string[] Kinds = { "robot", "cave", "echo" };

    public static string FileName(string kind, int sampleRate) => $"pulse1x-ir-{kind}-{sampleRate}.wav";

    public static float[] Generate(string kind, int sampleRate) => kind switch
    {
        "robot" => Robot(sampleRate),
        "cave" => Cave(sampleRate),
        "echo" => Echo(sampleRate),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static float[] Robot(int sr)
    {
        // 26 repetições decaindo 12% cada: picos fortes nos harmônicos de 110 Hz.
        int period = (int)Math.Round(sr / 110.0);
        const int taps = 26;
        const double decay = 0.88;
        var ir = new float[period * taps + 1];
        double energy = 0;
        for (int k = 0; k < taps; k++) energy += Math.Pow(decay, 2 * k);
        double norm = 1 / Math.Sqrt(energy);
        for (int k = 0; k < taps; k++) ir[k * period] = (float)(Math.Pow(decay, k) * norm);
        return ir;
    }

    private static float[] Cave(int sr)
    {
        int length = (int)(sr * 1.8);
        var ir = new float[length];
        ir[0] = 1f;
        // Primeiras reflexões nas paredes da caverna.
        foreach (var (ms, gain) in new[] { (37, 0.55), (59, 0.45), (83, 0.4), (121, 0.35), (163, 0.3) })
            ir[sr * ms / 1000] += (float)gain;

        // Cauda difusa: ruído com queda exponencial (RT60 ≈ 1,6 s), entrando a partir de 25 ms.
        var random = new Random(1717);
        int start = sr * 25 / 1000;
        double rt60 = 1.6;
        for (int n = start; n < length; n++)
        {
            double t = (n - start) / (double)sr;
            double envelope = Math.Pow(10, -3 * t / rt60) * Math.Min(1, t / 0.04);
            ir[n] += (float)((random.NextDouble() * 2 - 1) * envelope * 0.12);
        }
        return ir;
    }

    private static float[] Echo(int sr)
    {
        int step = sr * 280 / 1000;
        var ir = new float[step * 5 + 1];
        ir[0] = 1f;
        double gain = 0.55;
        for (int k = 1; k <= 5; k++, gain *= 0.55) ir[k * step] = (float)gain;
        return ir;
    }

    /// <summary>WAV mono de 32 bits em ponto flutuante (formato 3), lido pelo libsndfile do motor.</summary>
    public static byte[] ToWav(float[] samples, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 4;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)3);          // IEEE float
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 4);
        w.Write((short)4);
        w.Write((short)32);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
