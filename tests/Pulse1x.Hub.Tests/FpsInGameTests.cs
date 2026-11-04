using Pulse1x.App.Services.FpsInGame;

namespace Pulse1x.Hub.Tests;

/// <summary>
/// Categoria Fps-In-Game: a base embutida carrega, as GPUs que o Windows informa são reconhecidas e
/// a interpolação devolve os valores publicados nas GPUs de referência.
/// </summary>
internal static class FpsInGameTests
{
    public static void Register(TestSuite suite)
    {
        var db = FpsDatabase.Load();

        suite.Run("FPS database loads thousands of games and GPUs", () =>
        {
            TestSuite.Equal(true, db.Games.Count > 15000);
            TestSuite.Equal(true, db.Gpus.Count > 1000);
            TestSuite.Equal(12, db.ReferenceScores.Count);
            TestSuite.Equal(true, db.Games.Any(g => g.Name == "Cyberpunk 2077"));
        });

        suite.Run("GPU names reported by Windows match the exact catalog model", () =>
        {
            void Expect(string wmi, string expected, string? cpu = null, bool laptop = false)
            {
                var m = GpuMatcher.Match(db.Gpus, wmi, cpu, laptop);
                if (m?.Gpu.Name != expected || m.IsApproximate)
                    throw new InvalidOperationException($"{wmi}: expected <{expected}>, got <{m?.Gpu.Name}> approx={m?.IsApproximate}");
            }

            // Desktop NVIDIA / AMD / Intel
            Expect("NVIDIA GeForce RTX 3060", "GeForce RTX 3060");
            Expect("NVIDIA GeForce RTX 3060 Ti", "GeForce RTX 3060 Ti");
            Expect("NVIDIA GeForce RTX 4060 Ti", "GeForce RTX 4060 Ti");
            Expect("NVIDIA GeForce RTX 4070 SUPER", "GeForce RTX 4070 SUPER");
            Expect("NVIDIA GeForce RTX 4090", "GeForce RTX 4090");
            Expect("NVIDIA GeForce GTX 1660 SUPER", "GeForce GTX 1660 SUPER");
            Expect("NVIDIA GeForce GTX 1050 Ti", "GeForce GTX 1050 Ti");
            Expect("NVIDIA GeForce GTX 970", "GeForce GTX 970");
            Expect("AMD Radeon RX 6600 XT", "Radeon RX 6600 XT");
            Expect("AMD Radeon RX 6600", "Radeon RX 6600");
            Expect("AMD Radeon RX 7900 XTX", "Radeon RX 7900 XTX");
            Expect("Radeon RX 580 Series", "Radeon RX 580");
            Expect("AMD Radeon RX 580 2048SP", "Radeon RX 580 2048SP");
            Expect("AMD Radeon RX 5700 XT", "Radeon RX 5700 XT");
            Expect("Intel(R) Arc(TM) A770 Graphics", "Intel Arc A770");
            Expect("Intel(R) Arc(TM) B580 Graphics", "Intel Arc B580");
            // Notebooks
            Expect("NVIDIA GeForce RTX 3050 6GB Laptop GPU", "GeForce RTX 3050 6GB Laptop GPU");
            Expect("NVIDIA GeForce RTX 3060 Laptop GPU", "GeForce RTX 3060 Laptop GPU");
            Expect("NVIDIA GeForce RTX 4060 Laptop GPU", "GeForce RTX 4060 Laptop GPU");
            // Driver antigo sem "Laptop" no nome: num notebook vale a versão mobile.
            Expect("NVIDIA GeForce RTX 3060", "GeForce RTX 3060 Laptop GPU", laptop: true);
            Expect("NVIDIA GeForce RTX 2060", "GeForce RTX 2060 (Mobile)", laptop: true);
            // Integradas
            Expect("Intel(R) Iris(R) Xe Graphics", "Intel Iris Xe");
            Expect("Intel(R) UHD Graphics 770", "Intel UHD Graphics 770");
            Expect("Intel(R) UHD Graphics 620", "Intel UHD Graphics 620");
            Expect("Intel(R) HD Graphics 620", "Intel HD Graphics 620");
            Expect("Intel(R) HD Graphics 4000", "Intel HD 4000");
            Expect("Intel(R) HD Graphics 530", "Intel HD 530");
            Expect("AMD Radeon 780M Graphics", "Radeon 780M");
            Expect("AMD Radeon(TM) 890M Graphics", "Radeon 890M");
            Expect("AMD Radeon(TM) Graphics", "Ryzen 7 6800HS with Radeon Graphics", "AMD Ryzen 7 6800HS with Radeon Graphics");
        });

        suite.Run("Unknown or virtual adapters never borrow another vendor's score", () =>
        {
            TestSuite.Equal<GpuMatch?>(null, GpuMatcher.Match(db.Gpus, "Microsoft Basic Display Adapter"));
            TestSuite.Equal<GpuMatch?>(null, GpuMatcher.Match(db.Gpus, "Parsec Virtual Display Adapter"));
            TestSuite.Equal<GpuMatch?>(null, GpuMatcher.Match(db.Gpus, "SMI USB Display"));
            // Intel com número que não existe na base: aproximação pela Intel vizinha, nunca uma GeForce/Radeon.
            var hd540 = GpuMatcher.Match(db.Gpus, "Intel(R) HD Graphics 540");
            TestSuite.Equal(true, hd540 is not null && hd540.IsApproximate && hd540.Gpu.Name.StartsWith("Intel"));
            // O catálogo não lista lixo de submissões (telas USB, nomes corrompidos).
            TestSuite.Equal(false, db.Gpus.Any(g => g.Name is "SMI USB Display" or "Sharing Monitor" || g.Name.Contains('#')));
        });

        suite.Run("Hybrid PCs use the strongest recognized adapter", () =>
        {
            var hybrid = GpuMatcher.MatchBest(db.Gpus,
                new[] { "Intel(R) UHD Graphics", "Parsec Virtual Display Adapter", "NVIDIA GeForce RTX 4060 Laptop GPU" },
                "Intel(R) Core(TM) i7-13650HX", isLaptop: true);
            TestSuite.Equal("GeForce RTX 4060 Laptop GPU", hybrid?.Gpu.Name);

            // USB/virtual com score alto no PassMark nunca vence a GPU real.
            var withDock = GpuMatcher.MatchBest(db.Gpus,
                new[] { "SMI USB Display", "Intel(R) Iris(R) Xe Graphics" }, null, isLaptop: true);
            TestSuite.Equal("Intel Iris Xe", withDock?.Gpu.Name);

            // AMD APU sem o processador exato no catálogo: irmãos da mesma geração, marcado aproximado.
            var apu = GpuMatcher.MatchBest(db.Gpus, new[] { "AMD Radeon(TM) Graphics" }, "AMD Ryzen 7 5825U with Radeon Graphics", true);
            TestSuite.Equal(true, apu is not null && apu.Gpu.Name.Contains("Ryzen") && apu.Gpu.G3d < 9000);
        });

        suite.Run("VRAM requirement is read from the GPU requirement text", () =>
        {
            TestSuite.Equal<double?>(6, FpsEstimator.RequiredVramGb("GeForce GTX 1060 6GB or Radeon RX 580 8GB or Arc A380"));
            TestSuite.Equal<double?>(1, FpsEstimator.RequiredVramGb("NVIDIA 9800 GT 1GB / AMD HD 4870 1GB (DX 10, 10.1, 11)"));
            TestSuite.Equal<double?>(0.5, FpsEstimator.RequiredVramGb("512 MB video card"));
            TestSuite.Equal<double?>(null, FpsEstimator.RequiredVramGb("GeForce GTX 660 / Radeon HD 7870"));
        });

        suite.Run("Recommended preset holds 60 FPS when possible, else falls back to Low", () =>
        {
            var game = db.Games.First(g => g.Name == "Cyberpunk 2077");
            var (strongPreset, strong) = FpsEstimator.Recommend(game, db.ReferenceScores, 38000, 32, TargetResolution.R1080p);
            TestSuite.Equal(GraphicsPreset.Ultra, strongPreset);
            TestSuite.Equal(true, strong.Fps >= 60);
            var (weakPreset, weak) = FpsEstimator.Recommend(game, db.ReferenceScores, 2500, 16, TargetResolution.R1080p);
            TestSuite.Equal(GraphicsPreset.Low, weakPreset);
            TestSuite.Equal(true, weak.Fps < 60);
        });

        suite.Run("Estimate at a reference GPU reproduces the published FPS", () =>
        {
            var game = db.Games.First(g => g.Name == "The Witcher 3: Wild Hunt");
            for (int i = 0; i < db.ReferenceScores.Count; i++)
            {
                var e = FpsEstimator.Estimate(game, db.ReferenceScores, db.ReferenceScores[i], 16,
                    GraphicsPreset.High, TargetResolution.R1080p);
                double expected = Math.Min(game.ReferenceFps[i], FpsEstimator.GlobalCap);
                if (Math.Abs(e.Fps - expected) > 0.5)
                    throw new InvalidOperationException($"ref {i}: expected {expected}, got {e.Fps}");
            }
        });

        suite.Run("Faster GPU, lower preset and lower resolution never lower the FPS", () =>
        {
            foreach (var game in db.Games.Where(g => g.Popularity is < 200))
            {
                double prev = 0;
                foreach (int score in new[] { 800, 2000, 5000, 9000, 15000, 22000, 30000, 45000 })
                {
                    var fps = FpsEstimator.Estimate(game, db.ReferenceScores, score, 32, GraphicsPreset.High, TargetResolution.R1080p).Fps;
                    if (fps + 0.01 < prev * 0.97) // pontos de referência quase empatados podem oscilar pouco
                        throw new InvalidOperationException($"{game.Name}: {fps} < {prev} at {score}");
                    prev = fps;
                }

                var low = FpsEstimator.Estimate(game, db.ReferenceScores, 12000, 32, GraphicsPreset.Low, TargetResolution.R1080p).Fps;
                var ultra = FpsEstimator.Estimate(game, db.ReferenceScores, 12000, 32, GraphicsPreset.Ultra, TargetResolution.R1080p).Fps;
                var fourK = FpsEstimator.Estimate(game, db.ReferenceScores, 12000, 32, GraphicsPreset.Ultra, TargetResolution.R4K).Fps;
                TestSuite.Equal(true, low >= ultra && ultra >= fourK);
            }
        });

        suite.Run("Engine-capped game still drops below the cap at 4K", () =>
        {
            var capped = db.Games.FirstOrDefault(g => g.FpsCap == 60 && g.ReferenceFps.Count(f => f >= 60) >= 6);
            if (capped is null) return;
            var at1080 = FpsEstimator.Estimate(capped, db.ReferenceScores, 30000, 32, GraphicsPreset.High, TargetResolution.R1080p);
            TestSuite.Equal(true, at1080.HitCap);
            TestSuite.Equal(60, at1080.RangeHigh);
        });

        suite.Run("Below-minimum hardware is flagged", () =>
        {
            var game = db.Games.First(g => g.Name == "Cyberpunk 2077");
            var e = FpsEstimator.Estimate(game, db.ReferenceScores, 900, 4, GraphicsPreset.High, TargetResolution.R1080p);
            TestSuite.Equal(true, e.BelowMinGpu);
            TestSuite.Equal(RunVerdict.CannotRun, e.Verdict);
        });
    }
}
