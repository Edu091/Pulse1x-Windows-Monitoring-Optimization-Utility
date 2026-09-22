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

        suite.Run("Input metrics: 8 kHz intervals are preserved", () =>
        {
            var tracker = new InputMetricsTracker();
            for (int i = 0; i <= 64; i++) tracker.Record(i * 0.125);
            var result = tracker.Snapshot;
            TestSuite.Equal(64, result.SampleCount);
            TestSuite.Equal(8000d, Math.Round(result.PollingRate));
            TestSuite.Equal(0.125d, Math.Round(result.AverageInterval, 3));
            TestSuite.Equal(0d, Math.Round(result.Jitter, 6));
        });

        suite.Run("Raw input: buffered timestamps retain aggregate frequency", () =>
        {
            var timestamps = RawInputService.SpreadTimestamps(10, 11, 8);
            TestSuite.Equal(8, timestamps.Length);
            TestSuite.Equal(10.125d, timestamps[0]);
            TestSuite.Equal(11d, timestamps[^1]);
            var tracker = new InputMetricsTracker();
            tracker.Record(10);
            foreach (double timestamp in timestamps) tracker.Record(timestamp);
            TestSuite.Equal(8000d, Math.Round(tracker.Snapshot.PollingRate));
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
