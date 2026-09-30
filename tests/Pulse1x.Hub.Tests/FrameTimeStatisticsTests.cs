using Pulse1x.App.Services.GameHub;

namespace Pulse1x.Hub.Tests;

/// <summary>
/// A parte pura do medidor de FPS: instantes de Present (o que o ETW entrega) viram FPS por
/// segundo, FPS médio e 1% low. Não precisa de ETW nem de um jogo rodando.
/// </summary>
internal static class FrameTimeStatisticsTests
{
    public static void Register(TestSuite suite)
    {
        suite.Run("Steady 60 fps presents summarize as 60 fps", () =>
        {
            var stats = Feed(fps: 60, seconds: 10);
            var summary = stats.Summarize() ?? throw new InvalidOperationException("No summary.");

            Near(60, summary.Average, 0.01);
            Near(60, summary.OnePercentLow, 0.1);
            Near(60, summary.Min, 1);
            Near(60, summary.Max, 1);
            TestSuite.Equal(false, summary.IsEstimated);
            TestSuite.Equal(true, summary.Samples >= 9);
        });

        suite.Run("High frame rate is not capped by the refresh rate", () =>
        {
            var summary = Feed(fps: 300, seconds: 10).Summarize()!;
            Near(300, summary.Average, 0.1);
            Near(300, summary.Max, 1);
        });

        suite.Run("1% low reflects the slowest frames, not the average", () =>
        {
            // 100 fps (10 ms) com um engasgo de 50 ms a cada 50 quadros: 2% dos quadros são lentos,
            // então o pior 1% é todo de quadros de 50 ms => 20 fps, enquanto a média fica alta.
            var stats = new FrameTimeStatistics();
            double t = 0;
            stats.AddPresent(t);
            for (int i = 1; i <= 1500; i++)
            {
                t += i % 50 == 0 ? 50 : 10;
                stats.AddPresent(t);
            }

            var summary = stats.Summarize()!;
            Near(20, summary.OnePercentLow, 0.1);
            TestSuite.Equal(true, summary.Average > 80);
        });

        suite.Run("A pause (alt-tab) is not counted as a stutter", () =>
        {
            var stats = new FrameTimeStatistics();
            double t = 0;
            for (int i = 0; i < 600; i++, t += 10) stats.AddPresent(t);
            t += 5000; // cinco segundos minimizado
            for (int i = 0; i < 600; i++, t += 10) stats.AddPresent(t);

            var summary = stats.Summarize()!;
            Near(100, summary.OnePercentLow, 0.1);
            Near(100, summary.Average, 0.1);
            Near(100, summary.Min, 1);
        });

        suite.Run("Too few seconds of presents give no summary", () =>
        {
            TestSuite.Equal<FpsSummary?>(null, Feed(fps: 144, seconds: 3).Summarize());
            TestSuite.Equal<FpsSummary?>(null, new FrameTimeStatistics().Summarize());
        });

        suite.Run("Per-second frame counts follow the present rate", () =>
        {
            var stats = new FrameTimeStatistics();
            double t = 0;
            for (int i = 0; i < 30; i++, t += 1000.0 / 30) stats.AddPresent(t);
            for (int i = 0; i < 120; i++, t += 1000.0 / 120) stats.AddPresent(t);
            for (int i = 0; i < 60; i++, t += 1000.0 / 60) stats.AddPresent(t);
            stats.AddPresent(t + 5); // fecha o último segundo sem depender do arredondamento

            var perSecond = stats.PerSecondFrames;
            TestSuite.Equal(3, perSecond.Count);
            Near(30, perSecond[0], 1);
            Near(120, perSecond[1], 1);
            Near(60, perSecond[2], 1);
            Near(60, stats.CurrentFps ?? 0, 1);
        });
    }

    private static FrameTimeStatistics Feed(int fps, int seconds)
    {
        var stats = new FrameTimeStatistics();
        int frames = fps * seconds;
        for (int i = 0; i <= frames; i++) stats.AddPresent(i * 1000.0 / fps);
        return stats;
    }

    private static void Near(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected <{expected}> (+/- {tolerance}); got <{actual}>.");
    }
}
