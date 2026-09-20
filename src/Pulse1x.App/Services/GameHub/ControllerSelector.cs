namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Only one device drives the menu. Inactive devices must produce a fresh press after the
/// owner's quiet period, so a native controller and its delayed XInput echo cannot both act.
/// Arbitrarily delayed/remapped virtual input is indistinguishable from another user's input.
/// </summary>
internal sealed class ControllerSelector
{
    internal static readonly GamepadSnapshot Neutral = new(false, false, false, false,
        false, false, false, false, false, false, false, false, false, false);
    private readonly Dictionary<string, GamepadSnapshot> _previous = new();
    private readonly HashSet<string> _armed = new();
    private string? _activeId;
    private long _lastInput = long.MinValue / 2;
    private const int HandoffQuietMilliseconds = 120;

    internal void Reset()
    {
        _previous.Clear();
        _armed.Clear();
        _lastInput = long.MinValue / 2;
    }

    internal ControllerReading? Select(IReadOnlyList<ControllerReading> readings, long now)
    {
        var ids = readings.Select(r => r.Identity.Id).ToHashSet();
        if (_activeId is not null && !ids.Contains(_activeId))
            _armed.Clear();
        foreach (var id in _previous.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _previous.Remove(id);
            _armed.Remove(id);
        }

        var effective = new List<ControllerReading>(readings.Count);
        foreach (var reading in readings)
        {
            // Reconnect and resume never treat a button already held as a new press.
            if (Mask(reading.Snapshot) == 0) _armed.Add(reading.Identity.Id);
            effective.Add(_armed.Contains(reading.Identity.Id)
                ? reading : reading with { Snapshot = Neutral });
        }

        var active = effective.FirstOrDefault(r => r.Identity.Id == _activeId);
        bool ownerBusy = active is not null && Mask(active.Snapshot) != 0;
        if (!ownerBusy && now - _lastInput >= HandoffQuietMilliseconds)
        {
            var candidate = effective
                .Where(r => Mask(r.Snapshot) != 0 && Mask(_previous.GetValueOrDefault(r.Identity.Id, Neutral)) == 0)
                .OrderByDescending(r => r.Identity.Family == ControllerFamily.PlayStation)
                .FirstOrDefault();
            if (candidate is not null) active = candidate;
        }

        active ??= effective.OrderByDescending(r => r.Identity.Family == ControllerFamily.PlayStation).FirstOrDefault();
        if (active is not null && Mask(active.Snapshot) != 0) _lastInput = now;
        _activeId = active?.Identity.Id;
        foreach (var reading in effective) _previous[reading.Identity.Id] = reading.Snapshot;
        return active;
    }

    internal static uint Mask(GamepadSnapshot s)
    {
        uint mask = 0;
        if (s.Accept) mask |= 1u << 0;
        if (s.Back) mask |= 1u << 1;
        if (s.Favorite) mask |= 1u << 2;
        if (s.Details) mask |= 1u << 3;
        if (s.LeftBumper) mask |= 1u << 4;
        if (s.RightBumper) mask |= 1u << 5;
        if (s.View) mask |= 1u << 6;
        if (s.Start) mask |= 1u << 7;
        if (s.LeftTrigger) mask |= 1u << 8;
        if (s.Up || s.LeftStickY > 0) mask |= 1u << 9;
        if (s.Down || s.LeftStickY < 0) mask |= 1u << 10;
        if (s.Left || s.LeftStickX < 0) mask |= 1u << 11;
        if (s.Right || s.LeftStickX > 0) mask |= 1u << 12;
        return mask;
    }
}
