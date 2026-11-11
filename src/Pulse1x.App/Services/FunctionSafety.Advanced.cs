using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

public static partial class FunctionSafety
{
    // Nível de segurança das funções desta área (chave -> nível). Não listado = Seguro.
    private static readonly (string Key, ToolSafety Level)[] Advanced =
    {
        // Privacidade
        ("AdvInfo_copilot", ToolSafety.Moderate),
        ("AdvInfo_cortana", ToolSafety.Safe),
        ("AdvInfo_telemetry", ToolSafety.Safe),
        ("AdvInfo_telemetry-tasks", ToolSafety.Safe),
        ("AdvInfo_nvidia-telemetry", ToolSafety.Safe),
        ("AdvInfo_suggestions", ToolSafety.Safe),
        ("AdvInfo_browser-telemetry", ToolSafety.Safe),
        ("AdvInfo_notifications", ToolSafety.Moderate),
        ("AdvInfo_recall", ToolSafety.Moderate),
        ("AdvInfo_advertising-id", ToolSafety.Safe),
        ("AdvInfo_tailored-experiences", ToolSafety.Safe),
        ("AdvInfo_diagtrack-service", ToolSafety.Moderate),
        ("AdvInfo_start-web-search", ToolSafety.Safe),
        ("AdvInfo_lockscreen-ads", ToolSafety.Safe),
        ("AdvInfo_feedback-requests", ToolSafety.Safe),
        ("AdvInfo_activity-history", ToolSafety.Safe),

        // Desempenho (serviços)
        ("AdvInfo_xbox-services", ToolSafety.Advanced),
        ("AdvInfo_fax-service", ToolSafety.Safe),
        ("AdvInfo_windows-update-auto", ToolSafety.Advanced),
        ("AdvInfo_windows-search", ToolSafety.Advanced),
        ("AdvInfo_sysmain", ToolSafety.Moderate),
        ("AdvInfo_print-spooler", ToolSafety.Advanced),
        ("AdvInfo_remote-registry", ToolSafety.Safe),
        ("AdvInfo_remote-access", ToolSafety.Moderate),
        ("AdvInfo_retail-demo", ToolSafety.Safe),
        ("AdvInfo_map-broker", ToolSafety.Safe),
        ("AdvInfo_biometrics", ToolSafety.Moderate),
        ("AdvInfo_delivery-optimization", ToolSafety.Safe),

        // Sistema
        ("AdvInfo_hibernation", ToolSafety.Moderate),

        // Hardware
        ("AdvInfo_hw-gpu-scheduling", ToolSafety.Advanced),
        ("AdvInfo_core-parking", ToolSafety.Advanced),
        ("AdvInfo_power-throttling", ToolSafety.Moderate),
        ("AdvInfo_gpu-msi", ToolSafety.Advanced),
        ("AdvInfo_game-priority", ToolSafety.Safe),
        ("AdvInfo_mmcss-responsiveness", ToolSafety.Safe),
        ("AdvInfo_foreground-priority", ToolSafety.Safe),
        ("AdvInfo_ultimate-power", ToolSafety.Moderate),
        ("AdvInfo_windowed-game-opt", ToolSafety.Safe),

        // Jogos
        ("AdvInfo_game-dvr", ToolSafety.Moderate),
        ("AdvInfo_xbox-gamebar", ToolSafety.Moderate),

        // Aceleração de hardware em aplicativos
        ("AdvInfo_hwaccel-chrome", ToolSafety.Moderate),
        ("AdvInfo_hwaccel-edge", ToolSafety.Moderate),
        ("AdvInfo_hwaccel-brave", ToolSafety.Moderate),
        ("AdvInfo_hwaccel-firefox", ToolSafety.Moderate),
        ("AdvInfo_hwaccel-discord", ToolSafety.Moderate),
        ("AdvInfo_hwaccel-spotify", ToolSafety.Safe),
    };
}
