using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Benchmark de memória RAM: largura de banda de leitura, escrita e cópia (todas as threads
/// juntas, em blocos bem maiores que o cache do processador) e latência (saltos aleatórios
/// dependentes, que nenhum pré-carregamento consegue adivinhar). Banda pesa em carregamento e
/// streaming de mundo aberto; latência, em quedas de FPS mínimo.
/// </summary>
public static class MemoryBenchmark
{
    // Máquina de referência: Core i5-13420H com 2x8 GB DDR5-5200.
    private const double RefReadGBs = 44.9, RefWriteGBs = 28.75, RefCopyGBs = 21.9, RefLatencyNs = 136.5;

    public static BenchmarkResult Run(TimeSpan perTest, double? totalRamGb, CancellationToken ct, IProgress<double>? progress = null)
    {
        using var session = new Session(totalRamGb, ct);
        progress?.Report(0.1);
        return session.Measure(perTest, ct, progress);
    }

    public static IStressSession StartStress(double? totalRamGb, CancellationToken ct) => new Stress(totalRamGb, ct);

    private sealed class Stress : IStressSession
    {
        private readonly Session _session;
        public Stress(double? totalRamGb, CancellationToken ct) => _session = new Session(totalRamGb, ct);
        // Quatro medidas de 250 ms = uma amostra por segundo.
        public BenchmarkResult Sample(CancellationToken ct) => _session.Measure(TimeSpan.FromMilliseconds(250), ct, null);
        public void Dispose() => _session.Dispose();
    }

    /// <summary>Buffers alocados uma vez (1 GB de banda + 256 MB de latência) e reaproveitados.</summary>
    private sealed class Session : IDisposable
    {
        private long[]? _buffer;
        private int[]? _chain;
        private readonly long _bufferBytes;
        private readonly int _threads = Math.Max(1, Environment.ProcessorCount);
        private int _position;

        public Session(double? totalRamGb, CancellationToken ct)
        {
            // Bem acima de qualquer cache L3 (até 128 MB nos X3D), sem pesar em PCs com pouca RAM.
            _bufferBytes = (totalRamGb ?? 8) >= 12 ? 1L << 30 : 512L << 20;
            var buffer = GC.AllocateUninitializedArray<long>((int)(_bufferBytes / sizeof(long)));
            Partitioned(buffer.Length, _threads, (from, to) => buffer.AsSpan(from, to - from).Fill(1)); // aloca as páginas de verdade
            _buffer = buffer;
            ct.ThrowIfCancellationRequested();
            _chain = BuildChain();
        }

        public BenchmarkResult Measure(TimeSpan perTest, CancellationToken ct, IProgress<double>? progress)
        {
            var buffer = _buffer!;
            long bytes = _bufferBytes;
            double read = Bandwidth(perTest, ct, () => { Partitioned(buffer.Length, _threads, (f, t) => Sum(buffer, f, t)); return bytes; });
            progress?.Report(0.3);
            double write = Bandwidth(perTest, ct, () => { Partitioned(buffer.Length, _threads, (f, t) => buffer.AsSpan(f, t - f).Fill(7)); return bytes; });
            progress?.Report(0.5);
            int half = buffer.Length / 2;
            double copy = Bandwidth(perTest, ct, () =>
            {
                Partitioned(half, _threads, (f, t) => buffer.AsSpan(f, t - f).CopyTo(buffer.AsSpan(half + f, t - f)));
                return (long)half * sizeof(long);
            });
            progress?.Report(0.75);
            double latency = Latency(perTest, ct);
            progress?.Report(1);

            return new BenchmarkResult
            {
                Kind = BenchmarkKind.Memory,
                Score = BenchmarkScoring.Score(new[]
                {
                    (read, RefReadGBs, true), (write, RefWriteGBs, true), (copy, RefCopyGBs, true), (latency, RefLatencyNs, false),
                }),
                Metrics = new()
                {
                    new("Bench_MemRead", read, "GB/s"),
                    new("Bench_MemWrite", write, "GB/s"),
                    new("Bench_MemCopy", copy, "GB/s"),
                    new("Bench_MemLatency", latency, "ns"),
                },
            };
        }

        /// <summary>
        /// Latência: um ciclo aleatório único sobre 256 MB, percorrido salto a salto — cada leitura
        /// depende da anterior, então mede o tempo real de ida à RAM.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private double Latency(TimeSpan duration, CancellationToken ct)
        {
            var next = _chain!;
            int p = _position;
            for (int i = 0; i < 200_000; i++) p = next[p]; // aquecimento
            long hops = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                for (int i = 0; i < 1_000_000; i++) p = next[p];
                hops += 1_000_000;
                if (ct.IsCancellationRequested) break;
            }
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / hops;
            _position = p;
            ct.ThrowIfCancellationRequested();
            return ns;
        }

        private static int[] BuildChain()
        {
            // Saltos de 64 bytes: um índice a cada linha de cache, para cada salto ser uma linha nova.
            const int stride = 16; // ints
            int lines = (256 << 20) / (stride * sizeof(int));
            var next = new int[lines * stride];
            var order = new int[lines];
            for (int i = 0; i < lines; i++) order[i] = i;
            var rng = new Random(7);
            for (int i = lines - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            // Ligar a ordem embaralhada em sequência forma um ciclo único que passa por todas as linhas.
            for (int i = 0; i < lines; i++) next[order[i] * stride] = order[(i + 1) % lines] * stride;
            return next;
        }

        public void Dispose()
        {
            _buffer = null;
            _chain = null;
            GC.Collect(); // devolve os 1,25 GB ao Windows na hora
        }
    }

    /// <summary>Repete a passada até completar o tempo e devolve GB/s.</summary>
    private static double Bandwidth(TimeSpan duration, CancellationToken ct, Func<long> pass)
    {
        pass(); // aquecimento
        long bytes = 0;
        var sw = Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            bytes += pass();
        }
        while (sw.Elapsed < duration);
        return bytes / sw.Elapsed.TotalSeconds / 1e9;
    }

    private static void Partitioned(int length, int threads, Action<int, int> body)
    {
        int chunk = (length + threads - 1) / threads;
        // Alinha cada fatia em 64 bytes (uma linha de cache) para as threads não disputarem linhas.
        chunk = (chunk + 7) & ~7;
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            int from = t * chunk;
            int to = Math.Min(length, from + chunk);
            if (from < to) body(from, to);
        });
    }

    private static long _sink;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static void Sum(long[] data, int from, int to)
    {
        var span = data.AsSpan(from, to - from);
        var acc = Vector<long>.Zero;
        int w = Vector<long>.Count, i = 0;
        for (; i <= span.Length - w; i += w) acc += new Vector<long>(span.Slice(i, w));
        long total = Vector.Sum(acc);
        for (; i < span.Length; i++) total += span[i];
        Interlocked.Add(ref _sink, total); // impede que o JIT descarte a leitura
    }
}
