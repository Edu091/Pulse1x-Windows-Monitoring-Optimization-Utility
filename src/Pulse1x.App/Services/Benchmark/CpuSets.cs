using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Núcleos de desempenho em CPUs híbridas (Intel 12ª geração+, alguns Ryzen móveis). Com uma thread
/// só, o Windows ora a coloca num núcleo P, ora num núcleo E (~20-25% mais lento), e o single-thread
/// variava de 716 a 865 pontos na mesma máquina. Os testes de uma thread ficam presos aos núcleos da
/// classe de eficiência mais alta, que é o que um jogo em primeiro plano recebe.
/// </summary>
internal static class CpuSets
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadSelectedCpuSets(IntPtr thread, uint[] cpuSetIds, uint cpuSetIdCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    private static uint[]? _performanceCores;
    private static bool _read;

    /// <summary>IDs dos CPU sets da classe de eficiência mais alta; null em CPUs não híbridas.</summary>
    public static uint[]? PerformanceCores
    {
        get
        {
            if (!_read)
            {
                _performanceCores = Read();
                _read = true;
            }
            return _performanceCores;
        }
    }

    /// <summary>Prende a thread atual aos núcleos de desempenho (nada acontece em CPUs não híbridas).</summary>
    public static void PinCurrentThreadToPerformanceCores()
    {
        var ids = PerformanceCores;
        if (ids is null) return;
        try { SetThreadSelectedCpuSets(GetCurrentThread(), ids, (uint)ids.Length); }
        catch { /* Windows antigo: segue sem fixar */ }
    }

    private static uint[]? Read()
    {
        try
        {
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint length, IntPtr.Zero, 0);
            if (length == 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0)) return null;
                var sets = new List<(uint Id, byte Efficiency)>();
                int offset = 0;
                while (offset < length)
                {
                    // SYSTEM_CPU_SET_INFORMATION: Size (4), Type (4), Id (4), Group (2),
                    // LogicalProcessorIndex, CoreIndex, LastLevelCacheIndex, NumaNodeIndex,
                    // EfficiencyClass (1 byte cada, offset 18)...
                    int size = Marshal.ReadInt32(buffer, offset);
                    if (size <= 0) break;
                    int type = Marshal.ReadInt32(buffer, offset + 4);
                    if (type == 0) // CpuSetInformation
                        sets.Add(((uint)Marshal.ReadInt32(buffer, offset + 8), Marshal.ReadByte(buffer, offset + 18)));
                    offset += size;
                }
                if (sets.Count == 0) return null;
                byte best = sets.Max(s => s.Efficiency);
                if (sets.All(s => s.Efficiency == best)) return null; // CPU não híbrida
                return sets.Where(s => s.Efficiency == best).Select(s => s.Id).ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { return null; }
    }
}
