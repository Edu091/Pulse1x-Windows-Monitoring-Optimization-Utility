namespace Pulse1x.App.Localization;

/// <summary>
/// Terceira leva de textos do GameHub: menu lateral (botão View), teclado virtual, estatísticas e
/// varredura automática.
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddGameHub3Portuguese()
    {
        var t = Pt;

        // ---- Menu lateral ----
        t["GH_MenuTitle"] = "Menu";
        t["GH_MenuHint"] = "Pressione View no controle para abrir este menu a qualquer momento.";
        t["GH_MenuGameProfile"] = "Perfil do jogo selecionado";
        t["GH_MenuStatistics"] = "Estatísticas";
        t["GH_MenuSettings"] = "Configurações do Pulse1x";
        t["GH_MenuSleep"] = "Suspender o computador";
        t["GH_MenuRestart"] = "Reiniciar o computador";
        t["GH_MenuShutdown"] = "Desligar o computador";
        t["GH_MenuConfirm"] = "Confirmar: {0}?";
        t["GH_GameActions"] = "Ações do jogo";

        // ---- Teclado virtual ----
        t["GH_KeyboardHint"] = "Direcional move · A digita · B conclui";
        t["GH_KeySpace"] = "Espaço";
        t["GH_KeyClear"] = "Limpar";
        t["GH_KeyDone"] = "Concluir";

        // ---- Estatísticas ----
        t["GH_StatsSubtitle"] = "Tudo calculado a partir das suas sessões — nada é enviado para fora do computador.";
        t["GH_StatsTotal"] = "Tempo total";
        t["GH_StatsPerDay"] = "Média por dia (30 dias)";
        t["GH_StatsPeak"] = "Maior dia";
        t["GH_StatsMostPlayed"] = "Mais jogado";
        t["GH_StatsLast14"] = "Últimos 14 dias";
        t["GH_StatsEnabled"] = "Registrar horas e desempenho";
        t["GH_StatsClear"] = "Apagar histórico";
        t["GH_StatsClearConfirm"] = "Apagar todo o histórico de sessões?\n\nAs horas, médias e estatísticas de FPS serão perdidas. Esta ação não pode ser desfeita.";
        t["GH_StatsEmpty"] = "Ainda não há sessões registradas. Jogue alguma coisa pelo GameHub e as estatísticas aparecem aqui.";
        t["GH_StatsFpsEstimated"] = "O FPS é estimado sem injetar nada no jogo — serve para comparar sessões do mesmo jogo, não como número de benchmark.";

        // ---- Varredura automática ----
        t["Settings_AutoScanFolders"] = "Varrer as pastas salvas ao abrir o GameHub";
        t["Settings_AutoScanFoldersHint"] = "Além das lojas, revarre as pastas que você já mandou vigiar, encontrando jogos novos instalados nelas.";
        t["Settings_AutoStartLaunchers"] = "Abrir estes launchers junto com o GameHub";
        t["Settings_AutoStartLaunchersHint"] = "Steam, Epic e afins levam alguns segundos para subir. Abrindo junto com o hub, quando você apertar Jogar o cliente já está de pé.";
        t["GH_RemoveDuplicates"] = "Remover jogos duplicados";
        t["GH_DuplicatesRemoved"] = "{0} duplicados removidos.";
        t["GH_NoDuplicates"] = "Nenhum jogo duplicado encontrado.";
    }

    internal static void AddGameHub3English()
    {
        var t = En;

        t["GH_MenuTitle"] = "Menu";
        t["GH_MenuHint"] = "Press View on the controller to open this menu at any time.";
        t["GH_MenuGameProfile"] = "Selected game profile";
        t["GH_MenuStatistics"] = "Statistics";
        t["GH_MenuSettings"] = "Pulse1x settings";
        t["GH_MenuSleep"] = "Sleep";
        t["GH_MenuRestart"] = "Restart";
        t["GH_MenuShutdown"] = "Shut down";
        t["GH_MenuConfirm"] = "Confirm: {0}?";
        t["GH_GameActions"] = "Game actions";

        t["GH_KeyboardHint"] = "D-pad moves · A types · B finishes";
        t["GH_KeySpace"] = "Space";
        t["GH_KeyClear"] = "Clear";
        t["GH_KeyDone"] = "Done";

        t["GH_StatsSubtitle"] = "Everything is computed from your own sessions — nothing leaves this computer.";
        t["GH_StatsTotal"] = "Total time";
        t["GH_StatsPerDay"] = "Daily average (30 days)";
        t["GH_StatsPeak"] = "Biggest day";
        t["GH_StatsMostPlayed"] = "Most played";
        t["GH_StatsLast14"] = "Last 14 days";
        t["GH_StatsEnabled"] = "Record hours and performance";
        t["GH_StatsClear"] = "Clear history";
        t["GH_StatsClearConfirm"] = "Delete the whole session history?\n\nHours, averages and FPS statistics will be lost. This cannot be undone.";
        t["GH_StatsEmpty"] = "No sessions recorded yet. Play something through GameHub and the statistics show up here.";
        t["GH_StatsFpsEstimated"] = "FPS is estimated without injecting anything into the game — use it to compare sessions of the same game, not as a benchmark number.";

        t["Settings_AutoScanFolders"] = "Scan saved folders when opening GameHub";
        t["Settings_AutoScanFoldersHint"] = "Besides the stores, rescans the folders you asked to watch, finding newly installed games in them.";
        t["Settings_AutoStartLaunchers"] = "Open these launchers together with GameHub";
        t["Settings_AutoStartLaunchersHint"] = "Steam, Epic and friends take a few seconds to start. Opening them with the hub means the client is already up when you press Play.";
        t["GH_RemoveDuplicates"] = "Remove duplicate games";
        t["GH_DuplicatesRemoved"] = "{0} duplicates removed.";
        t["GH_NoDuplicates"] = "No duplicate games found.";
    }
}
