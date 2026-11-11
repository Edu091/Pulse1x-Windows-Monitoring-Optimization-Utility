using Pulse1x.App.ViewModels;

namespace Pulse1x.App.Services;

public static partial class FunctionSafety
{
    // Nível de segurança das funções desta área (chave -> nível). Não listado = Seguro.
    private static readonly (string Key, ToolSafety Level)[] Sound =
    {
        ("SndInfo_AudioEngine", ToolSafety.Advanced),
        ("SndInfo_CallModes", ToolSafety.Advanced),
        ("SndInfo_DefaultDevice", ToolSafety.Moderate),
        ("SndInfo_MicBoost", ToolSafety.Moderate),
        ("SndInfo_HwBoost", ToolSafety.Moderate),
        ("SndInfo_MicFilters", ToolSafety.Safe),
        ("SndInfo_Equalizer", ToolSafety.Moderate),
        ("SndInfo_OutputGain", ToolSafety.Moderate),
        ("SndInfo_VoiceChanger", ToolSafety.Moderate),
        ("SndInfo_Soundboard", ToolSafety.Moderate),
        ("SndInfo_ApplyOnStartup", ToolSafety.Safe),
        ("SndInfo_LockMic", ToolSafety.Moderate),
    };
}
