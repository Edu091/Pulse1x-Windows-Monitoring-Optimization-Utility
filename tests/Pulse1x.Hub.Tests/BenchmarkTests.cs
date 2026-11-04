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

        suite.Run("Overall score ignores the disk", () =>
        {
            var core = BenchmarkScoring.CoreKinds.Select(k => Result(k, 1000)).ToList();
            TestSuite.Equal<int?>(1000, BenchmarkScoring.Overall(core));
            core.Add(Result(BenchmarkKind.Disk, 100)); // um HD lento não derruba o geral
            TestSuite.Equal<int?>(1000, BenchmarkScoring.Overall(core));
            TestSuite.Equal<int?>(null, BenchmarkScoring.Overall(new[] { Result(BenchmarkKind.Disk, 1000) }));
        });

        suite.Run("Disk list finds the system drive and scores against the reference", () =>
        {
            var drives = DiskBenchmark.ListDrives();
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
            var system = drives.FirstOrDefault(d => string.Equals(d.Root, systemRoot, StringComparison.OrdinalIgnoreCase));
            TestSuite.Equal(true, system is not null && system.TotalBytes > 0 && system.Display.Contains(systemRoot.TrimEnd('\\')));
            var atReference = DiskBenchmark.Result(system!, 5240, 4300, 1250, 1100, 50);
            TestSuite.Equal(1000, atReference.Score);
            TestSuite.Equal(5, atReference.Metrics.Count);
            var hdd = DiskBenchmark.Result(system!, 160, 150, 1.5, 1.5, 0.5);
            TestSuite.Equal(true, hdd.Score < 100);
        });

        suite.Run("Stress sessions return a live score every second", () =>
        {
            using (var cpu = CpuBenchmark.StartStress(multi: false))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var first = cpu.Sample(default);
                var second = cpu.Sample(default);
                TestSuite.Equal(true, first.Score > 50 && second.Score > 50);
                TestSuite.Equal(true, sw.Elapsed < TimeSpan.FromSeconds(4));
                TestSuite.Equal(BenchmarkKind.CpuSingle, second.Kind);
            }
            using (var mem = MemoryBenchmark.StartStress(8, default))
                TestSuite.Equal(true, mem.Sample(default).Score > 50);
            try
            {
                using var gpu = GpuBenchmark.StartStress(default);
                TestSuite.Equal(true, gpu.Sample(default).Score > 10);
            }
            catch (InvalidOperationException) { /* máquina sem Direct3D 11 (CI): o app mostra o erro no cartão */ }
        });

        suite.Run("Stop button stays clickable while a stress runs and stops it", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"pulse1x-bench-{Guid.NewGuid():N}.json");
            try
            {
                var vm = new Pulse1x.App.ViewModels.BenchmarkViewModel(new BenchmarkHistoryService(path), new NoSensors());
                var card = vm.Cards.First(c => c.Kind == BenchmarkKind.CpuSingle);
                var other = vm.Cards.First(c => c.Kind == BenchmarkKind.Memory);

                card.StressCommand.Execute(null);
                Pump(1500);
                TestSuite.Equal(true, card.IsStressing);
                // O que o WPF consulta para habilitar o botão: o comando E a propriedade ligada a IsEnabled.
                TestSuite.Equal(true, card.StressCommand.CanExecute(null));
                TestSuite.Equal(true, card.CanToggleStress);
                // Os outros cartões ficam bloqueados enquanto isso.
                TestSuite.Equal(false, other.CanToggleStress);

                card.StressCommand.Execute(null);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (card.IsStressing && sw.Elapsed < TimeSpan.FromSeconds(5)) Pump(50);
                TestSuite.Equal(false, card.IsStressing);
                TestSuite.Equal(false, vm.IsBusy);
                TestSuite.Equal(true, other.CanToggleStress && other.StressCommand.CanExecute(null));
            }
            finally { File.Delete(path); }
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

    private static void Pump(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
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
