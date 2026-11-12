using System.Windows;
using System.Windows.Input;
using Pulse1x.App.Localization;
using Pulse1x.App.Services.Recording;

namespace Pulse1x.App.Controls;

/// <summary>
/// Botão que grava um atalho: clique, pressione a combinação (ex.: Ctrl + Alt + R) e pronto.
/// Esc cancela; Backspace ou Delete deixa o atalho sem tecla. Exige um modificador, a não ser
/// nas teclas F1–F24, Pause, ScrollLock e PrintScreen, que dificilmente atrapalham a digitação.
/// </summary>
public class HotkeyBox : Wpf.Ui.Controls.Button
{
    public static readonly DependencyProperty BindingProperty = DependencyProperty.Register(
        nameof(Binding), typeof(HotkeyBinding), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));

    public HotkeyBinding? Binding
    {
        get => (HotkeyBinding?)GetValue(BindingProperty);
        set => SetValue(BindingProperty, value);
    }

    /// <summary>Avisam quem registrou atalhos globais para soltá-los enquanto o usuário digita um novo.</summary>
    public static event Action? CaptureStarted;
    public static event Action? CaptureEnded;

    private bool _capturing;

    public HotkeyBox()
    {
        // Classe derivada não herda o estilo implícito do botão do WPF-UI: aponta para ele.
        SetResourceReference(StyleProperty, typeof(Wpf.Ui.Controls.Button));
        MinWidth = 150;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        Click += (_, _) => BeginCapture();
        LostKeyboardFocus += (_, _) => EndCapture();
        Loaded += (_, _) => { Loc.Instance.LanguageChanged += Refresh; Refresh(); };
        Unloaded += (_, _) => { Loc.Instance.LanguageChanged -= Refresh; EndCapture(); };
    }

    private void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        Focus();
        Keyboard.Focus(this);
        CaptureStarted?.Invoke();
        Refresh();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        CaptureEnded?.Invoke();
        Refresh();
    }

    private void Refresh() =>
        Content = _capturing ? Loc.S("Rec_HotkeyPress") : (Binding?.Display() ?? "—");

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return; // espera a tecla "de verdade"

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { EndCapture(); return; }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            Binding = new HotkeyBinding();
            EndCapture();
            return;
        }

        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotkeyBinding.ModControl;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotkeyBinding.ModAlt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotkeyBinding.ModShift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= HotkeyBinding.ModWin;

        bool standalone = key is >= Key.F1 and <= Key.F24 or Key.Pause or Key.Scroll or Key.Snapshot;
        if (mods == 0 && !standalone) return; // uma letra sozinha travaria a digitação no PC inteiro

        Binding = new HotkeyBinding(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        EndCapture();
    }
}
