namespace Pulse1x.App.Localization;

/// <summary>
/// Textos da moldura do app (navegação lateral agrupada e cabeçalho da área de conteúdo).
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddShellPortuguese()
    {
        var t = Pt;
        t["NavGroup_Overview"] = "Visão geral";
        t["NavGroup_Optimization"] = "Otimização";
        t["NavGroup_Utilities"] = "Utilidades";
        t["NavGroup_App"] = "Aplicativo";

        // "Utilidade" sozinho ficava ambíguo dentro do grupo Utilidades: a página é a Central
        // Pós-Formatação (instalar programas, debloat e ajustes iniciais).
        t["Nav_PostFormat"] = "Pós-formatação";
        t["Nav_Optimization"] = "Otimizações";

        t["Shell_Version"] = "Versão {0}";
        t["Shell_RecBadge"] = "REC";

        t["Hub_ConfirmTitle"] = "Entrar no GameHub?";
        t["Hub_ConfirmBody"] = "O GameHub abre em tela cheia, no estilo de console, e esconde a navegação do Pulse1x. Para voltar, use o botão de sair dentro do hub.";
        t["Hub_ConfirmEnter"] = "Entrar";
        t["Hub_ConfirmCancel"] = "Cancelar";
    }

    internal static void AddShellEnglish()
    {
        var t = En;
        t["NavGroup_Overview"] = "Overview";
        t["NavGroup_Optimization"] = "Optimization";
        t["NavGroup_Utilities"] = "Utilities";
        t["NavGroup_App"] = "App";

        t["Nav_PostFormat"] = "Post-format";
        t["Nav_Optimization"] = "Optimizations";

        t["Shell_Version"] = "Version {0}";
        t["Shell_RecBadge"] = "REC";

        t["Hub_ConfirmTitle"] = "Open GameHub?";
        t["Hub_ConfirmBody"] = "GameHub opens full screen in a console-style layout and hides the Pulse1x navigation. To come back, use the exit button inside the hub.";
        t["Hub_ConfirmEnter"] = "Open";
        t["Hub_ConfirmCancel"] = "Cancel";
    }
}
