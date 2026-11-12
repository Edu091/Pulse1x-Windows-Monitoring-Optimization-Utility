using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Pulse1x.App.Localization;
using Pulse1x.App.Services.Recording;
using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Views.Recording;

/// <summary>
/// Menu flutuante da Gravação (aberto pelo atalho, como a Game Bar do Xbox): escurece o monitor
/// onde está o mouse e mostra os controles de captura e as últimas gravações. Não aparece no vídeo.
/// Esc, o atalho de novo ou um clique fora fecham.
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    private readonly DispatcherTimer _clock;
    private bool _closing;

    public RecordingOverlayWindow(RecordingViewModel viewModel, CaptureMonitor monitor)
    {
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += (_, _) =>
        {
            RecordingNative.ExcludeFromCapture(this);
            RecordingNative.PlacePhysical(this, monitor.Left, monitor.Top, monitor.Width, monitor.Height);
        };

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("t");
        ClockText.Text = DateTime.Now.ToString("t");
        _clock.Start();

        HotkeyHint.Text = Loc.F("Rec_OverlayHotkeys",
            viewModel.StartHotkey?.Display() ?? "—", viewModel.StopHotkey?.Display() ?? "—",
            viewModel.PauseHotkey?.Display() ?? "—", viewModel.OverlayHotkey?.Display() ?? "—");

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closed += (_, _) => _clock.Stop();
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
