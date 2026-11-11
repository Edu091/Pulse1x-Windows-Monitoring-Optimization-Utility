using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

public static partial class FunctionSafety
{
    // Nível de segurança das funções desta área (chave -> nível). Não listado = Seguro.
    private static readonly (string Key, ToolSafety Level)[] PostFormat =
    {
        ("PfInfo_restore_point", ToolSafety.Safe),
        ("PfInfo_enable_system_restore", ToolSafety.Safe),
        ("PfInfo_high_performance", ToolSafety.Moderate),
        ("PfInfo_ultimate_performance", ToolSafety.Moderate),
        ("PfInfo_disable_suggestions", ToolSafety.Safe),
        ("PfInfo_reduce_telemetry", ToolSafety.Moderate),
        ("PfInfo_show_extensions", ToolSafety.Safe),
        ("PfInfo_show_hidden", ToolSafety.Safe),
        ("PfInfo_explorer_this_pc", ToolSafety.Safe),
        ("PfInfo_manage_startup", ToolSafety.Safe),
    };
}
