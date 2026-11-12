using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

public static partial class FunctionSafety
{
    // Nível de segurança das funções da Gravação (chave -> nível). Não listado = Seguro.
    // O Game DVR e a Xbox Game Bar reaproveitam as chaves das Otimizações Avançadas (AdvInfo_*).
    private static readonly (string Key, ToolSafety Level)[] RecordingArea =
    {
        ("RecInfo_Engine", ToolSafety.Safe),
        ("RecInfo_Hotkeys", ToolSafety.Safe),
    };
}
