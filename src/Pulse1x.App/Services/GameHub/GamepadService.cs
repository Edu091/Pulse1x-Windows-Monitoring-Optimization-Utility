using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;

namespace Pulse1x.App.Services.GameHub;

public enum GamepadDirection { Up, Down, Left, Right }

/// <summary>Logical menu actions, independent of the physical controller.</summary>
public enum GamepadAction
{
    Accept, Back, Favorite, Search, PreviousTab, NextTab, Menu, Details, GameActions
}

/// <summary>Legacy single-controller provider contract.</summary>
public interface IGamepadProvider
{
    string Name { get; }
    bool IsConnected { get; }
    GamepadSnapshot? Poll();
}

/// <summary>Normalized input; positive LeftStickY means up. Details is the north face button.</summary>
public record GamepadSnapshot(
    bool Up, bool Down, bool Left, bool Right,
    bool Accept, bool Back, bool Favorite, bool Details,
    bool LeftBumper, bool RightBumper, bool LeftTrigger, bool RightTrigger,
    bool View, bool Start,
    float LeftStickX = 0, float LeftStickY = 0);

/// <summary>Alteração observada no estado do controle, usada por ferramentas de diagnóstico.</summary>
public sealed record GamepadInputSample(
    double TimestampMilliseconds, ControllerIdentity Controller, GamepadSnapshot Snapshot);

/// <summary>
/// Polls only while the hub is active. Construct, activate and dispose on the WPF dispatcher.
/// All events and property notifications are raised on that dispatcher.
/// </summary>
public class GamepadService : IDisposable, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private readonly IControllerBackend _backend;
    private readonly ControllerSelector _selector = new();
    private readonly Func<long> _milliseconds;
    private readonly Func<double> _sampleMilliseconds;
    private GamepadSnapshot? _previous;
    private readonly Dictionary<GamepadDirection, long> _heldSince = new();
    private readonly Dictionary<GamepadDirection, long> _lastRepeat = new();
    private bool _disposed;
    private bool _active;
    private bool _ticking;

    public event Action<GamepadDirection>? Navigate;
    public event Action<GamepadAction>? Action;
    public event Action<bool>? ConnectionChanged;
    public event Action<ControllerIdentity?>? ActiveControllerChanged;
    public event Action<GamepadInputSample>? InputSampled;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ControllerIdentity? ActiveController { get; private set; }
    public ControllerFamily ActiveControllerFamily => ActiveController?.Family ?? ControllerFamily.Unknown;
    public ControllerButtonLabels ActiveControllerLabels => ActiveController?.Labels ?? ControllerButtonLabels.Generic;
    public string? ActiveControllerName => ActiveController?.Name;
    public string? ActiveProviderName => ActiveController?.Provider;
    public bool IsConnected => ActiveController is not null;

    public GamepadService() : this(
        new FallbackControllerBackend(),
        () => Environment.TickCount64,
        () => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency) { }

    internal GamepadService(IControllerBackend backend, Func<long> milliseconds, Func<double>? sampleMilliseconds = null)
    {
        _backend = backend;
        _milliseconds = milliseconds;
        _sampleMilliseconds = sampleMilliseconds ?? (() => milliseconds());
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        if (_active == active) return;
        _active = active;
        ResetInput();
        _selector.Reset();
        if (active) _timer.Start();
        else _timer.Stop();
    }

    /// <summary>
    /// Aumenta temporariamente a frequência de leitura para ferramentas de medição. O modo normal
    /// continua em 16 ms para não gastar CPU enquanto o usuário apenas navega pelo aplicativo.
    /// </summary>
    public void SetMeasurementMode(bool enabled)
    {
        if (_disposed) return;
        _timer.Interval = TimeSpan.FromMilliseconds(enabled ? 1 : 16);
    }

    private void OnTick(object? sender, EventArgs e) => Tick();

    internal void Tick()
    {
        if (_disposed || !_active || _ticking) return;
        _ticking = true;
        try
        {
            var reading = _selector.Select(_backend.PollControllers(), _milliseconds());
            var oldIdentity = ActiveController;
            var identity = reading?.Identity;
            bool switched = oldIdentity?.Id != identity?.Id;
            if (switched) ResetInput();
            if (oldIdentity != identity)
            {
                ActiveController = identity;
                foreach (var property in new[] { nameof(ActiveController), nameof(ActiveControllerFamily),
                    nameof(ActiveControllerLabels), nameof(ActiveControllerName), nameof(ActiveProviderName), nameof(IsConnected) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
                ActiveControllerChanged?.Invoke(identity);
                if ((oldIdentity is null) != (identity is null)) ConnectionChanged?.Invoke(identity is not null);
            }

            if (reading is null || !_active || _disposed) return;
            var snapshot = reading.Snapshot;
            // Capture this before callbacks: actions can suspend the hub or open a modal dispatcher.
            var previous = _previous;
            _previous = snapshot;
            if (previous is not null && snapshot != previous)
                InputSampled?.Invoke(new(_sampleMilliseconds(), reading.Identity, snapshot));
            var direction = ResolveDirection(snapshot);
            foreach (var candidate in Enum.GetValues<GamepadDirection>())
            {
                if (!_active || _disposed) return;
                HandleDirection(candidate, candidate == direction);
            }

            HandleButton(GamepadAction.Accept, snapshot.Accept, previous?.Accept);
            HandleButton(GamepadAction.Back, snapshot.Back, previous?.Back);
            HandleButton(GamepadAction.Favorite, snapshot.Favorite, previous?.Favorite);
            HandleButton(GamepadAction.Search, snapshot.Details, previous?.Details);
            HandleButton(GamepadAction.PreviousTab, snapshot.LeftBumper, previous?.LeftBumper);
            HandleButton(GamepadAction.NextTab, snapshot.RightBumper, previous?.RightBumper);
            HandleButton(GamepadAction.Menu, snapshot.View, previous?.View);
            HandleButton(GamepadAction.Details, snapshot.Start, previous?.Start);
            HandleButton(GamepadAction.GameActions, snapshot.LeftTrigger, previous?.LeftTrigger);
        }
        finally { _ticking = false; }
    }

    private static GamepadDirection? ResolveDirection(GamepadSnapshot snapshot)
    {
        float horizontal = Math.Abs(snapshot.LeftStickX);
        float vertical = Math.Abs(snapshot.LeftStickY);
        if (horizontal > 0 || vertical > 0)
        {
            if (vertical >= horizontal)
                return snapshot.LeftStickY > 0 ? GamepadDirection.Up : GamepadDirection.Down;
            return snapshot.LeftStickX > 0 ? GamepadDirection.Right : GamepadDirection.Left;
        }
        if (snapshot.Up) return GamepadDirection.Up;
        if (snapshot.Down) return GamepadDirection.Down;
        if (snapshot.Left) return GamepadDirection.Left;
        if (snapshot.Right) return GamepadDirection.Right;
        return null;
    }

    private void HandleDirection(GamepadDirection direction, bool pressed)
    {
        var now = _milliseconds();
        if (!pressed)
        {
            _heldSince.Remove(direction);
            _lastRepeat.Remove(direction);
            return;
        }
        if (!_heldSince.TryGetValue(direction, out var since))
        {
            _heldSince[direction] = now;
            _lastRepeat[direction] = now;
            Navigate?.Invoke(direction);
            return;
        }
        if (now - since < 330 || now - _lastRepeat[direction] < 85) return;
        _lastRepeat[direction] = now;
        Navigate?.Invoke(direction);
    }

    private void HandleButton(GamepadAction action, bool pressed, bool? wasPressed)
    {
        if (_active && !_disposed && pressed && wasPressed != true) Action?.Invoke(action);
    }

    private void ResetInput()
    {
        _previous = null;
        _heldSince.Clear();
        _lastRepeat.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        ResetInput();
        _backend.Dispose();
    }
}

/// <summary>SDL owns every device when available; never poll XInput alongside it.</summary>
internal sealed class FallbackControllerBackend : IControllerBackend
{
    private IControllerBackend? _backend;

    public IReadOnlyList<ControllerReading> PollControllers()
    {
        if (_backend is null)
        {
            var sdl = new SdlGamepadProvider();
            if (sdl.TryInitialize()) _backend = sdl;
            else
            {
                Debug.WriteLine($"SDL controller support unavailable: {sdl.Error}. Using XInput.");
                sdl.Dispose();
                _backend = new XInputProvider();
            }
        }
        return _backend.PollControllers();
    }

    public void Dispose() => _backend?.Dispose();
}
