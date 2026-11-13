using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using Pulse1x.App.Services.Recording;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views.Recording;

/// <summary>
/// Barra flutuante da Gravação: uma faixa compacta no topo da tela com gravar/encerrar,
/// pausar/continuar, configurações rápidas e ocultar. Arrastável pela alça (a posição fica salva),
/// mostrada e escondida pelo atalho, não rouba o foco do jogo e não aparece no vídeo.
/// </summary>
public partial class RecordingToolbarWindow : Window
{
    private readonly RecordingViewModel _vm;
    private readonly RecordingSettings _settings;
    private readonly Action _save;
    private readonly CaptureMonitor _monitor;

    public RecordingToolbarWindow(RecordingViewModel viewModel, RecordingSettings settings, Action save, CaptureMonitor monitor)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;
        _settings = settings;
        _save = save;
        _monitor = monitor;

        SourceInitialized += (_, _) =>
        {
            RecordingNative.MakeNoActivate(this);
            RecordingNative.ExcludeFromCapture(this);
        };
        Loaded += (_, _) => PlaceInitially();
    }

    /// <summary>Posição salva (se ainda cair dentro de algum monitor) ou o topo central do monitor.</summary>
    private void PlaceInitially()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;

        if (_settings.ToolbarX is int x && _settings.ToolbarY is int y
            && DisplayEnumerator.GetMonitors().Any(m => x + w / 2 >= m.Left && x + w / 2 < m.Left + m.Width && y >= m.Top && y < m.Top + m.Height))
        {
            RecordingNative.MovePhysical(this, x, y);
            return;
        }
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        RecordingNative.MovePhysical(this, _monitor.Left + (_monitor.Width - w) / 2, _monitor.Top + (int)(10 * scale));
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        // Soltou: guarda a posição em pixels físicos.
        GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
        _settings.ToolbarX = r.Left;
        _settings.ToolbarY = r.Top;
        _save();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => _vm.HideToolbar();

    private void MoreSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = false;
        _vm.OpenAppCommand.Execute(null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}

/// <summary>
/// true quando o valor (texto) é igual ao parâmetro; ao marcar, devolve o parâmetro convertido para
/// o tipo da propriedade. Liga um grupo de botões de opção a uma única chave do ViewModel.
/// </summary>
public sealed class KeyEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        if (targetType == typeof(bool)) return bool.Parse(parameter.ToString()!);
        return parameter.ToString()!;
    }
}
