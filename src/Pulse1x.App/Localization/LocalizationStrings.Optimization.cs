namespace Pulse1x.App.Localization;

/// <summary>
/// Textos da reorganização das Otimizações Avançadas (1.13.0): o grupo de privacidade/bloatware
/// separado do grupo de otimização de hardware e software, Game DVR, aceleração de hardware por
/// aplicativo e os novos ajustes de CPU/GPU. Algumas chaves da tabela principal são
/// sobrescritas aqui de propósito (títulos das categorias).
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddOptimizationPortuguese()
    {
        var t = Pt;

        t["Opt_AdvancedSub"] = "Privacidade, limpeza do sistema e ajustes de hardware e software — seguros e totalmente reversíveis";
        t["Opt_GroupDebloat"] = "🛡️ Privacidade, telemetria e bloatware";
        t["Opt_GroupDebloatSub"] = "Desliga coleta de dados, serviços que quase ninguém usa e apps pré-instalados.";
        t["Opt_GroupTuning"] = "⚡ Otimização de hardware e software";
        t["Opt_GroupTuningSub"] = "Ajusta como o Windows usa a CPU, a GPU e a memória, principalmente durante jogos.";
        t["Opt_CatPerformance"] = "🧹 Serviços e recursos dispensáveis";
        t["Opt_CatHardware"] = "🔧 CPU e GPU";
        t["Opt_CatGaming"] = "🎮 Jogos e captura";
        t["Opt_CatHwAccel"] = "🖼️ Aceleração de hardware em aplicativos";
        t["Opt_CatSystem"] = "💾 Sistema";

        t["Opt_HwAccelIntro"] =
            "Navegadores, Discord e Spotify usam a GPU mesmo em segundo plano, ocupando VRAM e tempo de GPU que poderiam ir para o jogo. " +
            "Escolha em quais aplicativos desativar a aceleração de hardware. Desligar o interruptor devolve a configuração que existia antes. " +
            "A mudança vale na próxima vez que o aplicativo abrir.";
        t["Opt_HwAccelNone"] = "Nenhum aplicativo compatível encontrado (Chrome, Edge, Brave, Firefox, Discord ou Spotify).";
        t["Opt_HwAccelDisableAll"] = "Desativar em todos";
        t["Opt_HwAccelRestoreAll"] = "Restaurar todos";
        t["AdvOpt_HwAccelPolicyDesc"] = "Desativa a aceleração de hardware do {0} pela política oficial do navegador. Ele passa a mostrar \"gerenciado pela organização\" enquanto a política existir.";
        t["AdvOpt_HwAccelDiscordDesc"] = "Desativa a aceleração de hardware no arquivo de configuração do Discord (também PTB e Canary).";
        t["AdvOpt_HwAccelSpotifyDesc"] = "Desativa a aceleração de hardware no arquivo de preferências do Spotify.";
        t["AdvOpt_HwAccelRestartApp"] = "Reabra o app para aplicar";
        t["AdvOpt_HwAccelCloseApp"] = "{0} está aberto: feche-o por completo (inclusive da bandeja) antes, ou ele pode desfazer a mudança ao sair";

        t["AdvOpt_GameDvrTitle"] = "Desativar Game DVR";
        t["AdvOpt_GameDvrDesc"] =
            "Desliga a gravação de jogos do Windows, a captura contínua em segundo plano (\"Gravar o que aconteceu\") e a gravação de áudio. " +
            "Evita que o Windows grave a partida o tempo todo, poupando CPU, GPU, disco e memória. A Xbox Game Bar e o Modo de Jogo " +
            "continuam funcionando. Desligar o interruptor restaura tudo como estava.";
        t["AdvOpt_GameDvrDetail"] = "Afeta: GameDVR, captura em segundo plano, gravação de áudio e o serviço BcastDVRUserService (este vale no próximo logon).";
        t["AdvOpt_XboxGameBarTitle"] = "Desativar Xbox Game Bar";
        t["AdvOpt_XboxGameBarDesc"] =
            "Impede que o botão Xbox do controle abra a Xbox Game Bar por cima do jogo, remove a dica de abertura e não deixa o app " +
            "da Game Bar rodar em segundo plano. A gravação é controlada à parte, pela opção Game DVR. Desligar o interruptor restaura tudo como estava.";
        t["AdvOpt_XboxGameBarDetail"] = "Afeta: botão Xbox (UseNexusForGameBarEnabled), painel de abertura e execução em segundo plano da Xbox Game Bar.";

        t["AdvOpt_SystemResponsivenessTitle"] = "Menos CPU reservada para segundo plano";
        t["AdvOpt_SystemResponsivenessDesc"] =
            "Enquanto um jogo ou app multimídia roda, o Windows reserva 20% da CPU para tarefas de baixa prioridade. " +
            "Reduz essa reserva para 10%, o mínimo aceito pelo sistema, deixando mais processamento para o jogo.";
        t["AdvOpt_ForegroundPriorityTitle"] = "Prioridade para o programa em primeiro plano";
        t["AdvOpt_ForegroundPriorityDesc"] =
            "Usa fatias de tempo curtas e dá o reforço máximo à janela ativa (Win32PrioritySeparation = 0x26). " +
            "O jogo recebe a CPU com mais frequência que os programas atrás dele. Padrão do Windows: 2.";
        t["AdvOpt_UltimatePowerTitle"] = "Plano de energia Desempenho Máximo";
        t["AdvOpt_UltimatePowerDesc"] =
            "Ativa o plano oculto \"Desempenho Máximo\" da Microsoft: a CPU não reduz a frequência entre picos de carga, eliminando " +
            "microatrasos de gerenciamento de energia. O plano anterior é lembrado e volta ao desligar o interruptor.";
        t["AdvOpt_UltimatePowerWarning"] = "Aumenta o consumo de energia e a temperatura em repouso. Em notebooks, reduz a duração da bateria.";
        t["AdvOpt_WindowedGamesTitle"] = "Otimizações para jogos em janela";
        t["AdvOpt_WindowedGamesDesc"] =
            "Faz jogos DirectX 10/11 em janela ou em tela cheia sem bordas usarem o modelo de apresentação moderno (flip), o mesmo da " +
            "tela cheia exclusiva: menos latência e suporte a VRR e Auto HDR. É a opção das Configurações de Gráficos do Windows 11.";
    }

    internal static void AddOptimizationEnglish()
    {
        var t = En;

        t["Opt_AdvancedSub"] = "Privacy, system cleanup and hardware/software tuning — safe and fully reversible";
        t["Opt_GroupDebloat"] = "🛡️ Privacy, telemetry and bloatware";
        t["Opt_GroupDebloatSub"] = "Turns off data collection, services almost nobody uses and preinstalled apps.";
        t["Opt_GroupTuning"] = "⚡ Hardware and software optimization";
        t["Opt_GroupTuningSub"] = "Tunes how Windows uses the CPU, GPU and memory, especially while gaming.";
        t["Opt_CatPerformance"] = "🧹 Unneeded services and features";
        t["Opt_CatHardware"] = "🔧 CPU and GPU";
        t["Opt_CatGaming"] = "🎮 Gaming and capture";
        t["Opt_CatHwAccel"] = "🖼️ Hardware acceleration in apps";
        t["Opt_CatSystem"] = "💾 System";

        t["Opt_HwAccelIntro"] =
            "Browsers, Discord and Spotify use the GPU even in the background, taking VRAM and GPU time that could go to the game. " +
            "Choose which apps should have hardware acceleration turned off. Turning a switch off brings back the setting that was there before. " +
            "The change takes effect the next time the app opens.";
        t["Opt_HwAccelNone"] = "No supported app found (Chrome, Edge, Brave, Firefox, Discord or Spotify).";
        t["Opt_HwAccelDisableAll"] = "Disable in all";
        t["Opt_HwAccelRestoreAll"] = "Restore all";
        t["AdvOpt_HwAccelPolicyDesc"] = "Turns off {0} hardware acceleration through the browser's official policy. It will show \"managed by your organization\" while the policy exists.";
        t["AdvOpt_HwAccelDiscordDesc"] = "Turns off hardware acceleration in Discord's settings file (PTB and Canary too).";
        t["AdvOpt_HwAccelSpotifyDesc"] = "Turns off hardware acceleration in Spotify's preferences file.";
        t["AdvOpt_HwAccelRestartApp"] = "Reopen the app to apply";
        t["AdvOpt_HwAccelCloseApp"] = "{0} is open: fully close it first (including from the tray), or it may undo the change when it exits";

        t["AdvOpt_GameDvrTitle"] = "Disable Game DVR";
        t["AdvOpt_GameDvrDesc"] =
            "Turns off Windows game recording, continuous background capture (\"Record what happened\") and audio capture. " +
            "Stops Windows from recording the match all the time, saving CPU, GPU, disk and memory. The Xbox Game Bar and Game Mode " +
            "keep working. Turning the switch off restores everything.";
        t["AdvOpt_GameDvrDetail"] = "Affects: GameDVR, background capture, audio capture and the BcastDVRUserService service (this one applies at next sign-in).";
        t["AdvOpt_XboxGameBarTitle"] = "Disable Xbox Game Bar";
        t["AdvOpt_XboxGameBarDesc"] =
            "Stops the controller's Xbox button from opening the Xbox Game Bar over the game, removes the startup tip and keeps the " +
            "Game Bar app from running in the background. Recording is controlled separately by the Game DVR option. Turning the switch off restores everything.";
        t["AdvOpt_XboxGameBarDetail"] = "Affects: Xbox button (UseNexusForGameBarEnabled), startup panel and background running of the Xbox Game Bar.";

        t["AdvOpt_SystemResponsivenessTitle"] = "Less CPU reserved for background tasks";
        t["AdvOpt_SystemResponsivenessDesc"] =
            "While a game or multimedia app runs, Windows reserves 20% of the CPU for low-priority tasks. " +
            "Lowers that reserve to 10%, the minimum the system accepts, leaving more processing for the game.";
        t["AdvOpt_ForegroundPriorityTitle"] = "Priority for the foreground program";
        t["AdvOpt_ForegroundPriorityDesc"] =
            "Uses short time slices and gives the maximum boost to the active window (Win32PrioritySeparation = 0x26). " +
            "The game gets the CPU more often than the programs behind it. Windows default: 2.";
        t["AdvOpt_UltimatePowerTitle"] = "Ultimate Performance power plan";
        t["AdvOpt_UltimatePowerDesc"] =
            "Enables Microsoft's hidden \"Ultimate Performance\" plan: the CPU does not lower its frequency between load spikes, removing " +
            "power-management micro-delays. The previous plan is remembered and comes back when you turn the switch off.";
        t["AdvOpt_UltimatePowerWarning"] = "Raises idle power draw and temperature. On laptops, it shortens battery life.";
        t["AdvOpt_WindowedGamesTitle"] = "Optimizations for windowed games";
        t["AdvOpt_WindowedGamesDesc"] =
            "Makes DirectX 10/11 games in windowed or borderless mode use the modern (flip) presentation model, the same as exclusive " +
            "fullscreen: lower latency plus VRR and Auto HDR support. This is the Windows 11 Graphics settings option.";
    }
}
