using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

public static partial class FunctionSafety
{
    // Nível de segurança das funções desta área (chave -> nível). Não listado = Seguro.
    private static readonly (string Key, ToolSafety Level)[] Special =
    {
        // Reparo do Sistema
        ("Special_SfcTitle", ToolSafety.Safe),
        ("Special_DismRestoreTitle", ToolSafety.Safe),
        ("Special_ChkdskTitle", ToolSafety.Safe),
        ("Special_DismCleanupTitle", ToolSafety.Moderate),

        // Desempenho
        ("Special_BalancedPlanTitle", ToolSafety.Safe),
        ("Special_HighPerfTitle", ToolSafety.Moderate),
        ("Special_UltimateTitle", ToolSafety.Moderate),
        ("Special_OptimizeDrivesTitle", ToolSafety.Safe),

        // Recuperação
        ("Special_SystemRestoreTitle", ToolSafety.Advanced),

        // Ferramentas do Windows
        ("Special_GodModeTitle", ToolSafety.Safe),
        ("Special_DeviceManagerTitle", ToolSafety.Safe),
        ("Special_DiskManagementTitle", ToolSafety.Moderate),
        ("Special_ResourceMonitorTitle", ToolSafety.Safe),
        ("Special_PerfMonitorTitle", ToolSafety.Safe),
        ("Special_EventViewerTitle", ToolSafety.Safe),
        ("Special_ServicesTitle", ToolSafety.Moderate),
        ("Special_GroupPolicyTitle", ToolSafety.Moderate),
        ("Special_MemoryDiagnosticTitle", ToolSafety.Moderate),
        ("Special_TaskManagerTitle", ToolSafety.Safe),

        // Rede
        ("Special_RenewNetworkTitle", ToolSafety.Safe),
        ("Special_ResetNetworkTitle", ToolSafety.Advanced),
    };
}
