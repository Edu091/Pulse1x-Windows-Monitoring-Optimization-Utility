using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pulse1x.App.Services.Recording;

public enum RecordingHotkeyAction { Start, Stop, StartStop, PauseResume, Overlay }

/// <summary>
/// Atalhos globais da Gravação (funcionam com o Pulse1x minimizado, na bandeja ou atrás de um jogo).
/// Janela só de mensagens + RegisterHotKey, como na mesa de sons. Iniciar e Terminar podem usar a
/// mesma tecla: aí ela alterna.
/// </summary>
public sealed class RecordingHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int IdBase = 0x5200;

    private readonly HwndSource _source;
    private readonly Dictionary<int, RecordingHotkeyAction> _registered = new();

    /// <summary>Disparado na thread de UI.</summary>
    public event Action<RecordingHotkeyAction>? Pressed;

    public RecordingHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("Pulse1xRecordingHotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registra os atalhos. Devolve as ações cujo atalho o Windows recusou (já usado por outro programa).
    /// </summary>
    public List<RecordingHotkeyAction> Apply(RecordingSettings s)
    {
        UnregisterAll();
        var failed = new List<RecordingHotkeyAction>();
        if (!s.HotkeysEnabled) return failed;

        var wanted = new List<(HotkeyBinding Binding, RecordingHotkeyAction Action, RecordingHotkeyAction Reported)>();
        if (s.StartHotkey.SameAs(s.StopHotkey))
            wanted.Add((s.StartHotkey, RecordingHotkeyAction.StartStop, RecordingHotkeyAction.Start));
        else
        {
            wanted.Add((s.StartHotkey, RecordingHotkeyAction.Start, RecordingHotkeyAction.Start));
            wanted.Add((s.StopHotkey, RecordingHotkeyAction.Stop, RecordingHotkeyAction.Stop));
        }
        wanted.Add((s.PauseHotkey, RecordingHotkeyAction.PauseResume, RecordingHotkeyAction.PauseResume));
        wanted.Add((s.OverlayHotkey, RecordingHotkeyAction.Overlay, RecordingHotkeyAction.Overlay));

        int id = IdBase;
        var used = new List<HotkeyBinding>();
        foreach (var (binding, action, reported) in wanted)
        {
            if (binding.IsEmpty) continue;
            // Mesma tecla em duas ações (fora o caso Iniciar = Terminar): a segunda não é registrada.
            if (used.Any(u => u.SameAs(binding))) { failed.Add(reported); continue; }
            used.Add(binding);
            if (RegisterHotKey(_source.Handle, id, binding.Modifiers | ModNoRepeat, binding.Key))
                _registered[id] = action;
            else
                failed.Add(reported);
            id++;
        }
        return failed;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            Pressed?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
