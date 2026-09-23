using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.Views;

public partial class InputLabPage : Page
{
    private enum DeviceKind { Keyboard, Mouse, Gamepad }

    private readonly GamepadService _gamepad;
    private readonly RawInputService _rawInput = new();
    private readonly InputMetricsTracker _keyboard = new();
    private readonly InputMetricsTracker _mouse = new();
    private readonly InputMetricsTracker _controller = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly Dictionary<ushort, List<Border>> _virtualKeys = new();
    private readonly HashSet<ushort> _pressedKeys = new();

    private DeviceKind _selectedDevice = DeviceKind.Keyboard;
    private bool _listening;
    private bool _keyboardDetected;
    private bool _mouseDetected;
    private ControllerIdentity? _controllerIdentity;
    private string? _keyboardName;
    private string? _mouseName;
    private string? _keyboardDeviceId;
    private string? _mouseDeviceId;
    private string? _lastKeyboardKey;
    private InputPollingEstimate? _keyboardPollingEstimate;
    private int _lastMouseX;
    private int _lastMouseY;

    public event Action? BackRequested;

    public InputLabPage(GamepadService gamepad)
    {
        InitializeComponent();
        _gamepad = gamepad;
        BuildVirtualKeyboard();
        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _uiTimer.Tick += (_, _) => Refresh();
        IsVisibleChanged += OnVisibilityChanged;
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool visible = (bool)e.NewValue;
        if (visible == _listening) return;
        _listening = visible;

        if (visible)
        {
            _rawInput.SamplesReceived += OnRawInput;
            _gamepad.InputSampled += OnGamepadSampled;
            _gamepad.ActiveControllerChanged += OnControllerChanged;
            _gamepad.Action += OnGamepadAction;
            _gamepad.SetMeasurementMode(true);
            _gamepad.SetActive(true);
            _controllerIdentity = _gamepad.ActiveController;
            _uiTimer.Start();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Window.GetWindow(this) is { } window) _rawInput.Start(window);
                Keyboard.Focus(this);
            }));
        }
        else
        {
            _uiTimer.Stop();
            _rawInput.Stop();
            _rawInput.SamplesReceived -= OnRawInput;
            _gamepad.InputSampled -= OnGamepadSampled;
            _gamepad.ActiveControllerChanged -= OnControllerChanged;
            _gamepad.Action -= OnGamepadAction;
            _gamepad.SetMeasurementMode(false);
            ClearVirtualKeyboard();
        }

        Refresh();
    }

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) BackRequested?.Invoke();
        e.Handled = true;
    }

    private void Page_PreviewKeyUp(object sender, KeyEventArgs e) => e.Handled = true;

    private void OnRawInput(IReadOnlyList<RawInputSample> samples)
    {
        foreach (var sample in samples)
        {
            if (sample.Kind == RawInputKind.Mouse)
            {
                string deviceId = DeviceId(sample);
                if (_mouseDeviceId != deviceId)
                {
                    _mouse.Reset();
                    _mouseDeviceId = deviceId;
                }
                _mouseDetected = true;
                _mouseName = sample.DeviceName;
                _lastMouseX = sample.DeltaX;
                _lastMouseY = sample.DeltaY;
                _mouse.Record(sample.TimestampMilliseconds);
                continue;
            }

            string keyboardDeviceId = DeviceId(sample);
            if (_keyboardDeviceId != keyboardDeviceId)
            {
                _keyboard.Reset();
                _keyboardDeviceId = keyboardDeviceId;
                _keyboardPollingEstimate = null;
                ClearVirtualKeyboard();
            }
            _keyboardDetected = true;
            _keyboardName = sample.DeviceName;
            _keyboardPollingEstimate ??= InputPollingEstimator.EstimateKeyboard(sample.DeviceName, sample.DevicePath);
            ushort virtualKey = NormalizeVirtualKey(sample.VirtualKey);
            _lastKeyboardKey = KeyLabel(virtualKey);
            _keyboard.Record(sample.TimestampMilliseconds);
            SetVirtualKey(virtualKey, !sample.IsKeyUp);
        }
    }

    private static string DeviceId(RawInputSample sample) =>
        string.IsNullOrWhiteSpace(sample.DevicePath) ? sample.DeviceName : sample.DevicePath;

    private void OnGamepadSampled(GamepadInputSample sample)
    {
        _controllerIdentity = sample.Controller;
        _controller.Record(sample.TimestampMilliseconds);
    }

    private void OnControllerChanged(ControllerIdentity? controller)
    {
        _controllerIdentity = controller;
        Refresh();
    }

    private void OnGamepadAction(GamepadAction action)
    {
        if (_listening && action == GamepadAction.Back) BackRequested?.Invoke();
    }

    private void KeyboardTab_Click(object sender, RoutedEventArgs e) => SelectDevice(DeviceKind.Keyboard);
    private void MouseTab_Click(object sender, RoutedEventArgs e) => SelectDevice(DeviceKind.Mouse);
    private void GamepadTab_Click(object sender, RoutedEventArgs e) => SelectDevice(DeviceKind.Gamepad);

    private void SelectDevice(DeviceKind device)
    {
        _selectedDevice = device;
        Refresh();
        Keyboard.Focus(this);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Tracker.Reset();
        Refresh();
        Keyboard.Focus(this);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private InputMetricsTracker Tracker => _selectedDevice switch
    {
        DeviceKind.Mouse => _mouse,
        DeviceKind.Gamepad => _controller,
        _ => _keyboard,
    };

    private void Refresh()
    {
        if (!IsInitialized) return;
        SetVirtualKeyLabel(0x20, Loc.S("InputLab_SpaceKey"));
        KeyboardTab.IsChecked = _selectedDevice == DeviceKind.Keyboard;
        MouseTab.IsChecked = _selectedDevice == DeviceKind.Mouse;
        GamepadTab.IsChecked = _selectedDevice == DeviceKind.Gamepad;
        KeyboardDeviceIcon.Visibility = _selectedDevice == DeviceKind.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        MouseDeviceIcon.Visibility = _selectedDevice == DeviceKind.Mouse ? Visibility.Visible : Visibility.Collapsed;
        GamepadDeviceIcon.Visibility = _selectedDevice == DeviceKind.Gamepad ? Visibility.Visible : Visibility.Collapsed;
        KeyboardVisualizer.Visibility = _selectedDevice == DeviceKind.Keyboard ? Visibility.Visible : Visibility.Collapsed;

        bool connected;
        switch (_selectedDevice)
        {
            case DeviceKind.Mouse:
                connected = _mouseDetected;
                DeviceNameText.Text = _mouseName ?? Loc.S("InputLab_MouseName");
                DeviceDetailText.Text = _mouseDetected
                    ? Loc.F("InputLab_MouseDelta", _lastMouseX, _lastMouseY)
                    : Loc.S("InputLab_MoveMouse");
                InputHintText.Text = Loc.S("InputLab_MouseHint");
                AccuracyText.Text = Loc.S("InputLab_MouseAccuracy");
                RateTitleText.Text = Loc.S("InputLab_RawPolling");
                break;
            case DeviceKind.Gamepad:
                _controllerIdentity = _gamepad.ActiveController;
                connected = _controllerIdentity is not null;
                DeviceNameText.Text = _controllerIdentity?.Name ?? Loc.S("InputLab_NoGamepad");
                DeviceDetailText.Text = _controllerIdentity is null
                    ? Loc.S("InputLab_MoveGamepad")
                    : Loc.F("InputLab_GamepadDetail", _controllerIdentity.Family, _controllerIdentity.Provider);
                InputHintText.Text = Loc.S("InputLab_GamepadHint");
                AccuracyText.Text = Loc.S("InputLab_GamepadAccuracy");
                RateTitleText.Text = Loc.S("InputLab_ObservedPolling");
                break;
            default:
                connected = _keyboardDetected;
                DeviceNameText.Text = _keyboardName ?? Loc.S("InputLab_KeyboardName");
                DeviceDetailText.Text = _lastKeyboardKey is null
                    ? Loc.S("InputLab_PressKey")
                    : Loc.F("InputLab_LastKey", _lastKeyboardKey);
                InputHintText.Text = Loc.S("InputLab_KeyboardHint");
                AccuracyText.Text = Loc.S("InputLab_KeyboardAccuracy");
                RateTitleText.Text = Loc.S(_keyboardPollingEstimate is null
                    ? "InputLab_EventRate"
                    : "InputLab_EstimatedPolling");
                break;
        }

        StatusDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(34, 197, 94) : Color.FromRgb(122, 127, 135));
        StatusText.Text = Loc.S(connected ? (_selectedDevice == DeviceKind.Gamepad ? "InputLab_Connected" : "InputLab_Detected") : "InputLab_Waiting");

        var snapshot = Tracker.Snapshot;
        RateText.Text = _selectedDevice == DeviceKind.Keyboard && _keyboardPollingEstimate is not null
            ? $"~{_keyboardPollingEstimate.Hertz.ToString("N0", CultureInfo.CurrentCulture)}"
            : Format(snapshot.PollingRate, "0");
        AverageText.Text = Format(snapshot.AverageInterval, snapshot.AverageInterval < 1 ? "0.000" : "0.00");
        CurrentText.Text = Format(snapshot.CurrentInterval, snapshot.CurrentInterval < 1 ? "0.000" : "0.00");
        JitterText.Text = Format(snapshot.Jitter, snapshot.Jitter < 1 ? "0.000" : "0.00");
        SampleCountText.Text = Loc.F("InputLab_Samples", snapshot.SampleCount);
        DrawChart(snapshot.Intervals);
    }

    private static string Format(double value, string format) =>
        value <= 0 ? "--" : value.ToString(format, CultureInfo.CurrentCulture);

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart(Tracker.Snapshot.Intervals);

    private void DrawChart(IReadOnlyList<double> intervals)
    {
        if (ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0) return;
        var values = intervals.TakeLast(64).ToArray();
        EmptyChartText.Visibility = values.Length < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (values.Length < 2)
        {
            IntervalLine.Points = new PointCollection();
            return;
        }

        double width = ChartCanvas.ActualWidth;
        double height = ChartCanvas.ActualHeight;
        double max = Math.Max(values.Max(), values.Average() * 1.5);
        var points = new PointCollection(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            double x = i * width / (values.Length - 1);
            double y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 12) - 6;
            points.Add(new Point(x, y));
        }
        IntervalLine.Points = points;
    }

    private void BuildVirtualKeyboard()
    {
        var rows = new[]
        {
            new[] { K("Esc", 0x1B, 1.3), K("F1", 0x70), K("F2", 0x71), K("F3", 0x72), K("F4", 0x73), K("F5", 0x74), K("F6", 0x75), K("F7", 0x76), K("F8", 0x77), K("F9", 0x78), K("F10", 0x79), K("F11", 0x7A), K("F12", 0x7B) },
            new[] { K("`", 0xC0), K("1", 0x31), K("2", 0x32), K("3", 0x33), K("4", 0x34), K("5", 0x35), K("6", 0x36), K("7", 0x37), K("8", 0x38), K("9", 0x39), K("0", 0x30), K("-", 0xBD), K("=", 0xBB), K("Backspace", 0x08, 2.1) },
            new[] { K("Tab", 0x09, 1.55), K("Q", 0x51), K("W", 0x57), K("E", 0x45), K("R", 0x52), K("T", 0x54), K("Y", 0x59), K("U", 0x55), K("I", 0x49), K("O", 0x4F), K("P", 0x50), K("[", 0xDB), K("]", 0xDD), K("\\", 0xDC, 1.55) },
            new[] { K("Caps", 0x14, 1.85), K("A", 0x41), K("S", 0x53), K("D", 0x44), K("F", 0x46), K("G", 0x47), K("H", 0x48), K("J", 0x4A), K("K", 0x4B), K("L", 0x4C), K(";", 0xBA), K("'", 0xDE), K("Enter", 0x0D, 2.25) },
            new[] { K("Shift", 0x10, 2.35), K("Z", 0x5A), K("X", 0x58), K("C", 0x43), K("V", 0x56), K("B", 0x42), K("N", 0x4E), K("M", 0x4D), K(",", 0xBC), K(".", 0xBE), K("/", 0xBF), K("Shift", 0x10, 2.75) },
            new[] { K("Ctrl", 0x11, 1.4), K("Win", 0x5B, 1.3), K("Alt", 0x12, 1.3), K(Loc.S("InputLab_SpaceKey"), 0x20, 7.2), K("Alt", 0x12, 1.3), K("Menu", 0x5D, 1.3), K("Ctrl", 0x11, 1.4) },
        };

        var keyStyle = (Style)FindResource("VirtualKey");
        foreach (var row in rows)
        {
            var grid = new Grid { Height = 38 };
            foreach (var key in row) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(key.Units, GridUnitType.Star) });
            for (int column = 0; column < row.Length; column++)
            {
                var key = row[column];
                var border = new Border { Style = keyStyle, Tag = key.KeyCode };
                var label = new TextBlock { Text = key.Label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                border.Child = label;
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
                if (!_virtualKeys.TryGetValue(key.KeyCode, out var visuals))
                    _virtualKeys[key.KeyCode] = visuals = new List<Border>();
                visuals.Add(border);
            }
            KeyboardRows.Children.Add(grid);
        }
    }

    private void SetVirtualKey(ushort virtualKey, bool pressed)
    {
        if (pressed) _pressedKeys.Add(virtualKey); else _pressedKeys.Remove(virtualKey);
        if (!_virtualKeys.TryGetValue(virtualKey, out var visuals)) return;
        foreach (var border in visuals)
        {
            if (pressed)
            {
                border.SetResourceReference(Border.BackgroundProperty, "BrandAccentBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "BrandAccentBrush");
                if (border.Child is TextBlock text) text.Foreground = Brushes.White;
            }
            else
            {
                border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                if (border.Child is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
        }
    }

    private void ClearVirtualKeyboard()
    {
        foreach (ushort key in _pressedKeys.ToArray()) SetVirtualKey(key, false);
        _pressedKeys.Clear();
    }

    private void SetVirtualKeyLabel(ushort virtualKey, string label)
    {
        if (!_virtualKeys.TryGetValue(virtualKey, out var visuals)) return;
        foreach (var border in visuals)
            if (border.Child is TextBlock text) text.Text = label;
    }

    private static ushort NormalizeVirtualKey(ushort virtualKey) => virtualKey switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => virtualKey,
    };

    private static string KeyLabel(ushort virtualKey) => virtualKey switch
    {
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 => "Shift",
        0x11 => "Ctrl", 0x12 => "Alt", 0x14 => "Caps Lock", 0x1B => "Esc",
        0x20 => Loc.S("InputLab_SpaceKey"), 0x5B => "Windows",
        >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
        _ => KeyInterop.KeyFromVirtualKey(virtualKey).ToString(),
    };

    private static KeyboardKeySpec K(string label, int virtualKey, double units = 1) => new(label, (ushort)virtualKey, units);
    private sealed record KeyboardKeySpec(string Label, ushort KeyCode, double Units);
}
