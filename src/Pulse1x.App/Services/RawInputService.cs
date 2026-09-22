using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace Pulse1x.App.Services;

public enum RawInputKind { Mouse, Keyboard }

public sealed record RawInputSample(
    RawInputKind Kind,
    double TimestampMilliseconds,
    string DeviceName,
    ushort VirtualKey = 0,
    ushort ScanCode = 0,
    bool IsKeyUp = false,
    int DeltaX = 0,
    int DeltaY = 0,
    ushort MouseButtonFlags = 0);

/// <summary>
/// Captura teclado e mouse diretamente por WM_INPUT. O serviço também drena eventos acumulados
/// para não perder amostras de mouses de alta frequência quando a fila da interface fica ocupada.
/// </summary>
public sealed class RawInputService : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const uint RidevRemove = 0x00000001;

    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private HwndSource? _source;
    private IntPtr _buffer;
    private int _bufferSize = 64 * 64;
    private double? _lastBatchTimestamp;
    private bool _active;

    public event Action<IReadOnlyList<RawInputSample>>? SamplesReceived;

    public bool Start(Window window)
    {
        if (_active) return true;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;

        var devices = new[]
        {
            new RawInputDevice(0x01, 0x02, 0, handle),
            new RawInputDevice(0x01, 0x06, 0, handle),
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            return false;

        _buffer = Marshal.AllocHGlobal(_bufferSize);
        _source = HwndSource.FromHwnd(handle);
        if (_source is null)
        {
            UnregisterDevices();
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            return false;
        }

        _source.AddHook(WindowHook);
        _active = true;
        return true;
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        _source?.RemoveHook(WindowHook);
        _source = null;

        UnregisterDevices();
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
        _deviceNames.Clear();
        _lastBatchTimestamp = null;
    }

    private static void UnregisterDevices()
    {
        var devices = new[]
        {
            new RawInputDevice(0x01, 0x02, RidevRemove, IntPtr.Zero),
            new RawInputDevice(0x01, 0x06, RidevRemove, IntPtr.Zero),
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_active || message != WmInput) return IntPtr.Zero;

        var pending = new List<PendingSample>(16);
        ReadSingle(lParam, pending);
        DrainBuffer(pending);
        if (pending.Count == 0) return IntPtr.Zero;

        double now = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
        var timestamps = SpreadTimestamps(_lastBatchTimestamp, now, pending.Count);
        _lastBatchTimestamp = now;
        var samples = new RawInputSample[pending.Count];
        for (int i = 0; i < pending.Count; i++) samples[i] = pending[i].WithTimestamp(timestamps[i]);
        SamplesReceived?.Invoke(samples);

        // handled fica falso para o WPF chamar DefWindowProc, exigido para a limpeza de WM_INPUT.
        return IntPtr.Zero;
    }

    private void ReadSingle(IntPtr rawInputHandle, List<PendingSample> samples)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return;
        EnsureBuffer((int)size);
        if (GetRawInputData(rawInputHandle, RidInput, _buffer, ref size, headerSize) == uint.MaxValue)
            return;
        ReadBlock(_buffer, samples);
    }

    private void DrainBuffer(List<PendingSample> samples)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        while (_active)
        {
            uint size = (uint)_bufferSize;
            uint count = GetRawInputBuffer(_buffer, ref size, headerSize);
            if (count == 0) return;
            if (count == uint.MaxValue)
            {
                if (size <= _bufferSize) return;
                EnsureBuffer((int)size);
                continue;
            }

            IntPtr current = _buffer;
            for (uint i = 0; i < count; i++)
            {
                var header = Marshal.PtrToStructure<RawInputHeader>(current);
                ReadBlock(current, samples);
                int alignment = IntPtr.Size;
                int blockSize = ((int)header.Size + alignment - 1) & ~(alignment - 1);
                current = IntPtr.Add(current, blockSize);
            }
        }
    }

    private void ReadBlock(IntPtr block, List<PendingSample> samples)
    {
        var header = Marshal.PtrToStructure<RawInputHeader>(block);
        IntPtr data = IntPtr.Add(block, Marshal.SizeOf<RawInputHeader>());
        string deviceName = ResolveDeviceName(header.Device, header.Type);

        if (header.Type == RimTypeMouse)
        {
            var mouse = Marshal.PtrToStructure<RawMouse>(data);
            samples.Add(PendingSample.Mouse(deviceName, mouse.LastX, mouse.LastY, mouse.Buttons.ButtonFlags));
        }
        else if (header.Type == RimTypeKeyboard)
        {
            var keyboard = Marshal.PtrToStructure<RawKeyboard>(data);
            bool keyUp = (keyboard.Flags & 0x0001) != 0;
            samples.Add(PendingSample.Keyboard(deviceName, keyboard.VirtualKey, keyboard.MakeCode, keyUp));
        }
    }

    private string ResolveDeviceName(IntPtr device, uint type)
    {
        if (_deviceNames.TryGetValue(device, out string? cached)) return cached;
        string fallback = type == RimTypeMouse ? "Mouse HID" : "Teclado HID";
        uint length = 0;
        _ = GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length);
        if (length == 0) return _deviceNames[device] = fallback;

        var path = new StringBuilder((int)length);
        if (GetRawInputDeviceInfo(device, RidiDeviceName, path, ref length) == uint.MaxValue)
            return _deviceNames[device] = fallback;

        string? product = ReadHidProduct(path.ToString());
        return _deviceNames[device] = string.IsNullOrWhiteSpace(product) ? fallback : product.Trim();
    }

    private static string? ReadHidProduct(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, 0x00000001 | 0x00000002, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var product = new StringBuilder(256);
        return HidD_GetProductString(handle, product, product.Capacity * sizeof(char)) ? product.ToString() : null;
    }

    private void EnsureBuffer(int required)
    {
        if (_buffer != IntPtr.Zero && required <= _bufferSize) return;
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _bufferSize = Math.Max(required, _bufferSize * 2);
        _buffer = Marshal.AllocHGlobal(_bufferSize);
    }

    internal static double[] SpreadTimestamps(double? previous, double now, int count)
    {
        if (count <= 0) return Array.Empty<double>();
        var result = new double[count];
        if (previous is not double start || now <= start || count == 1)
        {
            Array.Fill(result, now);
            return result;
        }

        double step = (now - start) / count;
        for (int i = 0; i < count; i++) result[i] = start + step * (i + 1);
        return result;
    }

    public void Dispose() => Stop();

    private sealed record PendingSample(
        RawInputKind Kind, string DeviceName, ushort VirtualKey, ushort ScanCode, bool IsKeyUp,
        int DeltaX, int DeltaY, ushort MouseButtonFlags)
    {
        internal static PendingSample Mouse(string name, int x, int y, ushort buttons) =>
            new(RawInputKind.Mouse, name, 0, 0, false, x, y, buttons);
        internal static PendingSample Keyboard(string name, ushort key, ushort scan, bool up) =>
            new(RawInputKind.Keyboard, name, key, scan, up, 0, 0, 0);
        internal RawInputSample WithTimestamp(double timestamp) =>
            new(Kind, timestamp, DeviceName, VirtualKey, ScanCode, IsKeyUp, DeltaX, DeltaY, MouseButtonFlags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal IntPtr Target;

        internal RawInputDevice(ushort usagePage, ushort usage, uint flags, IntPtr target)
        {
            UsagePage = usagePage;
            Usage = usage;
            Flags = flags;
            Target = target;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal IntPtr Device;
        internal IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouseButtons
    {
        [FieldOffset(0)] internal uint Value;
        [FieldOffset(0)] internal ushort ButtonFlags;
        [FieldOffset(2)] internal ushort ButtonData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        internal ushort Flags;
        internal RawMouseButtons Buttons;
        internal uint RawButtons;
        internal int LastX;
        internal int LastY;
        internal uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        internal ushort MakeCode;
        internal ushort Flags;
        internal ushort Reserved;
        internal ushort VirtualKey;
        internal uint Message;
        internal uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices, uint deviceCount, uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputBuffer(IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device, uint command, StringBuilder? data, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle hidDeviceObject, StringBuilder buffer, int bufferLength);
}
