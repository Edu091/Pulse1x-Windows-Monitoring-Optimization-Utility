using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Compatibility fallback when SDL cannot load. XInput cannot reveal a hidden physical controller's family.</summary>
public sealed class XInputProvider : IGamepadProvider, IControllerBackend
{
    private readonly Dictionary<int, Slot> _slots = new();
    private long _nextScan;
    private int _generation;
    private bool _disposed;
    public string Name => "XInput";
    public bool IsConnected => _slots.Count > 0;

    public GamepadSnapshot? Poll() => PollControllers().FirstOrDefault()?.Snapshot;
    IReadOnlyList<ControllerReading> IControllerBackend.PollControllers() => PollControllers();

    private IReadOnlyList<ControllerReading> PollControllers()
    {
        if (_disposed) return Array.Empty<ControllerReading>();
        var result = new List<ControllerReading>();
        bool scan = Environment.TickCount64 >= _nextScan;
        if (scan) _nextScan = Environment.TickCount64 + 1000;
        for (int index = 0; index < 4; index++)
        {
            if (!_slots.ContainsKey(index) && !scan) continue;
            if (!TryGetState(index, out var state))
            {
                _slots.Remove(index);
                continue;
            }
            if (!_slots.TryGetValue(index, out var slot))
            {
                slot = new Slot(new($"xinput:{index}:{++_generation}", $"Xbox controller {index + 1}",
                    Name, ControllerFamily.Xbox, ControllerButtonLabels.Xbox));
                _slots.Add(index, slot);
            }
            result.Add(new(slot.Identity, slot.Read(state.Gamepad)));
        }
        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        _slots.Clear();
    }

    private sealed class Slot(ControllerIdentity identity)
    {
        internal ControllerIdentity Identity { get; } = identity;
        private readonly ControllerAxisFilter _x = new();
        private readonly ControllerAxisFilter _y = new();
        private readonly ControllerTriggerFilter _leftTrigger = new();
        private readonly ControllerTriggerFilter _rightTrigger = new();

        internal GamepadSnapshot Read(XInputGamepad pad)
        {
            bool Button(ushort mask) => (pad.Buttons & mask) != 0;
            return new(Button(0x0001), Button(0x0002), Button(0x0004), Button(0x0008),
                Button(0x1000), Button(0x2000), Button(0x4000), Button(0x8000),
                Button(0x0100), Button(0x0200), _leftTrigger.Read(pad.LeftTrigger / 255f),
                _rightTrigger.Read(pad.RightTrigger / 255f), Button(0x0020), Button(0x0010),
                _x.Read(pad.ThumbLX / 32767f), _y.Read(pad.ThumbLY / 32767f));
        }
    }

    private static bool TryGetState(int index, out XInputState state)
    {
        try { return XInputGetState(index, out state) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            state = default;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int userIndex, out XInputState state);
}
