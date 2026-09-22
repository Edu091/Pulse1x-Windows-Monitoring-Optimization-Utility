using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pulse1x.App.Localization;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;

namespace Pulse1x.App.Views;

public partial class InputLabPage : Page
{
    private readonly GamepadService _gamepad;
    private readonly InputMetricsTracker _keyboard = new();
    private readonly InputMetricsTracker _controller = new();
    private bool _showGamepad;
    private bool _listening;
    private bool _keyboardDetected;
    private ControllerIdentity? _controllerIdentity;
    private string? _lastKeyboardKey;

    public event Action? BackRequested;

    public InputLabPage(GamepadService gamepad)
    {
        InitializeComponent();
        _gamepad = gamepad;
        IsVisibleChanged += OnVisibilityChanged;
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    private static double NowMilliseconds() =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool visible = (bool)e.NewValue;
        if (visible == _listening) return;
        _listening = visible;

        if (visible)
        {
            _gamepad.InputSampled += OnGamepadSampled;
            _gamepad.ActiveControllerChanged += OnControllerChanged;
            _gamepad.Action += OnGamepadAction;
            _gamepad.SetMeasurementMode(true);
            _controllerIdentity = _gamepad.ActiveController;
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(this)));
        }
        else
        {
            _gamepad.InputSampled -= OnGamepadSampled;
            _gamepad.ActiveControllerChanged -= OnControllerChanged;
            _gamepad.Action -= OnGamepadAction;
            _gamepad.SetMeasurementMode(false);
        }

        Refresh();
    }

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            BackRequested?.Invoke();
            e.Handled = true;
            return;
        }

        _keyboardDetected = true;
        _lastKeyboardKey = e.Key.ToString();
        _keyboard.Record(NowMilliseconds());
        SelectDevice(gamepad: false);
        e.Handled = true;
    }

    private void Page_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        _keyboardDetected = true;
        _lastKeyboardKey = e.Key.ToString();
        _keyboard.Record(NowMilliseconds());
        SelectDevice(gamepad: false);
        e.Handled = true;
    }

    private void OnGamepadSampled(GamepadInputSample sample)
    {
        _controllerIdentity = sample.Controller;
        _controller.Record(sample.TimestampMilliseconds);
        SelectDevice(gamepad: true);
    }

    private void OnControllerChanged(ControllerIdentity? controller)
    {
        _controllerIdentity = controller;
        Refresh();
    }

    private void OnGamepadAction(GamepadAction action)
    {
        if (_listening && action == GamepadAction.Back)
            BackRequested?.Invoke();
    }

    private void KeyboardTab_Click(object sender, RoutedEventArgs e) => SelectDevice(gamepad: false);
    private void GamepadTab_Click(object sender, RoutedEventArgs e) => SelectDevice(gamepad: true);

    private void SelectDevice(bool gamepad)
    {
        _showGamepad = gamepad;
        KeyboardTab.IsChecked = !gamepad;
        GamepadTab.IsChecked = gamepad;
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _keyboard.Reset();
        _controller.Reset();
        Refresh();
        Keyboard.Focus(this);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void Refresh()
    {
        if (!IsInitialized) return;

        var snapshot = _showGamepad ? _controller.Snapshot : _keyboard.Snapshot;
        bool connected = _showGamepad ? _controllerIdentity is not null : _keyboardDetected;

        KeyboardDeviceIcon.Visibility = _showGamepad ? Visibility.Collapsed : Visibility.Visible;
        GamepadDeviceIcon.Visibility = _showGamepad ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(34, 197, 94) : Color.FromRgb(122, 127, 135));

        if (_showGamepad)
        {
            DeviceNameText.Text = _controllerIdentity?.Name ?? Loc.S("InputLab_NoGamepad");
            DeviceDetailText.Text = _controllerIdentity is null
                ? Loc.S("InputLab_MoveGamepad")
                : Loc.F("InputLab_GamepadDetail", _controllerIdentity.Family, _controllerIdentity.Provider);
            StatusText.Text = Loc.S(connected ? "InputLab_Connected" : "InputLab_Waiting");
            InputHintText.Text = Loc.S("InputLab_GamepadHint");
        }
        else
        {
            DeviceNameText.Text = Loc.S("InputLab_KeyboardName");
            DeviceDetailText.Text = _lastKeyboardKey is null
                ? Loc.S("InputLab_PressKey")
                : Loc.F("InputLab_LastKey", _lastKeyboardKey);
            StatusText.Text = Loc.S(connected ? "InputLab_Detected" : "InputLab_Waiting");
            InputHintText.Text = Loc.S("InputLab_KeyboardHint");
        }

        RateText.Text = Format(snapshot.PollingRate, "0");
        AverageText.Text = Format(snapshot.AverageInterval, "0.00");
        CurrentText.Text = Format(snapshot.CurrentInterval, "0.00");
        JitterText.Text = Format(snapshot.Jitter, "0.00");
        SampleCountText.Text = Loc.F("InputLab_Samples", snapshot.SampleCount);
        DrawChart(snapshot.Intervals);
    }

    private static string Format(double value, string format) =>
        value <= 0 ? "--" : value.ToString(format, CultureInfo.CurrentCulture);

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DrawChart((_showGamepad ? _controller : _keyboard).Snapshot.Intervals);

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
}
