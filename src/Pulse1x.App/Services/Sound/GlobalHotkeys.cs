using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Atalhos globais Ctrl+Alt+1 a Ctrl+Alt+9 da mesa de sons: funcionam com o Pulse1x minimizado, na
/// bandeja ou atrás de um jogo. Usa uma janela só de mensagens (HWND_MESSAGE) e RegisterHotKey; um
/// atalho que outro programa já usa simplesmente não é registrado.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    public const int Count = 9;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private const int IdBase = 0x5100;

    private readonly HwndSource _source;
    private readonly bool[] _registered = new bool[Count];

    /// <summary>Disparado (na thread de UI) com o índice 0–8 do atalho pressionado.</summary>
    public event Action<int>? Pressed;

    public GlobalHotkeys()
    {
        var parameters = new HwndSourceParameters("Pulse1xSoundboardHotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Liga ou desliga os nove atalhos. Devolve quantos o Windows aceitou.</summary>
    public int SetEnabled(bool enabled)
    {
        int ok = 0;
        for (int i = 0; i < Count; i++)
        {
            if (_registered[i]) { UnregisterHotKey(_source.Handle, IdBase + i); _registered[i] = false; }
            if (!enabled) continue;
            _registered[i] = RegisterHotKey(_source.Handle, IdBase + i, ModControl | ModAlt | ModNoRepeat, (uint)('1' + i));
            if (_registered[i]) ok++;
        }
        return ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int index = wParam.ToInt32() - IdBase;
            if (index is >= 0 and < Count)
            {
                Pressed?.Invoke(index);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        SetEnabled(false);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
