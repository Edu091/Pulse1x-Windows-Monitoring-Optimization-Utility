namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Sons embutidos da mesa de sons, sintetizados na hora (mono, float, na taxa pedida). Nenhum é
/// gravação de terceiros: são recriações por síntese — osciladores, ruído filtrado e envelopes —,
/// então não há direitos autorais envolvidos e o app não carrega arquivos de áudio.
/// </summary>
public static class SoundboardSynth
{
    public static readonly string[] Keys =
    {
        "fart", "scream", "airhorn", "boom", "sadtrombone", "rimshot", "crickets",
        "applause", "boing", "buzzer", "ding", "explosion", "siren", "laser",
    };

    public static float[] Generate(string key, int sr)
    {
        var rng = new Random(key.GetHashCode() & 0x7fffffff);
        float[] s = key switch
        {
            "fart" => Fart(sr, rng),
            "scream" => Scream(sr, rng),
            "airhorn" => Airhorn(sr),
            "boom" => Boom(sr, rng),
            "sadtrombone" => SadTrombone(sr),
            "rimshot" => Rimshot(sr, rng),
            "crickets" => Crickets(sr),
            "applause" => Applause(sr, rng),
            "boing" => Boing(sr),
            "buzzer" => Buzzer(sr),
            "ding" => Ding(sr),
            "explosion" => Explosion(sr, rng),
            "siren" => Siren(sr),
            "laser" => Laser(sr),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        return Normalize(s, 0.9f);
    }

    // ------------------------------------------------------------------ sons

    private static float[] Fart(int sr, Random rng)
    {
        // Pulsos graves e irregulares (60–110 Hz) passando por ruído filtrado: o "trrr" característico.
        int n = (int)(sr * 1.1);
        var o = new float[n];
        var lp = Biquad.LowPass(sr, 420, 0.9);
        var bp = Biquad.BandPass(sr, 160, 1.4);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = 95 - 35 * t + 18 * Math.Sin(2 * Math.PI * 3.3 * t) + (rng.NextDouble() - 0.5) * 25;
            phase += f / sr;
            double pulse = (phase % 1) < 0.35 ? 1 : -0.4;
            double noise = rng.NextDouble() * 2 - 1;
            double x = pulse * (0.7 + 0.3 * noise);
            double env = Math.Min(1, t / 0.03) * Math.Min(1, (1.1 - t) / 0.25) * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 5 * t));
            o[i] = (float)(Math.Tanh(2.2 * (lp.Process(x) * 0.8 + bp.Process(x) * 0.6)) * env);
        }
        return o;
    }

    private static float[] Scream(int sr, Random rng)
    {
        // Dente de serra subindo e descendo, com vibrato e formantes de "aaah", e um pouco de rouquidão.
        int n = (int)(sr * 1.4);
        var o = new float[n];
        var f1 = Biquad.BandPass(sr, 850, 4);
        var f2 = Biquad.BandPass(sr, 1250, 5);
        var f3 = Biquad.BandPass(sr, 2900, 6);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double glide = t < 0.25 ? 480 + 900 * t : 705 - 60 * (t - 0.25);
            double f = glide * (1 + 0.035 * Math.Sin(2 * Math.PI * 6.5 * t));
            phase += f / sr;
            double saw = 2 * (phase % 1) - 1;
            double x = saw * 0.8 + (rng.NextDouble() * 2 - 1) * 0.25;
            double y = f1.Process(x) * 1.0 + f2.Process(x) * 0.7 + f3.Process(x) * 0.35;
            double env = Math.Min(1, t / 0.06) * Math.Min(1, (1.4 - t) / 0.3);
            o[i] = (float)(Math.Tanh(3 * y) * env);
        }
        return o;
    }

    private static float[] Airhorn(int sr)
    {
        // Acorde de metais saturado: três toques curtos e um longo.
        var blasts = new (double Start, double Len)[] { (0, 0.16), (0.22, 0.16), (0.44, 0.16), (0.66, 0.85) };
        int n = (int)(sr * 1.6);
        var o = new float[n];
        double[] freqs = { 466.2, 587.3, 698.5, 932.3 };
        var lp = Biquad.LowPass(sr, 3200, 0.8);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double env = 0;
            foreach (var (start, len) in blasts)
                if (t >= start && t < start + len)
                    env = Math.Min(1, (t - start) / 0.01) * Math.Min(1, (start + len - t) / 0.03);
            if (env == 0) { lp.Process(0); continue; }
            double x = 0;
            foreach (var f in freqs)
                for (int d = -1; d <= 1; d++)
                    x += 2 * ((f * (1 + d * 0.004) * t) % 1) - 1;
            o[i] = (float)(Math.Tanh(1.6 * lp.Process(x / 8)) * env);
        }
        return o;
    }

    private static float[] Boom(int sr, Random rng)
    {
        // Pancada grave que desce de 110 para 38 Hz, com estalo de ataque e cauda longa.
        int n = (int)(sr * 1.8);
        var o = new float[n];
        double phase = 0;
        var lp = Biquad.LowPass(sr, 900, 0.7);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = 38 + 72 * Math.Exp(-t * 9);
            phase += f / sr;
            double body = Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 2.2);
            double click = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 90);
            o[i] = (float)Math.Tanh(2.5 * (body + 0.6 * lp.Process(click)));
        }
        return o;
    }

    private static float[] SadTrombone(int sr)
    {
        // "Wah wah wah waaah": quatro notas descendo, a última com vibrato.
        var notes = new (double F, double Start, double Len)[] { (293.7, 0, 0.45), (277.2, 0.5, 0.45), (261.6, 1.0, 0.45), (246.9, 1.5, 1.3) };
        int n = (int)(sr * 2.9);
        var o = new float[n];
        var lp = Biquad.LowPass(sr, 1400, 1.2);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double x = 0;
            foreach (var (f, start, len) in notes)
            {
                if (t < start || t >= start + len) continue;
                double lt = t - start;
                double vib = start >= 1.5 ? 1 + 0.03 * Math.Sin(2 * Math.PI * 5.5 * lt) * Math.Min(1, lt / 0.3) : 1;
                phase += f * vib / sr;
                double wah = 0.35 + 0.65 * Math.Min(1, lt / 0.12);
                x = (2 * (phase % 1) - 1) * wah * Math.Min(1, (start + len - t) / 0.08);
            }
            o[i] = (float)Math.Tanh(1.8 * lp.Process(x));
        }
        return o;
    }

    private static float[] Rimshot(int sr, Random rng)
    {
        // "Ba-dum-tss": dois tons e o prato.
        int n = (int)(sr * 1.6);
        var o = new float[n];
        var hp = Biquad.HighPass(sr, 6000, 0.7);
        void Tom(double at, double f)
        {
            int s0 = (int)(at * sr);
            for (int i = s0; i < Math.Min(n, s0 + sr / 2); i++)
            {
                double t = (double)(i - s0) / sr;
                o[i] += (float)(Math.Sin(2 * Math.PI * f * t * (1 + 0.5 * Math.Exp(-t * 30))) * Math.Exp(-t * 14) * 0.9);
            }
        }
        Tom(0, 190);
        Tom(0.18, 130);
        int c0 = (int)(0.42 * sr);
        for (int i = c0; i < n; i++)
        {
            double t = (double)(i - c0) / sr;
            o[i] += (float)(hp.Process(rng.NextDouble() * 2 - 1) * Math.Exp(-t * 3.2) * 1.4);
        }
        return o;
    }

    private static float[] Crickets(int sr)
    {
        // Silêncio constrangedor: trinados de grilo a ~4,5 kHz em grupos.
        int n = sr * 3;
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double group = (t % 0.9) < 0.32 ? 1 : 0;
            double pulse = Math.Max(0, Math.Sin(2 * Math.PI * 28 * t));
            double tone = Math.Sin(2 * Math.PI * 4400 * t) + 0.3 * Math.Sin(2 * Math.PI * 8800 * t);
            double env = Math.Min(1, t / 0.2) * Math.Min(1, (3 - t) / 0.4);
            o[i] = (float)(tone * pulse * group * env * 0.6);
        }
        return o;
    }

    private static float[] Applause(int sr, Random rng)
    {
        // Centenas de palmas curtas (ruído filtrado), densidade crescendo e caindo.
        int n = (int)(sr * 3.2);
        var o = new float[n];
        int claps = 520;
        for (int c = 0; c < claps; c++)
        {
            double at = Math.Pow(rng.NextDouble(), 0.8) * 2.9;
            var bp = Biquad.BandPass(sr, 900 + rng.NextDouble() * 1800, 1.2);
            double amp = 0.3 + rng.NextDouble() * 0.7;
            int s0 = (int)(at * sr);
            for (int i = s0; i < Math.Min(n, s0 + sr / 20); i++)
            {
                double t = (double)(i - s0) / sr;
                o[i] += (float)(bp.Process(rng.NextDouble() * 2 - 1) * Math.Exp(-t * 120) * amp);
            }
        }
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            o[i] *= (float)(Math.Min(1, t / 0.3) * Math.Min(1, (3.2 - t) / 0.8));
        }
        return o;
    }

    private static float[] Boing(int sr)
    {
        // Mola: o tom oscila rápido e vai assentando.
        int n = (int)(sr * 1.0);
        var o = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = 260 + 170 * Math.Sin(2 * Math.PI * 9 * t) * Math.Exp(-t * 3.5) + 120 * t;
            phase += f / sr;
            o[i] = (float)(Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 2.8) * Math.Min(1, t / 0.005));
        }
        return o;
    }

    private static float[] Buzzer(int sr)
    {
        // "Errou": duas ondas quadradas graves batendo entre si.
        int n = (int)(sr * 0.9);
        var o = new float[n];
        var lp = Biquad.LowPass(sr, 2200, 0.7);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double x = Math.Sign(Math.Sin(2 * Math.PI * 110 * t)) + Math.Sign(Math.Sin(2 * Math.PI * 116.5 * t));
            o[i] = (float)(lp.Process(x * 0.5) * Math.Min(1, t / 0.01) * Math.Min(1, (0.9 - t) / 0.05));
        }
        return o;
    }

    private static float[] Ding(int sr)
    {
        // "Acertou": dois sinos em sequência, com parciais inarmônicos.
        int n = (int)(sr * 1.6);
        var o = new float[n];
        void Bell(double at, double f)
        {
            int s0 = (int)(at * sr);
            double[] ratios = { 1, 2.76, 5.4 }, amps = { 1, 0.4, 0.2 };
            for (int i = s0; i < n; i++)
            {
                double t = (double)(i - s0) / sr;
                double x = 0;
                for (int k = 0; k < ratios.Length; k++)
                    x += amps[k] * Math.Sin(2 * Math.PI * f * ratios[k] * t) * Math.Exp(-t * (2.5 + k * 2));
                o[i] += (float)(x * 0.6);
            }
        }
        Bell(0, 1318.5);
        Bell(0.16, 1760);
        return o;
    }

    private static float[] Explosion(int sr, Random rng)
    {
        // Ruído que fecha de agudo para grave, com uma pancada grave por baixo.
        int n = (int)(sr * 2.6);
        var o = new float[n];
        var boom = Boom(sr, rng);
        var lp = Biquad.LowPass(sr, 4000, 0.7);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            lp.SetLowPass(sr, 150 + 3800 * Math.Exp(-t * 2.5), 0.7);
            double noise = lp.Process(rng.NextDouble() * 2 - 1) * Math.Exp(-t * 1.6) * Math.Min(1, t / 0.004);
            o[i] = (float)Math.Tanh(2 * (noise * 1.3 + (i < boom.Length ? boom[i] * 0.8 : 0)));
        }
        return o;
    }

    private static float[] Siren(int sr)
    {
        int n = (int)(sr * 2.6);
        var o = new float[n];
        double phase = 0;
        var lp = Biquad.LowPass(sr, 3000, 0.7);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = 900 + 380 * Math.Sin(2 * Math.PI * 0.75 * t - Math.PI / 2);
            phase += f / sr;
            double x = 2 * Math.Abs(2 * (phase % 1) - 1) - 1; // triangular
            o[i] = (float)(lp.Process(x) * Math.Min(1, t / 0.05) * Math.Min(1, (2.6 - t) / 0.2));
        }
        return o;
    }

    private static float[] Laser(int sr)
    {
        // "Piu piu piu": três disparos descendo de 2 kHz para 200 Hz.
        int n = (int)(sr * 0.95);
        var o = new float[n];
        for (int shot = 0; shot < 3; shot++)
        {
            int s0 = (int)(shot * 0.28 * sr);
            double phase = 0;
            for (int i = s0; i < Math.Min(n, s0 + (int)(0.24 * sr)); i++)
            {
                double t = (double)(i - s0) / sr;
                double f = 200 + 1900 * Math.Exp(-t * 16);
                phase += f / sr;
                o[i] += (float)(Math.Sign(Math.Sin(2 * Math.PI * phase)) * 0.5 * Math.Exp(-t * 9));
            }
        }
        return o;
    }

    // ------------------------------------------------------------------ utilitários

    private static float[] Normalize(float[] s, float peak)
    {
        float max = 0;
        foreach (var v in s) max = Math.Max(max, Math.Abs(v));
        if (max < 1e-6f) return s;
        float g = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= g;
        return s;
    }

    /// <summary>Filtro biquad (receitas do Audio EQ Cookbook).</summary>
    internal sealed class Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

        public static Biquad LowPass(int sr, double f, double q) { var b = new Biquad(); b.SetLowPass(sr, f, q); return b; }
        public static Biquad HighPass(int sr, double f, double q) => Make(sr, f, q, kind: 1);
        public static Biquad BandPass(int sr, double f, double q) => Make(sr, f, q, kind: 2);

        public void SetLowPass(int sr, double f, double q) => Set(sr, f, q, 0);

        private static Biquad Make(int sr, double f, double q, int kind) { var b = new Biquad(); b.Set(sr, f, q, kind); return b; }

        private void Set(int sr, double f, double q, int kind)
        {
            f = Math.Clamp(f, 10, sr * 0.45);
            double w = 2 * Math.PI * f / sr, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            double a0 = 1 + alpha;
            (double b0, double b1, double b2) = kind switch
            {
                0 => ((1 - cos) / 2, 1 - cos, (1 - cos) / 2),
                1 => ((1 + cos) / 2, -(1 + cos), (1 + cos) / 2),
                _ => (alpha, 0, -alpha),
            };
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
            _a1 = -2 * cos / a0; _a2 = (1 - alpha) / a0;
        }

        public double Process(double x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
    }
}
