using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Pulse1x.App.Services;
using Pulse1x.App.Services.Recording;

namespace Pulse1x.Hub.Tests;

/// <summary>Categoria Gravação: linhas de comando do ffmpeg, área/escala, atalhos, textos e persistência.</summary>
internal static class RecordingTests
{
    private static CaptureMonitor Monitor(uint vendor = CaptureMonitor.VendorNvidia, int left = 0, int adapter = 0) =>
        new(@"\\.\DISPLAY1", 1, adapter, 0, vendor, "GPU", left, 0, 1920, 1080, left == 0);

    public static void Register(TestSuite suite)
    {
        suite.Run("Recording geometry clips the area to the monitor, keeps even sides and scales by height", () =>
        {
            var s = new RecordingSettings();
            var full = RecordingCommandBuilder.Geometry(s, 1920, 1080);
            TestSuite.Equal(new CaptureGeometry(0, 0, 1920, 1080, 1920, 1080, true), full);

            s.CaptureTarget = "Region";
            s.RegionX = 1800; s.RegionY = 1000; s.RegionWidth = 501; s.RegionHeight = 301;
            var clipped = RecordingCommandBuilder.Geometry(s, 1920, 1080);
            TestSuite.Equal(1800, clipped.X);
            TestSuite.Equal(120, clipped.Width);   // 1920 - 1800
            TestSuite.Equal(80, clipped.Height);   // 1080 - 1000
            TestSuite.Equal(false, clipped.IsFullMonitor);

            s.RegionX = 11; s.RegionY = 0; s.RegionWidth = 1001; s.RegionHeight = 777;
            var odd = RecordingCommandBuilder.Geometry(s, 1920, 1080);
            TestSuite.Equal(1000, odd.Width);
            TestSuite.Equal(776, odd.Height);

            // Região minúscula (ou não escolhida) = tela inteira.
            s.RegionWidth = 4;
            TestSuite.Equal(true, RecordingCommandBuilder.Geometry(s, 1920, 1080).IsFullMonitor);

            var scaled = RecordingCommandBuilder.Geometry(new RecordingSettings { Resolution = "720" }, 2560, 1440);
            TestSuite.Equal((1280, 720), (scaled.OutWidth, scaled.OutHeight));
            // Não aumenta: tela menor que o limite fica nativa.
            var small = RecordingCommandBuilder.Geometry(new RecordingSettings { Resolution = "1440" }, 1920, 1080);
            TestSuite.Equal(false, small.Scaled);
        });

        suite.Run("Recording segment uses GPU frames only for unscaled NVENC on an NVIDIA monitor", () =>
        {
            var s = new RecordingSettings { Fps = 60 };
            var m = Monitor();
            var g = RecordingCommandBuilder.Geometry(s, m.Width, m.Height);

            string direct = RecordingCommandBuilder.SegmentArgs(s, m, g, CaptureMethod.Ddagrab, "h264_nvenc", @"C:\t\p.mkv");
            TestSuite.Equal(false, direct.Contains("hwdownload"));
            TestSuite.Equal(true, direct.Contains("ddagrab=output_idx=0:framerate=60:draw_mouse=1[v]"));
            TestSuite.Equal(true, direct.Contains("-init_hw_device d3d11va=pulsecap:0"));
            TestSuite.Equal(true, direct.Contains("-an \"C:\\t\\p.mkv\""));

            var scaledSettings = new RecordingSettings { Resolution = "720" };
            var sg = RecordingCommandBuilder.Geometry(scaledSettings, m.Width, m.Height);
            string scaled = RecordingCommandBuilder.SegmentArgs(scaledSettings, m, sg, CaptureMethod.Ddagrab, "h264_nvenc", "p.mkv");
            TestSuite.Equal(true, scaled.Contains("hwdownload,format=bgra,scale=1280:720:flags=bicubic,format=nv12"));
            TestSuite.Equal(false, scaled.Contains("scale_d3d11"));

            // NVENC com o monitor ligado na placa Intel: cópia pela memória.
            string cross = RecordingCommandBuilder.SegmentArgs(s, Monitor(CaptureMonitor.VendorIntel, adapter: 1), g, CaptureMethod.Ddagrab, "h264_nvenc", "p.mkv");
            TestSuite.Equal(true, cross.Contains("hwdownload"));
            TestSuite.Equal(true, cross.Contains("d3d11va=pulsecap:1"));

            string cpu = RecordingCommandBuilder.SegmentArgs(s, m, g, CaptureMethod.Ddagrab, "libx265", "p.mkv");
            TestSuite.Equal(true, cpu.Contains("format=yuv420p"));
        });

        suite.Run("Recording region and GDI fallback use the right offsets", () =>
        {
            var s = new RecordingSettings { CaptureTarget = "Region", RegionX = 100, RegionY = 50, RegionWidth = 800, RegionHeight = 600, DrawMouse = false };
            var m = Monitor(left: 1920);
            var g = RecordingCommandBuilder.Geometry(s, m.Width, m.Height);
            string dda = RecordingCommandBuilder.SegmentArgs(s, m, g, CaptureMethod.Ddagrab, "h264_qsv", "p.mkv");
            TestSuite.Equal(true, dda.Contains("draw_mouse=0:offset_x=100:offset_y=50:video_size=800x600"));

            // O gdigrab usa coordenadas da área de trabalho virtual (monitor à direita começa em 1920).
            string gdi = RecordingCommandBuilder.SegmentArgs(s, m, g, CaptureMethod.Gdigrab, "libx264", "p.mkv");
            TestSuite.Equal(true, gdi.Contains("-offset_x 2020 -offset_y 50 -video_size 800x600 -i desktop"));
            TestSuite.Equal(true, gdi.Contains("-draw_mouse 0"));
        });

        suite.Run("Recording encoder names, quality and fixed bitrate arguments", () =>
        {
            TestSuite.Equal("hevc_amf", RecordingCommandBuilder.EncoderName("HEVC", "AMF"));
            TestSuite.Equal("av1_qsv", RecordingCommandBuilder.EncoderName("AV1", "QSV"));
            TestSuite.Equal("libsvtav1", RecordingCommandBuilder.EncoderName("AV1", "CPU"));
            TestSuite.Equal("h264_nvenc", RecordingCommandBuilder.EncoderName("H264", "NVENC"));
            TestSuite.Equal("QSV", RecordingCommandBuilder.VendorOf("hevc_qsv"));
            TestSuite.Equal("CPU", RecordingCommandBuilder.VendorOf("libx264"));
            TestSuite.SequenceEqual(new[] { "AMF", "NVENC", "QSV", "CPU" }, RecordingCommandBuilder.AutoVendorOrder(CaptureMonitor.VendorAmd));
            TestSuite.SequenceEqual(new[] { "NVENC", "AMF", "QSV", "CPU" }, RecordingCommandBuilder.AutoVendorOrder(0));

            string nv = string.Join(" ", RecordingCommandBuilder.EncoderArgs(new RecordingSettings { Quality = "High", Fps = 30 }, "h264_nvenc"));
            TestSuite.Equal(true, nv.Contains("-rc vbr -cq 23 -b:v 0"));
            TestSuite.Equal(true, nv.Contains("-g 60"));
            string x264 = string.Join(" ", RecordingCommandBuilder.EncoderArgs(new RecordingSettings { Quality = "Ultra" }, "libx264"));
            TestSuite.Equal(true, x264.Contains("-preset veryfast -crf 18"));
            string custom = string.Join(" ", RecordingCommandBuilder.EncoderArgs(new RecordingSettings { Quality = "Custom", CustomBitrateMbps = 20 }, "hevc_qsv"));
            TestSuite.Equal(true, custom.Contains("-b:v 20M -maxrate 30M -bufsize 40M"));
        });

        suite.Run("Recording final mux joins segments without re-encoding and mixes audio", () =>
        {
            var tracks = new[]
            {
                new AudioTrack(@"C:\t\system.pcm", "f32le", 48000, 2, 1.0),
                new AudioTrack(@"C:\t\mic.pcm", "s16le", 44100, 1, 1.5),
            };
            string two = RecordingCommandBuilder.FinalizeArgs(@"C:\t\parts.txt", tracks, 120, "HEVC", "mp4", @"C:\out\v.mp4");
            TestSuite.Equal(true, two.Contains("-f concat -safe 0 -i \"C:\\t\\parts.txt\""));
            TestSuite.Equal(true, two.Contains("-f f32le -ar 48000 -ac 2 -i \"C:\\t\\system.pcm\""));
            TestSuite.Equal(true, two.Contains("-f s16le -ar 44100 -ac 1 -i \"C:\\t\\mic.pcm\""));
            TestSuite.Equal(true, two.Contains("[2:a]volume=1.5,adelay=delays=120:all=1[a1]"));
            TestSuite.Equal(true, two.Contains("amix=inputs=2:duration=longest:normalize=0,apad[aout]"));
            TestSuite.Equal(true, two.Contains("-c:v copy -tag:v hvc1 -movflags +faststart"));
            TestSuite.Equal(true, two.Contains("-shortest"));

            string early = RecordingCommandBuilder.FinalizeArgs("l.txt", tracks.Take(1).ToList(), -250, "H264", "mkv", "v.mkv");
            TestSuite.Equal(true, early.Contains("atrim=start=0.25,asetpts=PTS-STARTPTS"));
            TestSuite.Equal(true, early.Contains("[a0]apad[aout]"));
            TestSuite.Equal(false, early.Contains("faststart"));

            string silent = RecordingCommandBuilder.FinalizeArgs("l.txt", Array.Empty<AudioTrack>(), 0, "H264", "mp4", "v.mp4");
            TestSuite.Equal(false, silent.Contains("aac"));

            TestSuite.Equal("file 'C:/a/it'\\''s.mkv'\nfile 'C:/b.mkv'\n", RecordingCommandBuilder.ConcatList(new[] { @"C:\a\it's.mkv", @"C:\b.mkv" }));
            TestSuite.Equal("Pulse1x_2026-11-12_21-05-03.mp4", RecordingCommandBuilder.FileName(new DateTime(2026, 11, 12, 21, 5, 3), "mp4"));
            TestSuite.Equal(true, RecordingCommandBuilder.IsRecordingFile(@"C:\x\Pulse1x_2026.MKV"));
            TestSuite.Equal(false, RecordingCommandBuilder.IsRecordingFile(@"C:\x\video.mp4"));
            TestSuite.Equal(TimeSpan.FromSeconds(83.5), RecordingLibrary.ParseDuration("  Duration: 00:01:23.50, start: 0.000000"));
        });

        suite.Run("Recording hotkeys display and settings survive a save round-trip", () =>
        {
            var hk = new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x52);
            TestSuite.Equal("Ctrl + Alt + R", hk.Display());
            TestSuite.Equal("Shift + F9", new HotkeyBinding(HotkeyBinding.ModShift, 0x78).Display());
            TestSuite.Equal("—", new HotkeyBinding().Display());
            TestSuite.Equal(true, hk.SameAs(hk.Clone()));
            TestSuite.Equal(false, new HotkeyBinding().SameAs(new HotkeyBinding()));

            string dir = Path.Combine(Path.GetTempPath(), "pulse1x-rec-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string file = Path.Combine(dir, "settings.json");
                var service = new SettingsService(file);
                service.Current.Recording.Fps = 120;
                service.Current.Recording.Codec = "AV1";
                service.Current.Recording.PauseHotkey = new HotkeyBinding(HotkeyBinding.ModWin, 0x7A);
                service.Save();

                var reloaded = new SettingsService(file).Current.Recording;
                TestSuite.Equal(120, reloaded.Fps);
                TestSuite.Equal("AV1", reloaded.Codec);
                TestSuite.Equal("Win + F11", reloaded.PauseHotkey.Display());

                // Valores inválidos (arquivo editado à mão) voltam ao padrão.
                File.WriteAllText(file, "{\"Recording\":{\"Fps\":7,\"Codec\":\"VP9\",\"Quality\":\"x\",\"CustomBitrateMbps\":9999,\"StartHotkey\":null}}");
                var fixedUp = new SettingsService(file).Current.Recording;
                TestSuite.Equal(60, fixedUp.Fps);
                TestSuite.Equal("H264", fixedUp.Codec);
                TestSuite.Equal("High", fixedUp.Quality);
                TestSuite.Equal(150, fixedUp.CustomBitrateMbps);
                TestSuite.Equal(true, fixedUp.StartHotkey.IsEmpty);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });

        suite.Run("ffmpeg behind a symbolic link (winget) resolves to the real file", () =>
        {
            // Regressão da 1.22.0: o Pulse1x elevado não consegue executar pelo link do winget
            // ("ponto de montagem não confiável"); precisa executar o .exe final.
            string dir = Path.Combine(Path.GetTempPath(), "pulse1x-link-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string real = Path.Combine(dir, "real-ffmpeg.exe");
                File.WriteAllText(real, "x");
                TestSuite.Equal(real, FfmpegService.ResolveRealFile(real));
                TestSuite.Equal<string?>(null, FfmpegService.ResolveRealFile(Path.Combine(dir, "missing.exe")));

                string link = Path.Combine(dir, "ffmpeg.exe");
                try { File.CreateSymbolicLink(link, real); }
                catch (IOException) { Console.WriteLine("    (symlink not allowed here; link case skipped)"); return; }
                catch (UnauthorizedAccessException) { Console.WriteLine("    (symlink not allowed here; link case skipped)"); return; }
                TestSuite.Equal(real, FfmpegService.ResolveRealFile(link));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });

        suite.Run("Recording screens only use icons that exist in WPF-UI", () =>
        {
            // Um nome de ícone errado só estoura ao abrir a tela (o XAML é lido em tempo de execução).
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "Pulse1x.sln"))) root = Path.GetDirectoryName(root)!;
            string app = Path.Combine(root, "src", "Pulse1x.App");
            var files = new[] { Path.Combine(app, "Views", "RecordingPage.xaml"), Path.Combine(app, "Views", "Recording", "RecordingToolbarWindow.xaml"), Path.Combine(app, "MainWindow.xaml") };
            var used = files.SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),
                    @"(?:\{ui:SymbolIcon\s+|Symbol="")([A-Za-z0-9]+)").Select(m => m.Groups[1].Value)).Distinct().ToList();
            var bad = used.Where(n => !Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(n, out _)).ToList();
            if (bad.Count > 0) throw new Exception("Unknown icons: " + string.Join(", ", bad));
            TestSuite.Equal(true, used.Count > 15);
        });

        suite.Run("Every Recording text exists in both languages", () =>
        {
            var keys = new HashSet<string>();
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "Pulse1x.sln"))) root = Path.GetDirectoryName(root)!;
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Pulse1x.App"), "*.*", SearchOption.AllDirectories)
                         .Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml")) && !p.Contains("LocalizationStrings")
                                     && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                             File.ReadAllText(file), @"[""\[]((?:Rec|RecInfo)_[A-Za-z0-9_]+)[""\]]"))
                    keys.Add(m.Groups[1].Value);
            keys.Add("Nav_Recording");
            // InfoKey do selo de segurança: os textos são "<chave>_Pros" e "<chave>_Cons".
            foreach (var info in keys.Where(k => k.StartsWith("RecInfo_") && !k.EndsWith("_Pros") && !k.EndsWith("_Cons")).ToList())
            {
                keys.Remove(info);
                keys.Add(info + "_Pros");
                keys.Add(info + "_Cons");
            }
            var missing = keys.Where(k => !Pulse1x.App.Localization.LocalizationStrings.Pt.ContainsKey(k)
                                          || !Pulse1x.App.Localization.LocalizationStrings.En.ContainsKey(k)).OrderBy(k => k).ToList();
            if (missing.Count > 0) throw new Exception("Missing: " + string.Join(", ", missing));
            TestSuite.Equal(true, keys.Count > 80);
        });
    }

    /// <summary>
    /// Renderiza a página Gravação e o menu flutuante reais em PNG (artifacts/recording-verification).
    /// Cria a própria Application, então roda sozinho: --ui-recording (sem --ui).
    /// </summary>
    public static void RenderUi(TestSuite suite)
    {
        suite.Run("UI: Recording page and floating menu render without binding errors", () =>
        {
            var application = new System.Windows.Application();
            application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
            application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            Pulse1x.App.Services.AnimationSettings.Enabled = false;

            var bindingErrors = new List<string>();
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener(bindingErrors));
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;

            string artifacts = Path.GetFullPath("artifacts/recording-verification");
            Directory.CreateDirectory(artifacts);
            var settings = new SettingsService(Path.Combine(artifacts, "settings.json"));
            settings.Current.Recording.OutputFolder = Path.Combine(artifacts, "videos");
            // Dois vídeos de exemplo para a lista "Gravações recentes" mostrar miniatura e duração.
            Directory.CreateDirectory(settings.Current.Recording.OutputFolder);
            var probeFfmpeg = new FfmpegService(() => null);
            if (probeFfmpeg.Locate())
                foreach (var (name, src) in new[] { ("Pulse1x_2026-11-12_10-00-00.mp4", "testsrc2"), ("Pulse1x_2026-11-12_11-30-00.mp4", "mandelbrot") })
                {
                    string target = Path.Combine(settings.Current.Recording.OutputFolder, name);
                    if (!File.Exists(target))
                        FfmpegService.RunAsync(probeFfmpeg.ExePath!, $"-y -f lavfi -i {src}=s=640x360:r=30 -t 3 -pix_fmt yuv420p \"{target}\"", TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                }
            string logName = $"test-rec-{Guid.NewGuid():N}.json";
            var advanced = new AdvancedOptimizationService(new OptimizationChangeLog(logName));
            using var vm = new Pulse1x.App.ViewModels.RecordingViewModel(settings, advanced);

            var page = new Pulse1x.App.Views.RecordingPage(vm);
            var window = new System.Windows.Window
            {
                Width = 1180, Height = 3100, Left = -30000, Top = 0, ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = System.Windows.WindowStyle.None,
                Background = (System.Windows.Media.Brush)application.Resources["ApplicationBackgroundBrush"],
                Content = page,
            };
            window.Show();
            var until = DateTime.UtcNow.AddSeconds(8); // dá tempo de achar o ffmpeg e testar os encoders
            while (DateTime.UtcNow < until) Pump(100);
            Console.WriteLine($"    encoders before save: {vm.Encoders.Count} [{string.Join(" | ", vm.Encoders.Select(e => e.Name))}] selected={vm.SelectedEncoder?.Name} hint={vm.EncoderHint}");
            page.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));
            Pump(100);
            Save(page, Path.Combine(artifacts, "recording-page.png"));
            ((System.Windows.Controls.ScrollViewer)page.Content).ScrollToBottom();
            Pump(300);
            Save(page, Path.Combine(artifacts, "recording-page-bottom.png"));

            // Barra flutuante com as configurações rápidas abertas, renderizada fora da tela.
            var monitor = DisplayEnumerator.GetMonitors().First();
            var toolbar = new Pulse1x.App.Views.Recording.RecordingToolbarWindow(vm, settings.Current.Recording, () => { }, monitor);
            ((System.Windows.Controls.Primitives.ToggleButton)toolbar.FindName("SettingsToggle")).IsChecked = true;
            var root = (System.Windows.UIElement)toolbar.Content;
            toolbar.Content = null;
            var host = new System.Windows.Window
            {
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight, Left = -30000, Top = 0, ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = System.Windows.WindowStyle.None, Background = System.Windows.Media.Brushes.DimGray,
                DataContext = vm, Content = root, Foreground = System.Windows.Media.Brushes.White,
            };
            host.Resources.MergedDictionaries.Add(toolbar.Resources);
            host.Show();
            Pump(500);
            Save(host, Path.Combine(artifacts, "recording-toolbar.png"));
            host.Close();
            toolbar.Close();

            var toast = new Pulse1x.App.Views.Recording.RecordingToastWindow(monitor, "Gravação salva", "Pulse1x_2026-11-12_21-05-03.mp4", false, () => { }, TimeSpan.FromSeconds(30));
            // Barra real: abre e fecha as configurações rápidas e confere que a janela acompanha.
            var realBar = new Pulse1x.App.Views.Recording.RecordingToolbarWindow(vm, settings.Current.Recording, () => { }, monitor);
            realBar.Show();
            Pump(400);
            double closedHeight = realBar.ActualHeight;
            ((System.Windows.Controls.Primitives.ToggleButton)realBar.FindName("SettingsToggle")).IsChecked = true;
            Pump(400);
            double openHeight = realBar.ActualHeight;
            Console.WriteLine($"    toolbar height closed {closedHeight:0}, open {openHeight:0}");
            TestSuite.Equal(true, closedHeight < 70 && openHeight > closedHeight + 150);
            realBar.Close();

            // A janela real (aparece por um instante no canto da tela): confere o tamanho de verdade.
            toast.Show();
            Pump(400);
            Console.WriteLine($"    toast window: {toast.ActualWidth:0}x{toast.ActualHeight:0}");
            TestSuite.Equal(true, toast.ActualHeight < 160 && toast.ActualWidth < 480);
            Save((System.Windows.FrameworkElement)toast.Content, Path.Combine(artifacts, "recording-toast.png"));
            toast.Close();
            window.Close();
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", logName)); } catch { }

            Console.WriteLine($"    engine: {vm.EngineStatus}");
            Console.WriteLine($"    encoders: {string.Join(" | ", vm.Encoders.Select(e => e.Name))}");
            if (bindingErrors.Count > 0) throw new Exception("Binding errors:\n" + string.Join("\n", bindingErrors.Distinct()));
        });
    }

    private sealed class BindingErrorListener : System.Diagnostics.TraceListener
    {
        private readonly List<string> _errors;
        public BindingErrorListener(List<string> errors) => _errors = errors;
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            // Erros de binding do próprio WPF-UI (templates internos) não interessam aqui.
            if (message is not null && (message.Contains("RecordingViewModel") || message.Contains("RecOption") || message.Contains("RecordingItemViewModel")))
                lock (_errors) _errors.Add(message);
        }
    }

    private static void Save(System.Windows.FrameworkElement element, string file)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
        Console.WriteLine($"    screenshot: {file}");
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Grava de verdade (precisa de ffmpeg, monitor e placa de vídeo): 2 s, pausa, 2 s, termina.
    /// Fica fora da suíte padrão; rode com --recording.
    /// </summary>
    public static void RegisterLive(TestSuite suite)
    {
        suite.Run("LIVE: records, pauses, resumes and saves a playable video with audio", () =>
        {
            var ffmpeg = new FfmpegService(() => null);
            if (!ffmpeg.Locate()) throw new Exception("ffmpeg not found");
            ffmpeg.ProbeAsync().GetAwaiter().GetResult();
            var monitor = DisplayEnumerator.GetMonitors().First(m => m.IsPrimary);
            string dir = Path.Combine(Path.GetTempPath(), "pulse1x-live-" + Guid.NewGuid().ToString("N"));
            var settings = new RecordingSettings { OutputFolder = dir, Fps = 30, RecordSystemAudio = true, Codec = "H264", Resolution = "720" };
            var encoders = new List<string>();
            foreach (var v in RecordingCommandBuilder.AutoVendorOrder(monitor.VendorId))
            {
                string enc = RecordingCommandBuilder.EncoderName("H264", v);
                if (ffmpeg.EncoderWorksAsync(enc).GetAwaiter().GetResult()) encoders.Add(enc);
            }
            using var recorder = new ScreenRecorder();
            recorder.StartAsync(new RecordingPlan(ffmpeg.ExePath!, settings, monitor, ffmpeg.HasDdagrab, encoders)).GetAwaiter().GetResult();
            Console.WriteLine($"    encoder: {recorder.ActiveEncoder} via {recorder.ActiveMethod}, fallback={recorder.UsedFallback}");
            Thread.Sleep(2000);
            recorder.PauseAsync().GetAwaiter().GetResult();
            TestSuite.Equal(RecorderState.Paused, recorder.State);
            Thread.Sleep(1500);
            recorder.ResumeAsync().GetAwaiter().GetResult();
            Thread.Sleep(2000);
            var shown = recorder.Elapsed;
            string path = recorder.StopAsync().GetAwaiter().GetResult();

            var probe = FfmpegService.RunAsync(ffmpeg.ExePath!, $"-hide_banner -i \"{path}\"", TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            var duration = RecordingLibrary.ParseDuration(probe.Output) ?? TimeSpan.Zero;
            Console.WriteLine($"    file: {path} ({new FileInfo(path).Length / 1024} KB), duration {duration.TotalSeconds:0.00}s, timer {shown.TotalSeconds:0.00}s");
            TestSuite.Equal(true, probe.Output.Contains("Video: h264"));
            TestSuite.Equal(true, probe.Output.Contains("1280x720"));
            TestSuite.Equal(true, probe.Output.Contains("Audio: aac"));
            // A pausa de 1,5 s fica de fora, e o tempo mostrado na tela bate com o vídeo.
            TestSuite.Equal(true, duration.TotalSeconds is > 3.5 and < 7.5);
            TestSuite.Equal(true, Math.Abs((duration - shown).TotalSeconds) < 0.7);
            TestSuite.Equal(false, Directory.EnumerateDirectories(dir, ".pulse1x-rec-*").Any());
            try { Directory.Delete(dir, true); } catch { }
        });
    }
}
