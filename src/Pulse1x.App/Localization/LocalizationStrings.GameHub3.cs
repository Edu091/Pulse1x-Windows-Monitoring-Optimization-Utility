namespace Pulse1x.App.Localization;

/// <summary>
/// Third GameHub localization batch: side menu, virtual keyboard, statistics and auto scan.
/// Unicode escapes keep this table stable across Windows code pages.
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddGameHub3Portuguese()
    {
        var t = Pt;

        // Side menu
        t["GH_MenuTitle"] = "Menu";
        t["GH_MenuHint"] = "Pressione {0} no controle para abrir este menu a qualquer momento.";
        t["GH_PlayDoubleTap"] = "Jogar (2x)";
        t["GH_Confirm"] = "Confirmar";
        t["Settings_ControllerGlyphs"] = "Bot\u00f5es do controle";
        t["Settings_ControllerAuto"] = "Autom\u00e1tico";
        t["GH_MenuGameProfile"] = "Perfil do jogo selecionado";
        t["GH_MenuStatistics"] = "Estat\u00edsticas";
        t["GH_MenuSettings"] = "Configura\u00e7\u00f5es do Pulse1x";
        t["GH_MenuSleep"] = "Suspender o computador";
        t["GH_MenuRestart"] = "Reiniciar o computador";
        t["GH_MenuShutdown"] = "Desligar o computador";
        t["GH_MenuConfirm"] = "Confirmar: {0}?";
        t["GH_GameActions"] = "A\u00e7\u00f5es do jogo";

        // Virtual keyboard
        t["GH_KeyboardHint"] = "Direcional move \u00b7 {0} digita \u00b7 {1} conclui";
        t["GH_KeySpace"] = "Espa\u00e7o";
        t["GH_KeyClear"] = "Limpar";
        t["GH_KeyDone"] = "Concluir";

        // Pro statistics
        t["GH_StatsProTitle"] = "Estat\u00edsticas Pro";
        t["GH_StatsSubtitle"] = "Tudo calculado a partir das suas sess\u00f5es \u2014 nada \u00e9 enviado para fora do computador.";
        t["GH_StatsLocalOnly"] = "Somente neste computador";
        t["GH_StatsMaster"] = "Coleta local";
        t["GH_StatsTotal"] = "Tempo total";
        t["GH_StatsSessions"] = "Sess\u00f5es";
        t["GH_StatsPerDay"] = "M\u00e9dia por dia (30 dias)";
        t["GH_StatsPeak"] = "Maior dia";
        t["GH_StatsMostPlayed"] = "Mais jogado";
        t["GH_StatsGames"] = "Estat\u00edsticas por jogo";
        t["GH_StatsNoSelection"] = "Nenhum jogo selecionado";
        t["GH_StatsNoSession"] = "Sem sess\u00f5es registradas";
        t["GH_StatsPeriod"] = "Hist\u00f3rico de {0} a {1}";
        t["GH_StatsPlaytime"] = "Horas jogadas";
        t["GH_StatsAvgFps"] = "FPS m\u00e9dio";
        t["GH_StatsOneLow"] = "1% low";
        t["GH_StatsLongest"] = "Maior sess\u00e3o";
        t["GH_StatsCpuTemp"] = "CPU m\u00e9dia";
        t["GH_StatsGpuTemp"] = "GPU m\u00e9dia";
        t["GH_StatsCpuUse"] = "Uso de CPU";
        t["GH_StatsGpuUse"] = "Uso de GPU";
        t["GH_StatsRam"] = "RAM m\u00e9dia";
        t["GH_StatsLast14"] = "\u00daltimos 14 dias";
        t["GH_StatsCollection"] = "Coletas da pr\u00f3xima sess\u00e3o";
        t["GH_StatsNextSession"] = "Cada grupo pode ser desligado";
        t["GH_StatsCollectTime"] = "Horas";
        t["GH_StatsCollectFps"] = "FPS / 1% low";
        t["GH_StatsCollectTemp"] = "Temperaturas";
        t["GH_StatsCollectUse"] = "Uso CPU/GPU";
        t["GH_StatsCollectRam"] = "RAM";
        t["GH_StatsEnabled"] = "Registrar horas e desempenho";
        t["GH_StatsClear"] = "Apagar hist\u00f3rico";
        t["GH_StatsClearConfirm"] = "Apagar todo o hist\u00f3rico de sess\u00f5es?\n\nAs horas, m\u00e9dias e estat\u00edsticas de FPS ser\u00e3o perdidas. Esta a\u00e7\u00e3o n\u00e3o pode ser desfeita.";
        t["GH_StatsEmpty"] = "Sua biblioteca ainda est\u00e1 vazia. Adicione um jogo ao GameHub para acompanhar as estat\u00edsticas.";
        t["GH_StatsFpsEstimated"] = "O FPS \u00e9 estimado sem injetar nada no jogo \u2014 serve para comparar sess\u00f5es do mesmo jogo, n\u00e3o como n\u00famero de benchmark.";
        t["GH_StatsImpactOff"] = "Impacto: zero. Todas as medi\u00e7\u00f5es est\u00e3o desligadas.";
        t["GH_StatsImpactMinimal"] = "Impacto: desprez\u00edvel. Apenas o registro da sess\u00e3o est\u00e1 ativo.";
        t["GH_StatsImpactVeryLow"] = "Impacto esperado: muito baixo. Sensores locais s\u00e3o lidos a cada 3 segundos.";
        t["GH_StatsImpactLow"] = "Impacto esperado: baixo. Todas as leituras s\u00e3o locais e amostradas a cada 3 segundos.";

        // Automatic scan
        t["Settings_AutoScanFolders"] = "Varrer as pastas salvas ao abrir o GameHub";
        t["Settings_AutoScanFoldersHint"] = "Al\u00e9m das lojas, revarre as pastas que voc\u00ea j\u00e1 mandou vigiar, encontrando jogos novos instalados nelas.";
        t["Settings_AutoStartLaunchers"] = "Abrir estes launchers junto com o GameHub";
        t["Settings_AutoStartLaunchersHint"] = "Steam, Epic e afins levam alguns segundos para subir. Abrindo junto com o hub, quando voc\u00ea apertar Jogar o cliente j\u00e1 est\u00e1 de p\u00e9.";
        t["GH_RemoveDuplicates"] = "Remover jogos duplicados";
        t["GH_DuplicatesRemoved"] = "{0} duplicados removidos.";
        t["GH_NoDuplicates"] = "Nenhum jogo duplicado encontrado.";
    }

    internal static void AddGameHub3English()
    {
        var t = En;

        t["GH_MenuTitle"] = "Menu";
        t["GH_MenuHint"] = "Press {0} on the controller to open this menu at any time.";
        t["GH_PlayDoubleTap"] = "Play (2x)";
        t["GH_Confirm"] = "Confirm";
        t["Settings_ControllerGlyphs"] = "Controller buttons";
        t["Settings_ControllerAuto"] = "Automatic";
        t["GH_MenuGameProfile"] = "Selected game profile";
        t["GH_MenuStatistics"] = "Statistics";
        t["GH_MenuSettings"] = "Pulse1x settings";
        t["GH_MenuSleep"] = "Sleep";
        t["GH_MenuRestart"] = "Restart";
        t["GH_MenuShutdown"] = "Shut down";
        t["GH_MenuConfirm"] = "Confirm: {0}?";
        t["GH_GameActions"] = "Game actions";

        t["GH_KeyboardHint"] = "D-pad moves \u00b7 {0} types \u00b7 {1} finishes";
        t["GH_KeySpace"] = "Space";
        t["GH_KeyClear"] = "Clear";
        t["GH_KeyDone"] = "Done";

        t["GH_StatsProTitle"] = "Pro Statistics";
        t["GH_StatsSubtitle"] = "Everything is computed from your own sessions \u2014 nothing leaves this computer.";
        t["GH_StatsLocalOnly"] = "This computer only";
        t["GH_StatsMaster"] = "Local collection";
        t["GH_StatsTotal"] = "Total time";
        t["GH_StatsSessions"] = "Sessions";
        t["GH_StatsPerDay"] = "Daily average (30 days)";
        t["GH_StatsPeak"] = "Biggest day";
        t["GH_StatsMostPlayed"] = "Most played";
        t["GH_StatsGames"] = "Statistics by game";
        t["GH_StatsNoSelection"] = "No game selected";
        t["GH_StatsNoSession"] = "No recorded sessions";
        t["GH_StatsPeriod"] = "History from {0} to {1}";
        t["GH_StatsPlaytime"] = "Playtime";
        t["GH_StatsAvgFps"] = "Average FPS";
        t["GH_StatsOneLow"] = "1% low";
        t["GH_StatsLongest"] = "Longest session";
        t["GH_StatsCpuTemp"] = "Average CPU";
        t["GH_StatsGpuTemp"] = "Average GPU";
        t["GH_StatsCpuUse"] = "CPU usage";
        t["GH_StatsGpuUse"] = "GPU usage";
        t["GH_StatsRam"] = "Average RAM";
        t["GH_StatsLast14"] = "Last 14 days";
        t["GH_StatsCollection"] = "Next session collection";
        t["GH_StatsNextSession"] = "Each group can be disabled";
        t["GH_StatsCollectTime"] = "Playtime";
        t["GH_StatsCollectFps"] = "FPS / 1% low";
        t["GH_StatsCollectTemp"] = "Temperatures";
        t["GH_StatsCollectUse"] = "CPU/GPU usage";
        t["GH_StatsCollectRam"] = "RAM";
        t["GH_StatsEnabled"] = "Record hours and performance";
        t["GH_StatsClear"] = "Clear history";
        t["GH_StatsClearConfirm"] = "Delete the whole session history?\n\nHours, averages and FPS statistics will be lost. This cannot be undone.";
        t["GH_StatsEmpty"] = "Your library is empty. Add a game to GameHub to track its statistics.";
        t["GH_StatsFpsEstimated"] = "FPS is estimated without injecting anything into the game \u2014 use it to compare sessions of the same game, not as a benchmark number.";
        t["GH_StatsImpactOff"] = "Impact: none. All measurements are disabled.";
        t["GH_StatsImpactMinimal"] = "Impact: negligible. Only session logging is active.";
        t["GH_StatsImpactVeryLow"] = "Expected impact: very low. Local sensors are read every 3 seconds.";
        t["GH_StatsImpactLow"] = "Expected impact: low. All readings stay local and are sampled every 3 seconds.";

        t["Settings_AutoScanFolders"] = "Scan saved folders when opening GameHub";
        t["Settings_AutoScanFoldersHint"] = "Besides the stores, rescans the folders you asked to watch, finding newly installed games in them.";
        t["Settings_AutoStartLaunchers"] = "Open these launchers together with GameHub";
        t["Settings_AutoStartLaunchersHint"] = "Steam, Epic and friends take a few seconds to start. Opening them with the hub means the client is already up when you press Play.";
        t["GH_RemoveDuplicates"] = "Remove duplicate games";
        t["GH_DuplicatesRemoved"] = "{0} duplicates removed.";
        t["GH_NoDuplicates"] = "No duplicate games found.";
    }
}
