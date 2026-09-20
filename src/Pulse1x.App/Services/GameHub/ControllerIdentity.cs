namespace Pulse1x.App.Services.GameHub;

public enum ControllerFamily { Unknown, Xbox, PlayStation, Nintendo, Generic }

/// <summary>Face-button names are also stable glyph keys (Cross, Circle, Square, Triangle, A, B, X, Y).</summary>
public sealed record ControllerButtonLabels(
    string Accept, string Back, string Favorite, string Search,
    string PreviousTab, string NextTab, string Menu, string Details, string GameActions)
{
    public string this[GamepadAction action] => action switch
    {
        GamepadAction.Accept => Accept,
        GamepadAction.Back => Back,
        GamepadAction.Favorite => Favorite,
        GamepadAction.Search => Search,
        GamepadAction.PreviousTab => PreviousTab,
        GamepadAction.NextTab => NextTab,
        GamepadAction.Menu => Menu,
        GamepadAction.Details => Details,
        GamepadAction.GameActions => GameActions,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static ControllerButtonLabels Xbox { get; } = new("A", "B", "X", "Y", "LB", "RB", "View", "Menu", "LT");
    public static ControllerButtonLabels Generic { get; } = new("South", "East", "West", "North", "L1", "R1", "Select", "Start", "L2");
}

/// <summary>Id is stable for a connection, not across unplug/replug. Hidden physical devices cannot be identified through XInput.</summary>
public sealed record ControllerIdentity(string Id, string Name, string Provider,
    ControllerFamily Family, ControllerButtonLabels Labels);

internal sealed record ControllerReading(ControllerIdentity Identity, GamepadSnapshot Snapshot);

internal interface IControllerBackend : IDisposable
{
    IReadOnlyList<ControllerReading> PollControllers();
}

internal sealed class ControllerAxisFilter
{
    private int _held;

    public float Read(float value)
    {
        value = Math.Clamp(value, -1, 1);
        var magnitude = Math.Abs(value);
        if (magnitude >= 0.60f) _held = Math.Sign(value);
        else if (magnitude < 0.35f || Math.Sign(value) != _held) _held = 0;
        return _held == 0 ? 0 : value;
    }
}

internal sealed class ControllerTriggerFilter
{
    private bool _held;
    public bool Read(float value) => _held = value > (_held ? 80f / 255 : 160f / 255);
}
