using Pulse1x.App.Services.GameHub;

namespace Pulse1x.Hub.Tests;

internal static class ControllerTests
{
    internal static void Register(TestSuite suite)
    {
        suite.Run("Controller: held attach/resume cannot confirm; fresh presses fire once", () =>
        {
            var backend = new FakeBackend();
            long now = 0;
            using var service = new GamepadService(backend, () => now);
            var events = new List<GamepadAction>();
            service.Action += events.Add;
            service.SetActive(true);
            void Tick(GamepadSnapshot snapshot)
            {
                backend.Readings = new[] { Reading("x", ControllerFamily.Xbox, snapshot) };
                now += 20;
                service.Tick();
            }
            Tick(ControllerSelector.Neutral with { Accept = true });
            TestSuite.Equal(0, events.Count);
            Tick(ControllerSelector.Neutral);
            Tick(ControllerSelector.Neutral with { Accept = true });
            Tick(ControllerSelector.Neutral with { Accept = true });
            TestSuite.SequenceEqual(new[] { GamepadAction.Accept }, events);
            service.SetActive(false);
            service.SetActive(true);
            Tick(ControllerSelector.Neutral with { Accept = true });
            TestSuite.Equal(1, events.Count);
            Tick(ControllerSelector.Neutral);
            Tick(ControllerSelector.Neutral with { Accept = true });
            TestSuite.Equal(2, events.Count);
        });
        suite.Run("Controller: Xbox and PlayStation share action positions", () =>
        {
            var backend = new FakeBackend();
            long now = 0;
            using var service = new GamepadService(backend, () => now);
            service.SetActive(true);
            foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation, ControllerFamily.Generic })
            {
                var events = new List<GamepadAction>();
                void Record(GamepadAction action) => events.Add(action);
                service.Action += Record;
                backend.Readings = new[] { Reading(family.ToString(), family, ControllerSelector.Neutral) };
                now += 500;
                service.Tick();
                backend.Readings = new[] { Reading(family.ToString(), family, ControllerSelector.Neutral with
                { Accept = true, Back = true, Favorite = true, Details = true, LeftBumper = true,
                  RightBumper = true, View = true, Start = true, LeftTrigger = true }) };
                now += 20;
                service.Tick();
                TestSuite.SequenceEqual(Enum.GetValues<GamepadAction>(), events);
                TestSuite.Equal(family, service.ActiveControllerFamily);
                service.Action -= Record;
            }
        });
        suite.Run("Controller: native PlayStation wins simultaneous virtual Xbox echo", () =>
        {
            var selector = new ControllerSelector();
            var neutral = ControllerSelector.Neutral;
            var devices = new[] { Reading("x", ControllerFamily.Xbox, neutral), Reading("p", ControllerFamily.PlayStation, neutral) };
            selector.Select(devices, 0);
            var pressed = devices.Select(d => d with { Snapshot = neutral with { Accept = true } }).ToArray();
            TestSuite.Equal("p", selector.Select(pressed, 200)!.Identity.Id);
            var delayed = new[] { devices[1], pressed[0] };
            TestSuite.Equal("p", selector.Select(delayed, 240)!.Identity.Id);
            TestSuite.Equal(false, selector.Select(delayed, 500)!.Snapshot.Accept);
            selector.Select(devices, 520);
            TestSuite.Equal("x", selector.Select(delayed, 700)!.Identity.Id);
        });
        suite.Run("Controller: axis and trigger hysteresis reject center drift", () =>
        {
            var axis = new ControllerAxisFilter();
            TestSuite.Equal(0f, axis.Read(0.4f));
            TestSuite.Equal(0.7f, axis.Read(0.7f));
            TestSuite.Equal(0.4f, axis.Read(0.4f));
            TestSuite.Equal(0f, axis.Read(-0.2f));
            TestSuite.Equal(-0.8f, axis.Read(-0.8f));
            TestSuite.Equal(0.8f, axis.Read(0.8f));
            var trigger = new ControllerTriggerFilter();
            TestSuite.Equal(false, trigger.Read(0.1f));
            TestSuite.Equal(true, trigger.Read(0.8f));
            TestSuite.Equal(true, trigger.Read(0.5f));
            TestSuite.Equal(false, trigger.Read(0.1f));
        });
        suite.Run("Controller: measurement samples only report state changes", () =>
        {
            var backend = new FakeBackend();
            long now = 0;
            using var service = new GamepadService(backend, () => now);
            var samples = new List<GamepadInputSample>();
            service.InputSampled += samples.Add;
            service.SetActive(true);

            backend.Readings = new[] { Reading("x", ControllerFamily.Xbox, ControllerSelector.Neutral) };
            service.Tick();
            now = 4;
            backend.Readings = new[] { Reading("x", ControllerFamily.Xbox,
                ControllerSelector.Neutral with { LeftStickX = 0.7f }) };
            service.Tick();
            now = 8;
            service.Tick();
            now = 12;
            backend.Readings = new[] { Reading("x", ControllerFamily.Xbox, ControllerSelector.Neutral) };
            service.Tick();

            TestSuite.Equal(2, samples.Count);
            TestSuite.Equal(4d, samples[0].TimestampMilliseconds);
            TestSuite.Equal(12d, samples[1].TimestampMilliseconds);
        });
        suite.Run("Controller: measurement preserves sub-millisecond timestamps", () =>
        {
            var backend = new FakeBackend();
            double sampleTime = 0;
            using var service = new GamepadService(backend, () => 0, () => sampleTime);
            var samples = new List<GamepadInputSample>();
            service.InputSampled += samples.Add;
            service.SetActive(true);

            backend.Readings = new[] { Reading("x", ControllerFamily.Xbox, ControllerSelector.Neutral) };
            service.Tick();
            sampleTime = 0.125;
            backend.Readings = new[] { Reading("x", ControllerFamily.Xbox,
                ControllerSelector.Neutral with { LeftStickX = 0.7f }) };
            service.Tick();

            TestSuite.Equal(1, samples.Count);
            TestSuite.Equal(0.125d, samples[0].TimestampMilliseconds);
        });
        suite.Run("Controller: SDL native library loads and enumerates", () =>
        {
            using var sdl = new SdlGamepadProvider();
            if (!sdl.TryInitialize()) throw new InvalidOperationException(sdl.Error);
            foreach (var reading in sdl.PollControllers())
                Console.WriteLine($"  Detected: {reading.Identity.Name} ({reading.Identity.Family})");
        });
    }

    private static ControllerReading Reading(string id, ControllerFamily family, GamepadSnapshot snapshot) =>
        new(new(id, id, "test", family, ControllerButtonLabels.Xbox), snapshot);

    private sealed class FakeBackend : IControllerBackend
    {
        public IReadOnlyList<ControllerReading> Readings { get; set; } = Array.Empty<ControllerReading>();
        public IReadOnlyList<ControllerReading> PollControllers() => Readings;
        public void Dispose() { }
    }
}
