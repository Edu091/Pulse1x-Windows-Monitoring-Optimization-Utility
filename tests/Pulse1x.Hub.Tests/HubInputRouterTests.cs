using Pulse1x.App.Services.GameHub;

namespace Pulse1x.Hub.Tests;

internal static class HubInputRouterTests
{
    private static readonly HubZone[] NonModalZones =
        { HubZone.Grid, HubZone.Chrome, HubZone.Hero, HubZone.Highlight };

    public static void Register(TestSuite suite)
    {
        suite.Run("Router: starts in the nonmodal grid", () =>
        {
            var router = new HubInputRouter();
            TestSuite.Equal(HubZone.Grid, router.Zone);
            TestSuite.Equal(false, router.IsModal);
        });

        suite.Run("Router: SetZone notifies once per actual transition", () =>
        {
            var router = new HubInputRouter();
            var changes = Observe(router);
            router.SetZone(HubZone.Grid);
            router.SetZone(HubZone.Hero);
            router.SetZone(HubZone.Hero);
            router.SetZone(HubZone.Highlight);
            TestSuite.SequenceEqual(new[] { HubZone.Hero, HubZone.Highlight }, changes);
        });

        suite.Run("Router: TrackFocus silently follows mouse and keyboard focus", () =>
        {
            var router = new HubInputRouter();
            var changes = Observe(router);
            foreach (var zone in NonModalZones)
            {
                router.TrackFocus(zone);
                router.TrackFocus(zone);
                TestSuite.Equal(zone, router.Zone);
                TestSuite.Equal(false, router.IsModal);
            }
            TestSuite.Equal(0, changes.Count);
            router.SetZone(HubZone.Grid);
            TestSuite.SequenceEqual(new[] { HubZone.Grid }, changes);
        });

        suite.Run("Router: navigation uses the silently tracked zone without moving it", () =>
        {
            var router = new HubInputRouter();
            var changes = Observe(router);
            router.TrackFocus(HubZone.Highlight);
            TestSuite.Equal<HubZone?>(HubZone.Hero,
                router.ResolveVerticalExit(GamepadDirection.Up, false, true, true));
            TestSuite.Equal(HubZone.Highlight, router.Zone);
            TestSuite.Equal(0, changes.Count);
        });

        foreach (var origin in NonModalZones)
        foreach (var modal in new[] { HubZone.Menu, HubZone.Keyboard })
        {
            suite.Run($"Router: {modal} protects focus and restores {origin}", () =>
            {
                var router = new HubInputRouter();
                router.TrackFocus(origin);
                var changes = Observe(router);
                router.EnterModal(modal);
                router.EnterModal(modal);
                TestSuite.Equal(true, router.IsModal);
                foreach (var attemptedZone in Enum.GetValues<HubZone>())
                {
                    router.TrackFocus(attemptedZone);
                    TestSuite.Equal(modal, router.Zone);
                }
                foreach (var direction in Enum.GetValues<GamepadDirection>())
                    TestSuite.Equal<HubZone?>(null,
                        router.ResolveVerticalExit(direction, true, true, true));
                TestSuite.SequenceEqual(new[] { modal }, changes);
                router.ExitModal();
                TestSuite.Equal(origin, router.Zone);
                TestSuite.Equal(false, router.IsModal);
                TestSuite.SequenceEqual(new[] { modal, origin }, changes);
            });
        }

        foreach (var firstModal in new[] { HubZone.Menu, HubZone.Keyboard })
        {
            suite.Run($"Router: switching modals from {firstModal} retains the original focus zone", () =>
            {
                var router = new HubInputRouter();
                router.TrackFocus(HubZone.Highlight);
                var changes = Observe(router);
                var secondModal = firstModal == HubZone.Menu ? HubZone.Keyboard : HubZone.Menu;
                router.EnterModal(firstModal);
                router.TrackFocus(HubZone.Grid);
                router.EnterModal(secondModal);
                router.TrackFocus(HubZone.Chrome);
                router.ExitModal();
                TestSuite.Equal(HubZone.Highlight, router.Zone);
                TestSuite.SequenceEqual(new[] { firstModal, secondModal, HubZone.Highlight }, changes);
            });
        }

        // Explicit navigation expectations, including absent selection and highlight rows.
        var exits = new (HubZone Zone, GamepadDirection Direction, bool Top, bool Selected, bool Highlights, HubZone? Expected)[]
        {
            (HubZone.Grid, GamepadDirection.Up, true, true, true, HubZone.Highlight),
            (HubZone.Grid, GamepadDirection.Up, true, false, true, HubZone.Highlight),
            (HubZone.Grid, GamepadDirection.Up, true, true, false, HubZone.Hero),
            (HubZone.Grid, GamepadDirection.Up, true, false, false, HubZone.Chrome),
            (HubZone.Grid, GamepadDirection.Up, false, true, true, null),
            (HubZone.Grid, GamepadDirection.Down, true, true, true, null),
            (HubZone.Highlight, GamepadDirection.Up, false, true, true, HubZone.Hero),
            (HubZone.Highlight, GamepadDirection.Up, false, false, true, HubZone.Chrome),
            (HubZone.Highlight, GamepadDirection.Down, false, true, true, HubZone.Grid),
            (HubZone.Hero, GamepadDirection.Up, false, true, true, HubZone.Chrome),
            (HubZone.Hero, GamepadDirection.Down, false, true, true, HubZone.Highlight),
            (HubZone.Hero, GamepadDirection.Down, false, true, false, HubZone.Grid),
            (HubZone.Chrome, GamepadDirection.Down, false, true, true, HubZone.Hero),
            (HubZone.Chrome, GamepadDirection.Down, false, true, false, HubZone.Hero),
            (HubZone.Chrome, GamepadDirection.Down, false, false, true, HubZone.Grid),
            (HubZone.Chrome, GamepadDirection.Down, false, false, false, HubZone.Grid),
            (HubZone.Chrome, GamepadDirection.Up, false, true, true, null),
        };
        foreach (var item in exits)
        {
            suite.Run($"Router: {item.Zone} {item.Direction}, top={item.Top}, selection={item.Selected}, highlights={item.Highlights}", () =>
            {
                var router = new HubInputRouter();
                router.TrackFocus(item.Zone);
                var changes = Observe(router);
                TestSuite.Equal(item.Expected,
                    router.ResolveVerticalExit(item.Direction, item.Top, item.Selected, item.Highlights));
                TestSuite.Equal(item.Zone, router.Zone);
                TestSuite.Equal(0, changes.Count);
            });
        }

        foreach (var zone in NonModalZones)
        {
            suite.Run($"Router: horizontal navigation stays within {zone}", () =>
            {
                var router = new HubInputRouter();
                router.TrackFocus(zone);
                TestSuite.Equal<HubZone?>(null, router.ResolveVerticalExit(GamepadDirection.Left, true, true, true));
                TestSuite.Equal<HubZone?>(null, router.ResolveVerticalExit(GamepadDirection.Right, true, true, true));
                TestSuite.Equal(zone, router.Zone);
            });
        }
    }

    private static List<HubZone> Observe(HubInputRouter router)
    {
        var changes = new List<HubZone>();
        router.ZoneChanged += changes.Add;
        return changes;
    }
}
