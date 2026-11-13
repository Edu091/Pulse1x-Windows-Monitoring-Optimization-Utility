using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Pulse1x.App.Localization;
using Pulse1x.App.Services.Recording;

namespace Pulse1x.App.Views.Recording;

/// <summary>Chamadas Win32 das janelas da Gravação.</summary>
internal static class RecordingNative
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    /// <summary>A janela some das capturas de tela (inclusive do próprio vídeo). Windows 10 2004+.</summary>
    public static void ExcludeFromCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    /// <summary>Cliques atravessam a janela e ela nunca rouba o foco do jogo.</summary>
    public static void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExLayered);
    }

    /// <summary>Recebe cliques, mas nunca rouba o foco (o jogo continua com o teclado) nem aparece na barra de tarefas.</summary>
    public static void MakeNoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>
    /// Só move (pixels físicos), sem mexer no tamanho: passar o tamanho ao SetWindowPos desliga o
    /// SizeToContent do WPF e a janela deixa de acompanhar o conteúdo (cortava o aviso e impedia a
    /// barra de crescer ao abrir as configurações rápidas).
    /// </summary>
    public static void MovePhysical(Window window, int left, int top)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, SwpNoZOrder | SwpNoActivate | SwpNoSize);
    }

    /// <summary>Posiciona a janela num retângulo em pixels físicos.</summary>
    public static void PlacePhysical(Window window, int left, int top, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
    }

    public static (int X, int Y) PhysicalCursor()
    {
        GetPhysicalCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>O monitor onde está o mouse (para abrir o menu flutuante onde o usuário está olhando).</summary>
    public static CaptureMonitor? MonitorUnderCursor(IReadOnlyList<CaptureMonitor> monitors)
    {
        var (x, y) = PhysicalCursor();
        return monitors.FirstOrDefault(m => x >= m.Left && x < m.Left + m.Width && y >= m.Top && y < m.Top + m.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out Point point);
}

/// <summary>
/// Tela escurecida sobre um monitor onde o usuário arrasta o retângulo a gravar. A área sai em
/// pixels físicos, lida direto do cursor (independe da escala do Windows).
/// </summary>
public sealed class RegionSelectorWindow : Window
{
    private readonly CaptureMonitor _monitor;
    private readonly Canvas _canvas = new();
    private readonly Rectangle _box;
    private readonly TextBlock _sizeLabel;
    private Point _startDip;
    private (int X, int Y) _startPx;
    private bool _dragging;

    /// <summary>Área escolhida (relativa ao monitor), ou null se cancelou.</summary>
    public Int32Rect? Result { get; private set; }

    public RegionSelectorWindow(CaptureMonitor monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0; Width = 100; Height = 100;

        var accent = (Brush?)Application.Current.TryFindResource("BrandAccentBrush") ?? Brushes.Red;
        _box = new Rectangle
        {
            Stroke = accent,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed,
        };
        _sizeLabel = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x11, 0x11, 0x11)),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 13,
            Visibility = Visibility.Collapsed,
        };
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x11, 0x11, 0x11)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 10, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = Loc.S("Rec_RegionHint"), Foreground = Brushes.White, FontSize = 15 },
        };
        _canvas.Children.Add(_box);
        _canvas.Children.Add(_sizeLabel);
        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            RecordingNative.ExcludeFromCapture(this);
            RecordingNative.PlacePhysical(this, monitor.Left, monitor.Top, monitor.Width, monitor.Height);
        };
        Loaded += (_, _) => Activate();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => Close();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _startDip = e.GetPosition(_canvas);
        _startPx = RecordingNative.PhysicalCursor();
        CaptureMouse();
        _box.Visibility = Visibility.Visible;
        _sizeLabel.Visibility = Visibility.Visible;
        OnMove(sender, e);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(_canvas);
        double x = Math.Min(p.X, _startDip.X), y = Math.Min(p.Y, _startDip.Y);
        Canvas.SetLeft(_box, x);
        Canvas.SetTop(_box, y);
        _box.Width = Math.Abs(p.X - _startDip.X);
        _box.Height = Math.Abs(p.Y - _startDip.Y);

        var now = RecordingNative.PhysicalCursor();
        _sizeLabel.Text = $"{Math.Abs(now.X - _startPx.X)} × {Math.Abs(now.Y - _startPx.Y)}";
        Canvas.SetLeft(_sizeLabel, x);
        Canvas.SetTop(_sizeLabel, Math.Max(0, y - 28));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var end = RecordingNative.PhysicalCursor();
        int x1 = Math.Clamp(Math.Min(_startPx.X, end.X) - _monitor.Left, 0, _monitor.Width);
        int y1 = Math.Clamp(Math.Min(_startPx.Y, end.Y) - _monitor.Top, 0, _monitor.Height);
        int x2 = Math.Clamp(Math.Max(_startPx.X, end.X) - _monitor.Left, 0, _monitor.Width);
        int y2 = Math.Clamp(Math.Max(_startPx.Y, end.Y) - _monitor.Top, 0, _monitor.Height);
        if (x2 - x1 < 32 || y2 - y1 < 32)
        {
            // Um clique sem arrastar: recomeça em vez de gravar uma área minúscula.
            _box.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
            return;
        }
        Result = new Int32Rect(x1, y1, x2 - x1, y2 - y1);
        Close();
    }
}

/// <summary>
/// Indicador discreto no canto do monitor gravado: bolinha vermelha piscando + tempo. Cliques
/// atravessam, nunca rouba o foco do jogo e não aparece no vídeo.
/// </summary>
public sealed class RecordingIndicatorWindow : Window
{
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CaptureMonitor _monitor;

    public RecordingIndicatorWindow(CaptureMonitor monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_dot);
        panel.Children.Add(_time);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 5, 14, 5),
            Child = panel,
        };

        SourceInitialized += (_, _) =>
        {
            RecordingNative.MakeClickThrough(this);
            RecordingNative.ExcludeFromCapture(this);
        };
        Loaded += (_, _) => PlaceTopRight();
        SizeChanged += (_, _) => PlaceTopRight();

        var blink = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        _dot.BeginAnimation(OpacityProperty, blink);
    }

    private void PlaceTopRight()
    {
        var source = PresentationSource.FromVisual(this);
        double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        int w = (int)Math.Ceiling(ActualWidth * scale), h = (int)Math.Ceiling(ActualHeight * scale);
        int margin = (int)(16 * scale);
        RecordingNative.MovePhysical(this, _monitor.Left + _monitor.Width - w - margin, _monitor.Top + margin);
    }

    public void Update(TimeSpan elapsed, bool paused)
    {
        _time.Text = (paused ? Loc.S("Rec_PausedShort") + "  " : "") + FormatElapsed(elapsed);
        _dot.Fill = paused ? new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    public static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}

/// <summary>Contagem regressiva grande no centro do monitor antes de começar a gravar.</summary>
public sealed class CountdownWindow : Window
{
    private readonly TextBlock _number = new()
    {
        Foreground = Brushes.White,
        FontSize = 120,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public CountdownWindow(CaptureMonitor monitor)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; Width = 220; Height = 220;
        Content = new Border
        {
            Width = 200, Height = 200,
            CornerRadius = new CornerRadius(100),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)),
            Child = _number,
        };
        SourceInitialized += (_, _) =>
        {
            RecordingNative.MakeClickThrough(this);
            RecordingNative.ExcludeFromCapture(this);
            var source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            int size = (int)(220 * scale);
            RecordingNative.PlacePhysical(this, monitor.Left + (monitor.Width - size) / 2, monitor.Top + (monitor.Height - size) / 2, size, size);
        };
    }

    /// <summary>Mostra N, N-1, ... 1 e fecha. Cancela pelo token.</summary>
    public async Task RunAsync(int seconds, CancellationToken ct)
    {
        Show();
        try
        {
            for (int i = seconds; i >= 1; i--)
            {
                _number.Text = i.ToString();
                var pop = new DoubleAnimation(1.4, 1.0, TimeSpan.FromMilliseconds(250));
                _number.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform();
                _number.RenderTransform = st;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                await Task.Delay(1000, ct);
            }
        }
        finally { Close(); }
    }
}

/// <summary>
/// Aviso no canto inferior direito ao terminar uma gravação (ou quando ela não começa): fica alguns
/// segundos com o botão "Abrir no Pulse1x", que leva direto à aba Gravação. Não rouba o foco, não
/// aparece no vídeo e o tempo para enquanto o mouse está em cima.
/// </summary>
public sealed class RecordingToastWindow : Window
{
    private readonly CaptureMonitor _monitor;
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public RecordingToastWindow(CaptureMonitor monitor, string title, string detail, bool isError, Action openApp, TimeSpan lifetime)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var accent = new SolidColorBrush(isError ? Color.FromRgb(0xF9, 0x73, 0x16) : Color.FromRgb(0x22, 0xC5, 0x5E));
        var icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = isError ? Wpf.Ui.Controls.SymbolRegular.Warning24 : Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24,
            FontSize = 26, Foreground = accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
        };
        var text = new StackPanel { MaxWidth = 300 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 64 });

        var open = new Button
        {
            Content = Loc.S("Rec_ToastOpen"),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Template = PillTemplate(Color.FromRgb(0xDC, 0x26, 0x26)),
            Foreground = Brushes.White,
        };
        open.Click += (_, _) => { Close(); openApp(); };
        text.Children.Add(open);

        var close = new Button
        {
            // Desenhado (não um caractere): a fonte da janela não tem o "✕".
            Content = new Path
            {
                Data = Geometry.Parse("M0,0 L10,10 M10,0 L0,10"),
                Stroke = Brushes.White, StrokeThickness = 1.6, Opacity = 0.85,
                Width = 10, Height = 10, Stretch = Stretch.None,
            },
            // Padding zero: senão vem o do estilo padrão de botão e não sobra espaço para o "✕".
            Width = 24, Height = 24, Padding = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, -4, -6, 0), Template = PillTemplate(Color.FromArgb(0x01, 0, 0, 0)),
        };
        close.Click += (_, _) => Close();

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(close, 2);
        row.Children.Add(icon);
        row.Children.Add(text);
        row.Children.Add(close);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x16, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Child = row,
        };

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = lifetime };
        _timer.Tick += (_, _) => Close();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { _timer.Interval = TimeSpan.FromSeconds(3); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();

        SourceInitialized += (_, _) =>
        {
            RecordingNative.MakeNoActivate(this);
            RecordingNative.ExcludeFromCapture(this);
        };
        Loaded += (_, _) => { PlaceBottomRight(); _timer.Start(); };
        SizeChanged += (_, _) => PlaceBottomRight();
    }

    private void PlaceBottomRight()
    {
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        int w = (int)Math.Ceiling(ActualWidth * scale), h = (int)Math.Ceiling(ActualHeight * scale);
        int margin = (int)(20 * scale), taskbar = (int)(56 * scale);
        RecordingNative.MovePhysical(this, _monitor.Left + _monitor.Width - w - margin, _monitor.Top + _monitor.Height - h - taskbar);
    }

    private static ControlTemplate PillTemplate(Color background)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(background));
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Control.Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
