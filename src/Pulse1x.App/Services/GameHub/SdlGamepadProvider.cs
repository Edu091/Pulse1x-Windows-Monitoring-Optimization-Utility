using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// SDL's mapping database and HIDAPI drivers normalize Xbox, native DualShock/DualSense
/// USB/Bluetooth and mapped generic controllers. All native handles belong to the UI thread.
/// </summary>
internal sealed class SdlGamepadProvider : IControllerBackend
{
    private const uint InitGamepad = 0x00002000;
    private readonly Dictionary<uint, Device> _devices = new();
    private long _nextScan;
    private bool _initialized;
    private bool _disposed;
    internal string? Error { get; private set; }

    internal bool TryInitialize()
    {
        if (_disposed) return false;
        if (_initialized) return true;
        try
        {
            SdlNative.SDL_SetMainReady();
            SdlNative.SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
            SdlNative.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
            SdlNative.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
            // WPF owns the window, so SDL never receives a window-focus event.
            SdlNative.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (!SdlNative.SDL_InitSubSystem(InitGamepad))
            {
                Error = Marshal.PtrToStringUTF8(SdlNative.SDL_GetError());
                return false;
            }
            _initialized = true;
            var mappings = Path.Combine(AppContext.BaseDirectory, "gamecontrollerdb.txt");
            if (File.Exists(mappings) && SdlNative.SDL_AddGamepadMappingsFromFile(mappings) < 0)
                Debug.WriteLine($"SDL mapping file: {Marshal.PtrToStringUTF8(SdlNative.SDL_GetError())}");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = ex.Message;
            Dispose();
            return false;
        }
    }

    public IReadOnlyList<ControllerReading> PollControllers()
    {
        if (!_initialized || _disposed) return Array.Empty<ControllerReading>();
        // Pump on the WPF thread for Windows device notifications, then discard only controller
        // events: this service reads snapshots and must not leave an ever-growing SDL event queue.
        SdlNative.SDL_PumpEvents();
        SdlNative.SDL_FlushEvents(0x600, 0x6ff);
        foreach (var pair in _devices.ToArray())
        {
            if (SdlNative.SDL_GamepadConnected(pair.Value.Handle)) continue;
            SdlNative.SDL_CloseGamepad(pair.Value.Handle);
            _devices.Remove(pair.Key);
            _nextScan = 0;
        }
        if (Environment.TickCount64 >= _nextScan)
        {
            Scan();
            _nextScan = Environment.TickCount64 + 500;
        }
        return _devices.Values.Select(d => new ControllerReading(d.Identity, d.Read())).ToArray();
    }

    private void Scan()
    {
        var ids = SdlNative.SDL_GetGamepads(out int count);
        if (ids == IntPtr.Zero) return;
        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = unchecked((uint)Marshal.ReadInt32(ids, i * sizeof(uint)));
                if (_devices.ContainsKey(id)) continue;
                var handle = SdlNative.SDL_OpenGamepad(id);
                if (handle == IntPtr.Zero) continue;
                try { _devices.Add(id, new Device(id, handle)); }
                catch { SdlNative.SDL_CloseGamepad(handle); throw; }
            }
        }
        finally { SdlNative.SDL_free(ids); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var device in _devices.Values) SdlNative.SDL_CloseGamepad(device.Handle);
        _devices.Clear();
        if (_initialized) SdlNative.SDL_QuitSubSystem(InitGamepad);
        _initialized = false;
    }

    private sealed class Device
    {
        internal IntPtr Handle { get; }
        internal ControllerIdentity Identity { get; }
        private readonly ControllerAxisFilter _x = new();
        private readonly ControllerAxisFilter _y = new();
        private readonly ControllerTriggerFilter _leftTrigger = new();
        private readonly ControllerTriggerFilter _rightTrigger = new();

        internal Device(uint id, IntPtr handle)
        {
            Handle = handle;
            int type = SdlNative.SDL_GetGamepadType(handle);
            var family = type switch
            {
                2 or 3 => ControllerFamily.Xbox,
                4 or 5 or 6 => ControllerFamily.PlayStation,
                >= 7 and <= 11 => ControllerFamily.Nintendo,
                _ => ControllerFamily.Generic
            };
            var labels = family switch
            {
                ControllerFamily.Xbox => ControllerButtonLabels.Xbox,
                ControllerFamily.PlayStation => new("Cross", "Circle", "Square", "Triangle", "L1", "R1",
                    type == 4 ? "Select" : type == 5 ? "Share" : "Create", type == 4 ? "Start" : "Options", "L2"),
                ControllerFamily.Nintendo => new("B", "A", "Y", "X", "L", "R", "-", "+", "ZL"),
                _ => ControllerButtonLabels.Generic
            };
            labels = labels with
            {
                Accept = FaceLabel(0, labels.Accept), Back = FaceLabel(1, labels.Back),
                Favorite = FaceLabel(2, labels.Favorite), Search = FaceLabel(3, labels.Search)
            };
            Identity = new($"sdl:{id}", Marshal.PtrToStringUTF8(SdlNative.SDL_GetGamepadName(handle)) ?? "Gamepad",
                "SDL3", family, labels);
        }

        private string FaceLabel(int button, string fallback) => SdlNative.SDL_GetGamepadButtonLabel(Handle, button) switch
        {
            1 => "A", 2 => "B", 3 => "X", 4 => "Y",
            5 => "Cross", 6 => "Circle", 7 => "Square", 8 => "Triangle",
            _ => fallback
        };

        internal GamepadSnapshot Read()
        {
            bool Button(int button) => SdlNative.SDL_GetGamepadButton(Handle, button);
            float Axis(int axis) => Math.Clamp(SdlNative.SDL_GetGamepadAxis(Handle, axis) / 32767f, -1, 1);
            return new(Button(11), Button(12), Button(13), Button(14),
                Button(0), Button(1), Button(2), Button(3), Button(9), Button(10),
                _leftTrigger.Read(Axis(4)), _rightTrigger.Read(Axis(5)), Button(4), Button(6),
                _x.Read(Axis(0)), _y.Read(-Axis(1)));
        }
    }
}

/// <summary>Small SDL3 C ABI surface. SDL3 bool is one byte; SDL2 signatures are incompatible.</summary>
internal static class SdlNative
{
    private const string Library = "SDL3";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_SetMainReady();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_InitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_PumpEvents();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_FlushEvents(uint minType, uint maxType);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGamepads(out int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_free(IntPtr memory);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_OpenGamepad(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_CloseGamepad(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GamepadConnected(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetGamepadType(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_GetGamepadButtonLabel(IntPtr gamepad, int button);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GetGamepadButton(IntPtr gamepad, int button);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SDL_AddGamepadMappingsFromFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
}
