using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pulse1x.App.Services;

/// <summary>
/// Resultado de uma execução da otimização de memória.
/// </summary>
public record MemoryOptimizationResult(
    double UsedBeforeGb,
    double UsedAfterGb,
    double FreedMb,
    DateTime ExecutedAt,
    bool Success,
    bool DeepCleanApplied,
    string Message);

/// <summary>
/// Otimização de memória RAM em duas camadas, ambas seguras e não-destrutivas:
///
/// 1) Aparar working sets — chama EmptyWorkingSet em cada processo acessível, pedindo
///    ao Gerenciador de Memória que devolva as páginas residentes mas ociosas. Funciona
///    mesmo sem privilégios especiais.
///
/// 2) Limpeza profunda ("desfragmentação" da RAM) — via NtSetSystemInformation
///    (SystemMemoryListInformation), no estilo do RAMMap/Wise Memory Optimizer:
///       • MemoryEmptyWorkingSets   — esvazia os working sets de todo o sistema (kernel);
///       • MemoryFlushModifiedList  — grava as páginas modificadas no disco (sem perda de dados);
///       • MemoryPurgeStandbyList   — libera a lista de standby (cache de arquivos em RAM).
///    Esta camada exige o privilégio SeProfileSingleProcessPrivilege (o app roda como
///    administrador). Se o privilégio não puder ser obtido, esta etapa é simplesmente
///    ignorada e apenas a camada 1 é aplicada.
///
/// Nada é encerrado, nenhum serviço é parado, nenhum registro/arquivo é alterado.
/// A limpeza da standby apenas descarta o cache de arquivos (que o Windows reconstrói
/// sob demanda), sem risco de perda de dados.
/// </summary>
public class MemoryOptimizationService
{
    private readonly ISystemMetricsService _metrics;

    public MemoryOptimizationService(ISystemMetricsService metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Executa a otimização de forma assíncrona (fora da thread de UI).
    /// O callback opcional reporta o progresso (0..1) para a barra de progresso.
    /// </summary>
    public Task<MemoryOptimizationResult> OptimizeAsync(IProgress<double>? progress = null)
    {
        return Task.Run(() => Optimize(progress));
    }

    private MemoryOptimizationResult Optimize(IProgress<double>? progress)
    {
        var executedAt = DateTime.Now;
        double usedBeforeGb = _metrics.ReadRam().UsedGb;

        progress?.Report(0.05);

        // ---- Camada 1: aparar o working set de cada processo acessível ----
        TrimAllProcessWorkingSets(progress);

        // ---- Camada 2: limpeza profunda via NtSetSystemInformation ----
        bool deepClean = false;
        if (TryEnablePrivilege(SeProfileSingleProcessPrivilege))
        {
            // Tenta também o privilégio de cota (alguns sistemas o exigem para esvaziar working sets).
            TryEnablePrivilege(SeIncreaseQuotaPrivilege);

            progress?.Report(0.6);
            bool emptied = RunMemoryCommand(MemoryEmptyWorkingSets);

            progress?.Report(0.72);
            bool flushed = RunMemoryCommand(MemoryFlushModifiedList);

            progress?.Report(0.85);
            bool purged = RunMemoryCommand(MemoryPurgeStandbyList);

            deepClean = emptied || flushed || purged;
        }

        // Dá um instante para o SO atualizar os contadores de memória.
        System.Threading.Thread.Sleep(300);
        progress?.Report(1.0);

        double usedAfterGb = _metrics.ReadRam().UsedGb;
        double freedMb = Math.Max(0, (usedBeforeGb - usedAfterGb) * 1024.0);

        string message;
        if (freedMb >= 1)
        {
            message = deepClean
                ? Localization.Loc.F("Opt_MemResultDeep", $"{freedMb:0}")
                : Localization.Loc.F("Opt_MemResultBasic", $"{freedMb:0}");
        }
        else
        {
            message = Localization.Loc.S("Opt_MemResultNothing");
        }

        return new MemoryOptimizationResult(
            usedBeforeGb, usedAfterGb, freedMb, executedAt, true, deepClean, message);
    }

    private static void TrimAllProcessWorkingSets(IProgress<double>? progress)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return; // Não conseguimos enumerar processos: pula esta camada com segurança.
        }

        int total = processes.Length;
        int processed = 0;

        foreach (var process in processes)
        {
            try
            {
                // EmptyWorkingSet é seguro: o SO recoloca as páginas quando necessário.
                // Processos protegidos lançam exceção de acesso e são apenas ignorados.
                K32EmptyWorkingSet(process.Handle);
            }
            catch
            {
                // Sem permissão para este processo (sistema/protegido) — ignorar.
            }
            finally
            {
                process.Dispose();
            }

            processed++;
            if (total > 0)
                progress?.Report(0.05 + 0.5 * (processed / (double)total));
        }
    }

    // ===================== Win32 / NT: aparar working sets =====================

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EmptyWorkingSet(IntPtr hProcess);

    // ===================== NtSetSystemInformation (limpeza profunda) =====================

    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    // Executa um comando da lista de memória. Retorna true se o NTSTATUS for STATUS_SUCCESS (0).
    private static bool RunMemoryCommand(int command)
    {
        try
        {
            int cmd = command;
            return NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int)) == 0;
        }
        catch
        {
            return false; // API indisponível/erro — ignora com segurança.
        }
    }

    // ===================== Habilitação de privilégio de token =====================

    private const string SeProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";
    private const string SeIncreaseQuotaPrivilege = "SeIncreaseQuotaPrivilege";

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    // Pack=4 é essencial: LUID é composto por dois campos de 32 bits, então o struct
    // nativo TOKEN_PRIVILEGES não tem padding. Sem Pack=4, o long de 64 bits seria
    // alinhado em 8 bytes e o struct ficaria corrompido (privilégio não seria aplicado).
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // Habilita um privilégio no token do processo atual. Retorna true se ficou ativo.
    private static bool TryEnablePrivilege(string privilegeName)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                return false;

            if (!LookupPrivilegeValue(null, privilegeName, out long luid))
                return false;

            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                return false;

            // AdjustTokenPrivileges retorna true mesmo quando não atribui tudo;
            // GetLastError == 0 confirma que o privilégio foi de fato habilitado.
            return Marshal.GetLastWin32Error() == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
                CloseHandle(token);
        }
    }
}
