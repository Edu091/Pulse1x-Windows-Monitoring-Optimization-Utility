using System.IO;
using Pulse1x.App.Services.Benchmark;

namespace Pulse1x.Hub.Tests;

/// <summary>Categoria Benchmark: pontuação, histórico e execuções curtas de cada teste.</summary>
internal static class BenchmarkTests
{
    public static void Register(TestSuite suite)
    {
        suite.Run("Benchmark score is 1000 at the reference and scales with throughput", () =>
        {
            TestSuite.Equal(1000, BenchmarkScoring.Score(new[] { (50.0, 50.0, true) }));
            TestSuite.Equal(2000, BenchmarkScoring.Score(new[] { (100.0, 50.0, true) }));
            // Latência: menor é melhor.
            TestSuite.Equal(2000, BenchmarkScoring.Score(new[] { (50.0, 100.0, false) }));
            // Média geométrica: 4x e 1x -> 2x.
            TestSuite.Equal(2000, BenchmarkScoring.Score(new[] { (4.0, 1.0, true), (1.0, 1.0, true) }));
        });

        suite.Run("Overall needs all four areas", () =>
        {
            var partial = new[] { Result(BenchmarkKind.CpuSingle, 1000), Result(BenchmarkKind.Gpu, 4000) };
            TestSuite.Equal<int?>(null, BenchmarkScoring.Overall(partial));
            var full = Enum.GetValues<BenchmarkKind>().Select(k => Result(k, k == BenchmarkKind.Gpu ? 16000 : 1000)).ToList();
            TestSuite.Equal<int?>(2000, BenchmarkScoring.Overall(full)); // (1000³·16000)^(1/4)
            full[0] = new BenchmarkResult { Kind = BenchmarkKind.CpuSingle, Error = "x" };
            TestSuite.Equal<int?>(null, BenchmarkScoring.Overall(full));
        });

        suite.Run("Benchmark history round-trips and keeps the newest runs", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"pulse1x-bench-{Guid.NewGuid():N}.json");
            try
            {
                var store = new BenchmarkHistoryService(path);
                for (int i = 0; i < 35; i++)
                    store.Add(new BenchmarkRun { When = new DateTime(2026, 1, 1).AddDays(i), Results = { Result(BenchmarkKind.Memory, 900 + i) } });
                var reloaded = new BenchmarkHistoryService(path);
                TestSuite.Equal(30, reloaded.Runs.Count);
                TestSuite.Equal(934, reloaded.Runs[0].Get(BenchmarkKind.Memory)!.Score);
                TestSuite.Equal("Bench_MemRead", reloaded.Runs[0].Results[0].Metrics[0].LabelKey);
                reloaded.Clear();
                TestSuite.Equal(0, new BenchmarkHistoryService(path).Runs.Count);
            }
            finally { File.Delete(path); }
        });

        suite.Run("Short CPU, memory and GPU runs produce sane numbers", () =>
        {
            var quick = TimeSpan.FromMilliseconds(150);
            var single = CpuBenchmark.RunSingle(quick, default);
            var multi = CpuBenchmark.RunMulti(quick, default);
            TestSuite.Equal(true, single.Score > 50 && multi.Score > 50);
            TestSuite.Equal(6, single.Metrics.Count);
            // Com mais de uma thread, o multi-thread rende mais que uma thread em inteiros.
            if (Environment.ProcessorCount > 1)
                TestSuite.Equal(true, multi.Metrics[0].Value > single.Metrics[0].Value * 1.3);

            var mem = MemoryBenchmark.Run(quick, 8, default);
            TestSuite.Equal(true, mem.Score > 50);
            TestSuite.Equal(true, mem.Metrics.Single(m => m.LabelKey == "Bench_MemLatency").Value is > 10 and < 1000);

            var gpu = GpuBenchmark.Run(quick, default);
            TestSuite.Equal(true, gpu.Error is not null || (gpu.Score > 10 && gpu.Device.Length > 0));
        });

        suite.Run("Benchmark cancellation stops promptly", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool canceled = false;
            try { CpuBenchmark.RunMulti(TimeSpan.FromSeconds(5), cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            TestSuite.Equal(true, canceled);
            TestSuite.Equal(true, sw.Elapsed < TimeSpan.FromSeconds(3));
        });
    }

    public static void RegisterFirmware(TestSuite suite)
    {
        suite.Run("Resizable BAR support follows GPU generation", () =>
        {
            TestSuite.Equal<bool?>(true, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("NVIDIA GeForce RTX 3050 6GB Laptop GPU"));
            TestSuite.Equal<bool?>(true, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("NVIDIA GeForce RTX 4090"));
            TestSuite.Equal<bool?>(false, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("NVIDIA GeForce RTX 2060"));
            TestSuite.Equal<bool?>(false, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("NVIDIA GeForce GTX 1660 SUPER"));
            TestSuite.Equal<bool?>(true, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("AMD Radeon RX 6600"));
            TestSuite.Equal<bool?>(true, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("AMD Radeon RX 9070 XT"));
            TestSuite.Equal<bool?>(false, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("AMD Radeon RX 580 Series"));
            TestSuite.Equal<bool?>(true, Pulse1x.App.Services.FirmwareStatusService.ReBarGpuSupport("Intel(R) Arc(TM) B580 Graphics"));
        });

        suite.Run("Firmware panel reads every item without throwing", () =>
        {
            var svc = new Pulse1x.App.Services.FirmwareStatusService(new NoSensors());
            var items = svc.ReadAsync().GetAwaiter().GetResult();
            TestSuite.Equal(14, items.Count);
            TestSuite.Equal(true, items.All(i => i.Title.Length > 0 && i.Value.Length > 0));
        });
    }

    private sealed class NoSensors : Pulse1x.App.Services.IHardwareMonitorService
    {
        public bool HasElevatedAccess => false;
        public bool IsMemoryIntegrityEnabled => false;
        public string CpuName => "CPU";
        public Pulse1x.App.Services.HardwareReading ReadCpu() => new(0, null);
        public IReadOnlyList<Pulse1x.App.Services.GpuReading> ReadGpus() => Array.Empty<Pulse1x.App.Services.GpuReading>();
    }

    private static BenchmarkResult Result(BenchmarkKind kind, int score) => new()
    {
        Kind = kind,
        Score = score,
        Metrics = { new BenchmarkMetric("Bench_MemRead", 1, "GB/s") },
    };
}
