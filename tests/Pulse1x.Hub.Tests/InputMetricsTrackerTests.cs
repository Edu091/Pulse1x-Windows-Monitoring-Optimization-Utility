using Pulse1x.App.Services;

namespace Pulse1x.Hub.Tests;

internal static class InputMetricsTrackerTests
{
    internal static void Register(TestSuite suite)
    {
        suite.Run("Input metrics: rate, interval and jitter use the event window", () =>
        {
            var tracker = new InputMetricsTracker();
            tracker.Record(0);
            tracker.Record(4);
            tracker.Record(8);
            tracker.Record(14);

            var result = tracker.Snapshot;
            TestSuite.Equal(3, result.SampleCount);
            TestSuite.Equal(214d, Math.Round(result.PollingRate));
            TestSuite.Equal(4.67d, Math.Round(result.AverageInterval, 2));
            TestSuite.Equal(6d, result.CurrentInterval);
            TestSuite.Equal(0.94d, Math.Round(result.Jitter, 2));
        });

        suite.Run("Input metrics: inactivity starts a clean burst", () =>
        {
            var tracker = new InputMetricsTracker();
            tracker.Record(0);
            tracker.Record(8);
            tracker.Record(500);
            TestSuite.Equal(0, tracker.Snapshot.SampleCount);
            tracker.Record(504);
            TestSuite.Equal(1, tracker.Snapshot.SampleCount);
            TestSuite.Equal(250d, tracker.Snapshot.PollingRate);
        });

        suite.Run("Input metrics: reset discards timestamps and samples", () =>
        {
            var tracker = new InputMetricsTracker();
            tracker.Record(0);
            tracker.Record(2);
            tracker.Reset();
            tracker.Record(100);
            TestSuite.Equal(0, tracker.Snapshot.SampleCount);
        });
    }
}
