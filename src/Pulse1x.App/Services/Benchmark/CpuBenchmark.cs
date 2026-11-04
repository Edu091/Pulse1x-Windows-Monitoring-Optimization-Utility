using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Benchmark de processador. Cinco cargas de trabalho que representam o que jogos e programas
/// fazem — inteiros (lógica, hashes), ponto flutuante (física), SIMD (vetores, áudio, IA),
/// ordenação (dados com desvios imprevisíveis) e compressão (carregamento de assets) — cada uma
/// rodando por um tempo fixo. Single-thread usa uma thread; multi-thread, uma por processador
/// lógico, ao mesmo tempo. A pontuação compara a vazão com a da máquina de referência.
/// </summary>
public static class CpuBenchmark
{
    /// <summary>Uma carga de trabalho: cria o estado por thread e executa um pedaço curto, devolvendo o trabalho feito.</summary>
    private sealed record Workload(string LabelKey, string Unit, double UnitDivisor, Func<object> CreateState, Func<object, long> RunChunk,
        double ReferenceSingle, double ReferenceMulti);

    // Vazão da máquina de referência (Core i5-13420H, 8 núcleos / 12 threads, no carregador, com a
    // janela do Pulse1x em primeiro plano), na unidade exibida. Uma CPU igual pontua 1000; o dobro
    // da vazão, 2000.
    private static readonly Workload[] Workloads =
    {
        new("Bench_CpuInteger", "M op/s", 1e6, () => new IntegerState(), s => IntegerChunk((IntegerState)s), 1484, 12121),
        new("Bench_CpuFloat", "M it/s", 1e6, () => new object(), _ => MandelbrotChunk(), 395.4, 3371),
        new("Bench_CpuSimd", "GFLOPS", 1e9, () => new SimdState(), s => SimdChunk((SimdState)s), 16.35, 64.5),
        new("Bench_CpuSort", "M elem/s", 1e6, () => new SortState(), s => SortChunk((SortState)s), 18.4, 134.6),
        new("Bench_CpuCompress", "MB/s", 1024 * 1024, () => new CompressState(), s => CompressChunk((CompressState)s), 35.2, 211.3),
    };

    public static BenchmarkResult RunSingle(TimeSpan perWorkload, CancellationToken ct, IProgress<double>? progress = null) =>
        Run(BenchmarkKind.CpuSingle, 1, perWorkload, ct, progress);

    public static BenchmarkResult RunMulti(TimeSpan perWorkload, CancellationToken ct, IProgress<double>? progress = null) =>
        Run(BenchmarkKind.CpuMulti, Environment.ProcessorCount, perWorkload, ct, progress);

    private static BenchmarkResult Run(BenchmarkKind kind, int threads, TimeSpan perWorkload, CancellationToken ct, IProgress<double>? progress)
    {
        var metrics = new List<BenchmarkMetric>();
        var parts = new List<(double, double, bool)>();
        for (int i = 0; i < Workloads.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var w = Workloads[i];
            double perSecond = Measure(w, threads, perWorkload, ct) / w.UnitDivisor;
            metrics.Add(new BenchmarkMetric(w.LabelKey, perSecond, w.Unit));
            parts.Add((perSecond, kind == BenchmarkKind.CpuSingle ? w.ReferenceSingle : w.ReferenceMulti, true));
            progress?.Report((i + 1.0) / Workloads.Length);
        }
        metrics.Add(new BenchmarkMetric("Bench_Threads", threads, "", 0));
        return new BenchmarkResult { Kind = kind, Score = BenchmarkScoring.Score(parts), Metrics = metrics };
    }

    /// <summary>Executa a carga em <paramref name="threads"/> threads pelo tempo dado e devolve o trabalho por segundo.</summary>
    private static double Measure(Workload w, int threads, TimeSpan duration, CancellationToken ct)
    {
        var totals = new long[threads];
        var elapsed = new double[threads];
        using var start = new ManualResetEventSlim(false);
        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int index = t;
            workers[t] = new Thread(() =>
            {
                PowerThrottlingScope.OptOutCurrentThread();
                var state = w.CreateState();
                w.RunChunk(state); // aquecimento: caches, páginas de memória e código já otimizado
                start.Wait();
                var sw = Stopwatch.StartNew();
                long done = 0;
                while (sw.Elapsed < duration && !ct.IsCancellationRequested)
                    done += w.RunChunk(state);
                elapsed[index] = sw.Elapsed.TotalSeconds;
                totals[index] = done;
            })
            {
                IsBackground = true,
                // Acima do normal para o teste não oscilar com tarefas do Windows em segundo plano.
                // Nunca abaixo do normal: no Windows 11 com CPU híbrida, threads de prioridade baixa
                // vão para os núcleos de eficiência e o multi-thread media menos da metade.
                Priority = ThreadPriority.AboveNormal,
                Name = "Pulse1x CPU benchmark",
            };
            workers[t].Start();
        }
        Thread.Sleep(50);
        start.Set();
        foreach (var worker in workers) worker.Join();
        ct.ThrowIfCancellationRequested();

        double sum = 0;
        for (int t = 0; t < threads; t++)
            if (elapsed[t] > 0) sum += totals[t] / elapsed[t];
        return sum;
    }

    /// <summary>Inicia o stress contínuo da CPU (uma thread ou todas).</summary>
    public static IStressSession StartStress(bool multi) => new CpuStress(multi ? Environment.ProcessorCount : 1);

    /// <summary>
    /// Stress contínuo: as threads ficam rodando as cinco cargas em rodízio, trocando todas juntas
    /// a cada 200 ms (pelo mesmo relógio), como no benchmark. Cada janela mede a vazão de cada carga
    /// pelo trabalho feito ÷ tempo gasto nela e devolve a pontuação na mesma escala do benchmark.
    /// </summary>
    private sealed class CpuStress : IStressSession
    {
        private const int SliceMs = 200;
        private readonly int _threads;
        private readonly Thread[] _workers;
        private readonly long[] _ops = new long[Workloads.Length];
        private readonly long[] _ticks = new long[Workloads.Length];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private volatile bool _stop;
        private long[] _lastOps = new long[Workloads.Length];
        private long[] _lastTicks = new long[Workloads.Length];

        public CpuStress(int threads)
        {
            _threads = threads;
            _workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                _workers[t] = new Thread(Loop)
                {
                    IsBackground = true,
                    // Acima do normal, como no benchmark: com prioridade normal ou menor, o Windows 11
                    // em CPUs híbridas manda a carga para os núcleos de eficiência assim que a janela
                    // perde o foco (o usuário abriu outro programa para ver temperaturas) e a
                    // pontuação caía pela metade. Para a tela não congelar, cada thread cede 1 ms a
                    // cada ~30 ms de trabalho (ver Loop).
                    Priority = ThreadPriority.AboveNormal,
                    Name = "Pulse1x CPU stress",
                };
                _workers[t].Start();
            }
        }

        private void Loop()
        {
            PowerThrottlingScope.OptOutCurrentThread();
            var states = Workloads.Select(w => w.CreateState()).ToArray();
            for (int i = 0; i < Workloads.Length; i++) Workloads[i].RunChunk(states[i]); // aquecimento
            long yieldEvery = Stopwatch.Frequency * 30 / 1000;
            // Cada thread cede em um momento diferente, para sempre sobrar um núcleo livre por um
            // instante para a interface, o desenho da tela e os sensores.
            long nextYield = Stopwatch.GetTimestamp() + Random.Shared.NextInt64(yieldEvery);
            while (!_stop)
            {
                int w = (int)(_clock.ElapsedMilliseconds / SliceMs % Workloads.Length);
                long start = Stopwatch.GetTimestamp();
                long done = Workloads[w].RunChunk(states[w]);
                long end = Stopwatch.GetTimestamp();
                Interlocked.Add(ref _ops[w], done);
                Interlocked.Add(ref _ticks[w], end - start); // a pausa fica fora do tempo medido
                if (end >= nextYield)
                {
                    Thread.Sleep(1);
                    nextYield = Stopwatch.GetTimestamp() + yieldEvery;
                }
            }
        }

        public BenchmarkResult Sample(CancellationToken ct)
        {
            // Janela de ~1 s = todas as cinco cargas passam uma vez.
            ct.WaitHandle.WaitOne(SliceMs * Workloads.Length);
            ct.ThrowIfCancellationRequested();

            var ops = _ops.Select(o => Interlocked.Read(ref o)).ToArray();
            var ticks = _ticks.Select(t => Interlocked.Read(ref t)).ToArray();
            bool multi = _threads > 1;
            var metrics = new List<BenchmarkMetric>();
            var parts = new List<(double, double, bool)>();
            for (int i = 0; i < Workloads.Length; i++)
            {
                long dOps = ops[i] - _lastOps[i];
                double dSeconds = (ticks[i] - _lastTicks[i]) / (double)Stopwatch.Frequency;
                if (dOps <= 0 || dSeconds <= 0) continue;
                // Vazão por thread × threads = vazão do processador inteiro.
                double perSecond = dOps / dSeconds * _threads / Workloads[i].UnitDivisor;
                metrics.Add(new BenchmarkMetric(Workloads[i].LabelKey, perSecond, Workloads[i].Unit));
                parts.Add((perSecond, multi ? Workloads[i].ReferenceMulti : Workloads[i].ReferenceSingle, true));
            }
            _lastOps = ops;
            _lastTicks = ticks;
            return new BenchmarkResult
            {
                Kind = multi ? BenchmarkKind.CpuMulti : BenchmarkKind.CpuSingle,
                Score = BenchmarkScoring.Score(parts),
                Metrics = metrics,
            };
        }

        public void Dispose()
        {
            _stop = true;
            foreach (var w in _workers) w.Join(2000);
        }
    }

    // ------------------------------------------------------------------ inteiros

    private sealed class IntegerState { public ulong A = 0x9E3779B97F4A7C15, B = 0xBF58476D1CE4E5B9, C = 0x94D049BB133111EB, D = 0x2545F4914F6CDD1D; }

    /// <summary>Mistura de bits (estilo splitmix/xorshift) em quatro cadeias independentes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static long IntegerChunk(IntegerState s)
    {
        const int n = 1 << 20;
        ulong a = s.A, b = s.B, c = s.C, d = s.D;
        for (int i = 0; i < n; i++)
        {
            a ^= a << 13; a ^= a >> 7; a ^= a << 17; a *= 0xFF51AFD7ED558CCD;
            b += 0x9E3779B97F4A7C15; b = (b ^ (b >> 30)) * 0xBF58476D1CE4E5B9;
            c = BitOperations.RotateLeft(c ^ a, 21) + (ulong)i;
            d = (d ^ (d >> 27)) * 0x94D049BB133111EB + (c >> 3);
        }
        s.A = a; s.B = b; s.C = c; s.D = d;
        return n * 4L;
    }

    // ------------------------------------------------------------------ ponto flutuante

    /// <summary>Mandelbrot em precisão dupla: multiplicações e somas dependentes, como física e geometria.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static long MandelbrotChunk()
    {
        const int size = 96, maxIter = 256;
        long iterations = 0;
        for (int py = 0; py < size; py++)
        {
            double ci = -1.2 + 2.4 * py / size;
            for (int px = 0; px < size; px++)
            {
                double cr = -2.1 + 3.0 * px / size;
                double zr = 0, zi = 0;
                int it = 0;
                while (it < maxIter && zr * zr + zi * zi <= 4.0)
                {
                    double t = zr * zr - zi * zi + cr;
                    zi = 2 * zr * zi + ci;
                    zr = t;
                    it++;
                }
                iterations += it;
            }
        }
        return iterations;
    }

    // ------------------------------------------------------------------ SIMD

    private sealed class SimdState
    {
        public readonly float[] A = Enumerable.Range(0, 4096).Select(i => 1f + i * 1e-5f).ToArray();
        public readonly float[] B = Enumerable.Range(0, 4096).Select(i => 0.999f - i * 1e-7f).ToArray();
    }

    /// <summary>Multiplica-e-soma em vetores (AVX/SSE quando disponível) sobre dados que cabem no cache L1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static long SimdChunk(SimdState s)
    {
        const int passes = 256;
        var a = s.A; var b = s.B;
        int width = Vector<float>.Count;
        var add = new Vector<float>(0.001f);
        for (int p = 0; p < passes; p++)
        {
            for (int i = 0; i <= a.Length - width; i += width)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                (va * vb + add).CopyTo(a, i);
            }
        }
        return (long)passes * a.Length * 2; // uma multiplicação e uma soma por elemento
    }

    // ------------------------------------------------------------------ ordenação

    private sealed class SortState
    {
        public readonly int[] Template;
        public readonly int[] Work;
        public SortState()
        {
            var rng = new Random(1234);
            Template = Enumerable.Range(0, 65536).Select(_ => rng.Next()).ToArray();
            Work = new int[Template.Length];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static long SortChunk(SortState s)
    {
        s.Template.CopyTo(s.Work, 0);
        Array.Sort(s.Work);
        return s.Work.Length;
    }

    // ------------------------------------------------------------------ compressão

    private sealed class CompressState
    {
        public readonly byte[] Input;
        public readonly MemoryStream Output = new(1 << 20);
        public CompressState()
        {
            // Texto com repetições parciais, parecido com arquivos de configuração e scripts.
            var rng = new Random(42);
            string[] words = { "player", "texture", "shader", "level", "health", "ammo", "vertex", "mesh", "audio", "frame", "0x1F", "true", "false", "{", "}", "=", ";" };
            var sb = new System.Text.StringBuilder(1 << 19);
            while (sb.Length < 1 << 19) sb.Append(words[rng.Next(words.Length)]).Append(rng.Next(3) == 0 ? '\n' : ' ').Append(rng.Next(1000));
            Input = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        }
    }

    private static long CompressChunk(CompressState s)
    {
        s.Output.SetLength(0);
        using (var deflate = new DeflateStream(s.Output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(s.Input, 0, s.Input.Length);
        return s.Input.Length;
    }
}
