using System.Runtime.InteropServices;

namespace Pulse1x.App.Services.Benchmark;

/// <summary>
/// Tira o processo do EcoQoS ("modo de eficiência") do Windows 11 enquanto o benchmark roda. Sem
/// isso, basta a janela do Pulse1x ficar em segundo plano para o Windows mandar as threads para os
/// núcleos de eficiência e baixar clocks — a mesma máquina media até 20% a menos no multi-thread e
/// na GPU. Ao terminar, devolve a decisão ao Windows.
/// </summary>
public sealed class PowerThrottlingScope : IDisposable
{
    private const int ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref ProcessPowerThrottlingState info, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private readonly bool _applied;

    public PowerThrottlingScope()
    {
        // ControlMask = velocidade de execução, StateMask = 0: "nunca reduzir este processo".
        _applied = Set(ExecutionSpeed, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref ThreadPowerThrottlingState info, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    private const int ThreadPowerThrottling = 3;

    /// <summary>
    /// Marca a thread atual como "nunca reduzir velocidade" (QoS alta). Necessário nas threads de
    /// carga: sem isso, com a janela fora de foco, o Windows 11 em CPUs híbridas as manda para os
    /// núcleos de eficiência e o multi-thread media menos da metade.
    /// </summary>
    public static void OptOutCurrentThread()
    {
        try
        {
            var info = new ThreadPowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed, StateMask = 0 };
            SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref info, Marshal.SizeOf<ThreadPowerThrottlingState>());
        }
        catch { /* Windows antigo: sem o ajuste */ }
    }

    public void Dispose()
    {
        // ControlMask = 0: o Windows volta a decidir sozinho.
        if (_applied) Set(0, 0);
    }

    private static bool Set(uint control, uint state)
    {
        try
        {
            var info = new ProcessPowerThrottlingState { Version = 1, ControlMask = control, StateMask = state };
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref info, Marshal.SizeOf<ProcessPowerThrottlingState>());
        }
        catch { return false; } // Windows antigo sem a API: segue sem o ajuste
    }
}
