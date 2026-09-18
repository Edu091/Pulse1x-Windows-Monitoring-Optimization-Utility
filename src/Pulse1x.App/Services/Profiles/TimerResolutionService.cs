using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pulse1x.App.Services.Profiles;

/// <summary>
/// Controla a resolução do timer do sistema (o mesmo ajuste da categoria Latência, agora acionável
/// por perfil). Usa as funções do ntdll — as mesmas que jogos e players de mídia usam para pedir um
/// timer mais fino.
///
/// O pedido vale enquanto o processo que o fez continuar vivo, e o Pulse1x fica aberto durante toda
/// a sessão de jogo, então basta liberar o pedido (<see cref="Restore"/>) ao final para o Windows
/// voltar ao intervalo padrão. Nada é gravado em disco nem sobrevive a um reinício.
/// </summary>
public class TimerResolutionService
{
    // As funções do ntdll trabalham em unidades de 100 ns: 0,5 ms = 5000.
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    private bool _applied;

    /// <summary>Resolução em vigor agora, em milissegundos (null se não for possível ler).</summary>
    public double? CurrentMs
    {
        get
        {
            try
            {
                if (NtQueryTimerResolution(out _, out _, out uint current) != 0) return null;
                return current / 10000.0;
            }
            catch { return null; }
        }
    }

    /// <summary>Menor intervalo que esta máquina aceita, em ms (normalmente 0,5).</summary>
    public double? MinimumMs
    {
        get
        {
            try
            {
                if (NtQueryTimerResolution(out _, out uint maximum, out _) != 0) return null;
                // "maximumResolution" na API é o MENOR intervalo (maior precisão).
                return maximum / 10000.0;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Pede ao Windows o intervalo indicado (em ms). O valor é limitado ao que a máquina suporta,
    /// então pedir 0,5 numa máquina que só chega a 1 ms simplesmente resulta em 1 ms.
    /// </summary>
    public bool Apply(double milliseconds)
    {
        try
        {
            uint desired = (uint)Math.Round(milliseconds * 10000.0);
            if (desired == 0) return false;
            if (NtSetTimerResolution(desired, true, out _) != 0) return false;
            _applied = true;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Libera o pedido do Pulse1x e devolve o timer ao padrão do sistema.</summary>
    public void Restore()
    {
        if (!_applied) return;
        try
        {
            NtQueryTimerResolution(out uint minimum, out _, out _);
            NtSetTimerResolution(minimum, false, out _);
        }
        catch { }
        finally { _applied = false; }
    }

    /// <summary>
    /// Desde o Windows 10 2004, um pedido de timer só vale para o processo que o fez, a menos que
    /// esta chave esteja ligada. Ligar exige reinício e é opcional — o ajuste por processo já
    /// beneficia o jogo na maioria dos casos. Devolve o valor anterior para permitir a reversão.
    /// </summary>
    public static int? SetGlobalTimerRequests(bool enabled)
    {
        const string path = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
        const string name = "GlobalTimerResolutionRequests";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
            if (key is null) return null;
            int? old = key.GetValue(name) as int?;
            key.SetValue(name, enabled ? 1 : 0, RegistryValueKind.DWord);
            return old;
        }
        catch { return null; }
    }

    /// <summary>Estado atual da chave global (null quando ela não existe).</summary>
    public static bool? GlobalTimerRequestsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
                return key?.GetValue("GlobalTimerResolutionRequests") is int v ? v != 0 : null;
            }
            catch { return null; }
        }
    }
}
