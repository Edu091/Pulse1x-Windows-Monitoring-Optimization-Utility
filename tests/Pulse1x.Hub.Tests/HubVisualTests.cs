using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Pulse1x.App.Models.GameHub;
using Pulse1x.App.Services;
using Pulse1x.App.Services.GameHub;
using Pulse1x.App.ViewModels.GameHub;
using Pulse1x.App.Views.GameHub;

namespace Pulse1x.Hub.Tests;

internal static class HubVisualTests
{
    internal static void Run(TestSuite suite)
    {
        // Host the real page without App.OnStartup (hardware, recovery and launchers).
        var application = new Application();
        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
            { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AnimationSettings.Enabled = false;
        var artifacts = Path.GetFullPath("artifacts/hub-verification");
        Directory.CreateDirectory(artifacts);
        var settings = new SettingsService();
        typeof(SettingsService).GetProperty(nameof(SettingsService.Current))!.SetValue(settings, new AppSettings());
        Set(settings, "_settingsFilePath", Path.Combine(artifacts, "settings.json"));
        var library = new GameLibraryService();
        Set(library, "_filePath", Path.Combine(artifacts, "library.json"));
        foreach (var game in library.Games)
        {
            game.Executable = "";
            game.Arguments = "";
            game.ProfileId = null;
            game.ArtLockedByUser = true;
        }
        var profiles = (ProfileStoreService)RuntimeHelpers.GetUninitializedObject(typeof(ProfileStoreService));
        Set(profiles, "_gate", new object());
        Set(profiles, "_data", new ProfileLibraryData());
        Set(profiles, "_filePath", Path.Combine(artifacts, "profiles.json"));
        var art = new GameArtService { OnlineEnabled = false };
        Set(art, "_artDir", artifacts);
        var gaming = new GamingModeService();
        var sessions = new GameSessionManager(null!, library, profiles, null!, gaming);
        using var gamepad = new GamepadService(new EmptyBackend(), () => Environment.TickCount64);
        var theme = new ThemeService(settings);
        theme.Apply();
        var sounds = new GameHubSoundService { Enabled = false };
        using var vm = new GameHubViewModel(library, profiles, art, sessions, gaming, theme, gamepad, sounds);
        int launches = 0;
        Set(vm, "playCommand", new AsyncRelayCommand(() => { launches++; return Task.CompletedTask; }));
        var page = new GameHubPage(vm, library, profiles, art, null!, null!, null!, null!, settings, null!);
        page.AttachGamepad(gamepad);
        var window = new Window { Content = page, Width = 1600, Height = 900, WindowStyle = WindowStyle.None,
            Left = 0, Top = 0, ShowInTaskbar = false, Title = "Pulse1x visual verification" };
        application.MainWindow = window;
        window.Show();
        Pump(1000);
        var list = (ListBox)page.FindName("LibraryList");
        var highlights = (ItemsControl)page.FindName("HighlightList");
        var play = (Button)page.FindName("PlayButton");
        if (list.Items.Count == 0) throw new InvalidOperationException("The optional local-art UI fixture needs a populated library.");
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var wide = Descendants<Button>(highlights).FirstOrDefault();
        suite.Run("UI: one red selector moves from library to highlight to action", () =>
        {
            first.Focus(); Pump();
            var ring = (Border)first.Template.FindName("Ring", first);
            var accent = (SolidColorBrush)page.FindResource("BrandAccentBrush");
            TestSuite.Equal(true, vm.GridHasFocus);
            TestSuite.Equal(accent.Color, ((SolidColorBrush)ring.BorderBrush).Color);
            if (wide is not null)
            {
                wide.Focus(); Pump();
                TestSuite.Equal(false, vm.GridHasFocus);
                TestSuite.Equal(false, ((SolidColorBrush)ring.BorderBrush).Color == accent.Color);
                var wideBorder = (Border)wide.Template.FindName("WideBd", wide);
                TestSuite.Equal(accent.Color, ((SolidColorBrush)wideBorder.BorderBrush).Color);
            }
            play.Focus(); Pump();
            TestSuite.Equal(false, vm.GridHasFocus);
            TestSuite.Equal(false, ((SolidColorBrush)ring.BorderBrush).Color == accent.Color);
            TestSuite.Equal<Style?>(null, first.FocusVisualStyle);
        });
        suite.Run("UI: controller double accept launches once in both card areas", () =>
        {
            first.Focus(); Pump(); launches = 0;
            Call(page, "OnGamepadAction", GamepadAction.Accept);
            TestSuite.Equal(0, launches);
            Call(page, "OnGamepadAction", GamepadAction.Accept);
            TestSuite.Equal(1, launches);
            if (wide is not null)
            {
                wide.Focus(); Pump();
                Call(page, "OnGamepadAction", GamepadAction.Accept);
                TestSuite.Equal(1, launches);
                Call(page, "OnGamepadAction", GamepadAction.Accept);
                TestSuite.Equal(2, launches);
            }
        });
        suite.Run("UI: mouse double click launches a card, never library whitespace", () =>
        {
            launches = 0;
            Call(page, "LibraryList_MouseDoubleClick", list,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = list });
            TestSuite.Equal(0, launches);
            Call(page, "LibraryList_MouseDoubleClick", list,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = first });
            TestSuite.Equal(1, launches);
            if (wide is not null)
            {
                Call(page, "WideCard_MouseDoubleClick", wide,
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
                TestSuite.Equal(2, launches);
            }
        });
        suite.Run("UI: PlayStation glyphs and profile modal follow controller focus", () =>
        {
            vm.ControllerGlyphs = "PlayStation";
            vm.GamepadConnected = true;
            TestSuite.Equal("\u00d7", vm.AcceptGlyph);
            TestSuite.Equal("\u25a1", vm.FavoriteGlyph);
            TestSuite.Equal("L2", vm.ActionsGlyph);
            vm.OpenProfilePickerCommand.Execute(null); Pump();
            TestSuite.Equal(false, vm.GridHasFocus);
            int before = launches;
            Call(page, "OnGamepadAction", GamepadAction.Back); Pump();
            TestSuite.Equal(false, vm.IsProfilePickerOpen);
            TestSuite.Equal(before, launches);
        });
        foreach (var size in new[] { (1600, 900), (1180, 780), (900, 600) })
        {
            suite.Run($"UI: render nonblank at {size.Item1}x{size.Item2}", () =>
            {
                window.Width = size.Item1; window.Height = size.Item2;
                first.Focus(); Pump(500);
                SaveImage(page, Path.Combine(artifacts, $"hub-{size.Item1}x{size.Item2}.png"));
                TestSuite.Equal(true, play.ActualWidth > 100 && play.ActualHeight >= 40);
                TestSuite.Equal(true, list.ActualWidth <= page.ActualWidth);
            });
        }
        page.DetachGamepad();
        window.Close();
        application.Shutdown();
    }

    private static void SaveImage(FrameworkElement element, string file)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int colorful = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] - pixels[i + 2]) > 35) colorful++;
        if (colorful < 1000) throw new InvalidOperationException("Rendered page has no visible game artwork.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file); encoder.Save(output);
        Console.WriteLine($"  Screenshot: {file}; artwork pixels: {colorful}");
    }

    private static void Pump(int milliseconds = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class EmptyBackend : IControllerBackend
    {
        public IReadOnlyList<ControllerReading> PollControllers() => Array.Empty<ControllerReading>();
        public void Dispose() { }
    }
}
