using Pulse1x.App.Services.GameHub;

namespace Pulse1x.Hub.Tests;

internal static class GameActivationGestureTests
{
    private const int Interval = 500;

    public static void Register(TestSuite suite)
    {
        foreach (var zone in new[] { HubZone.Grid, HubZone.Highlight })
        {
            suite.Run($"Gesture: single press only arms ({zone})", () =>
            {
                var gesture = new GameActivationGesture();
                TestSuite.Equal(false, gesture.Press("game-a", zone, 0, Interval));
            });

            suite.Run($"Gesture: same card activates on second press ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 1100, true)));

            suite.Run($"Gesture: exact timeout boundary activates ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 1500, true)));

            suite.Run($"Gesture: expired press rearms the interval ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 1501, false), ("game-a", 1900, true)));

            suite.Run($"Gesture: different card starts its own pair ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-b", 1100, false), ("game-b", 1200, true)));

            suite.Run($"Gesture: returning to previous card needs a fresh pair ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-b", 1100, false),
                    ("game-a", 1200, false), ("game-a", 1300, true)));

            suite.Run($"Gesture: triple press activates only once ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 1100, true), ("game-a", 1200, false)));

            suite.Run($"Gesture: four presses form two independent pairs ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 1100, true),
                    ("game-a", 1200, false), ("game-a", 1300, true)));

            suite.Run($"Gesture: reset cancels an armed press ({zone})", () =>
            {
                var gesture = new GameActivationGesture();
                gesture.Reset();
                TestSuite.Equal(false, gesture.Press("game-a", zone, 1000, Interval));
                gesture.Reset();
                gesture.Reset();
                TestSuite.Equal(false, gesture.Press("game-a", zone, 1100, Interval));
                TestSuite.Equal(true, gesture.Press("game-a", zone, 1200, Interval));
            });

            suite.Run($"Gesture: a backward timestamp cannot activate ({zone})", () =>
                Check(zone, ("game-a", 1000, false), ("game-a", 900, false), ("game-a", 950, true)));

            suite.Run($"Gesture: timestamps beyond Int32 remain valid ({zone})", () =>
                Check(zone, ("game-a", (long)int.MaxValue + 1000, false),
                    ("game-a", (long)int.MaxValue + 1100, true)));
        }

        suite.Run("Gesture: the same card in a different zone requires two presses", () =>
        {
            var gesture = new GameActivationGesture();
            TestSuite.Equal(false, gesture.Press("game-a", HubZone.Grid, 1000, Interval));
            TestSuite.Equal(false, gesture.Press("game-a", HubZone.Highlight, 1100, Interval));
            TestSuite.Equal(true, gesture.Press("game-a", HubZone.Highlight, 1200, Interval));
        });

        suite.Run("Gesture: switching away and back does not preserve the original pair", () =>
        {
            var gesture = new GameActivationGesture();
            TestSuite.Equal(false, gesture.Press("game-a", HubZone.Highlight, 1000, Interval));
            TestSuite.Equal(false, gesture.Press("game-a", HubZone.Grid, 1100, Interval));
            TestSuite.Equal(false, gesture.Press("game-a", HubZone.Highlight, 1200, Interval));
            TestSuite.Equal(true, gesture.Press("game-a", HubZone.Highlight, 1300, Interval));
        });
    }

    private static void Check(HubZone zone, params (string Id, long At, bool Activates)[] presses)
    {
        var gesture = new GameActivationGesture();
        foreach (var press in presses)
            TestSuite.Equal(press.Activates, gesture.Press(press.Id, zone, press.At, Interval));
    }
}
