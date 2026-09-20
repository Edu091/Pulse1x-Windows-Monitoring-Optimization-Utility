namespace Pulse1x.App.Localization;

/// <summary>
/// Textos do GameHub. Ficam num arquivo próprio para não inchar ainda
/// mais a tabela principal — o dicionário é o mesmo, preenchido no construtor estático (que roda
/// depois dos inicializadores de campo das duas tabelas).
/// </summary>
internal static partial class LocalizationStrings
{
    static LocalizationStrings()
    {
        AddGameHubPortuguese();
        AddGameHubEnglish();
        AddGameHub2Portuguese();
        AddGameHub2English();
        AddGameHub3Portuguese();
        AddGameHub3English();
    }

    private static void AddGameHubPortuguese()
    {
        var t = Pt;

        // ---- Navegação e títulos ----
        t["Nav_GameHub"] = "GameHub";
        t["GH_Title"] = "GameHub";
        t["GH_Library"] = "Biblioteca";
        t["GH_EmulatorsTitle"] = "Emuladores";
        t["GH_ProfileEditorTitle"] = "Perfil do jogo";
        t["GH_AddGameTitle"] = "Adicionar jogo ou aplicativo";
        t["GH_EditGameTitle"] = "Editar item";

        // ---- Biblioteca ----
        t["GH_Play"] = "Jogar";
        t["GH_PlayApp"] = "Abrir";
        t["GH_Stop"] = "Encerrar sessão";
        t["GH_Favorite"] = "Favoritar";
        t["GH_Unfavorite"] = "Remover dos favoritos";
        t["GH_EditGame"] = "Editar";
        t["GH_EditProfile"] = "Perfil";
        t["GH_Remove"] = "Remover";
        t["GH_Scan"] = "Detectar jogos";
        t["GH_ScanShortcuts"] = "Buscar atalhos";
        t["GH_AddGame"] = "Adicionar";
        t["GH_Emulators"] = "Emuladores";
        t["GH_Search"] = "Buscar na biblioteca...";
        t["GH_Sort"] = "Ordenar por";
        t["GH_Category"] = "Categoria";
        t["GH_FavoritesOnly"] = "Favoritos";
        t["GH_RecentOnly"] = "Recentes";
        t["GH_ChangeCover"] = "Trocar capa";
        t["GH_ChangeHero"] = "Trocar fundo";
        t["GH_Profile"] = "Perfil";
        t["GH_Playtime"] = "Tempo jogado";
        t["GH_LastPlayed"] = "Última vez";
        t["GH_Platform"] = "Plataforma";
        t["GH_GamepadConnected"] = "Controle conectado";
        t["GH_ItemsCount"] = "{0} itens";
        t["GH_EmptyLibrary"] = "Sua biblioteca está vazia. Use \"Detectar jogos\" para encontrar o que já está instalado, ou \"Adicionar\" para incluir um jogo ou aplicativo à mão.";
        t["GH_EmptyFilter"] = "Nenhum item corresponde à busca ou aos filtros selecionados.";
        t["GH_RemoveConfirm"] = "Remover \"{0}\" da biblioteca do Pulse1x?\n\nO jogo continua instalado no computador — apenas sai daqui.";
        t["GH_NeverPlayed"] = "Nunca aberto";
        t["GH_Today"] = "Hoje";
        t["GH_Yesterday"] = "Ontem";

        // ---- Plataformas ----
        t["GH_AllPlatforms"] = "Todas";
        t["GH_AllCategories"] = "Todas as categorias";
        t["GH_PlatformEmulator"] = "Emulador";
        t["GH_PlatformShortcut"] = "Atalho";
        t["GH_PlatformApp"] = "Aplicativo";
        t["GH_PlatformManual"] = "Manual";

        // ---- Ordenação ----
        t["GH_SortName"] = "Nome";
        t["GH_SortLastPlayed"] = "Última vez jogado";
        t["GH_SortPlaytime"] = "Tempo jogado";
        t["GH_SortAdded"] = "Adicionado recentemente";
        t["GH_SortLauncher"] = "Plataforma";

        // ---- Varredura ----
        t["GH_Scanning"] = "Procurando jogos instalados...";
        t["GH_ScanningSource"] = "Lendo {0}...";
        t["GH_ScanNothing"] = "Nada novo encontrado — a biblioteca já está em dia.";
        t["GH_ScanResult"] = "{0} adicionados · {1} atualizados · {2} removidos";
        t["GH_ScanFailed"] = "Não foi possível concluir a detecção: {0}";

        // ---- Sessão ----
        t["GH_Preparing"] = "Preparando o sistema...";
        t["GH_SessionRunning"] = "{0} em execução — o Pulse1x está em modo silencioso";
        t["GH_SessionEnded"] = "{0} encerrado · {1} min · configurações restauradas";
        t["GH_RecoveredTitle"] = "Configurações restauradas";
        t["GH_RecoveredBody"] = "O Pulse1x encontrou uma sessão de \"{0}\" que não foi encerrada normalmente e devolveu as configurações do sistema ao estado anterior.";

        // ---- Perfil: seções ----
        t["GH_SecPower"] = "Energia";
        t["GH_SecOem"] = "Notebook";
        t["GH_SecCpu"] = "CPU e processo";
        t["GH_SecMemory"] = "Memória";
        t["GH_SecLatency"] = "Latência";
        t["GH_SecNetwork"] = "Rede";
        t["GH_SecAudio"] = "Áudio";
        t["GH_SecDisplay"] = "Tela";
        t["GH_SecApps"] = "Aplicativos";
        t["GH_SecGeneral"] = "Geral";
        t["GH_SecSequence"] = "Sequência de inicialização";
        t["GH_ProfileNoChanges"] = "Não altera nada no sistema";
        t["GH_NoProfile"] = "Nenhum perfil";
        t["GH_NoProfileHint"] = "Este item inicia sem alterar as configurações do computador.";

        // ---- Perfil: modos ----
        t["GH_ModeUnchanged"] = "Não alterar";
        t["GH_ModeAuto"] = "Automático";
        t["GH_ModeCustom"] = "Personalizado";
        // Mostrado nas seções que estão em "Não alterar" ou "Automático": sem isso, os campos
        // esmaecidos passavam a impressão de estarem quebrados, em vez de desligados de propósito.
        t["GH_SectionLocked"] = "Mude o seletor acima para Personalizado para editar estes campos.";

        // ---- Perfil: comandos ----
        t["GH_ProfileName"] = "Nome do perfil";
        t["GH_Preset"] = "Começar a partir de";
        t["GH_ApplyPreset"] = "Aplicar preset";
        t["GH_SaveAsPreset"] = "Salvar como preset";
        t["GH_Save"] = "Salvar";
        t["GH_Cancel"] = "Cancelar";
        t["GH_RemoveProfile"] = "Remover perfil";
        t["GH_PresetApplied"] = "Preset \"{0}\" aplicado. Ajuste o que quiser antes de salvar.";
        t["GH_PresetSaved"] = "Preset \"{0}\" salvo e disponível para outros jogos.";

        // ---- Presets ----
        t["GH_PresetDefault"] = "Padrão";
        t["GH_PresetCompetitive"] = "Competitivo";
        t["GH_PresetMaxPerformance"] = "Máximo Desempenho";
        t["GH_PresetBalanced"] = "Balanceado";
        t["GH_PresetSilent"] = "Silencioso";
        t["GH_PresetPowerSaving"] = "Economia";

        // ---- Energia ----
        t["GH_Plan"] = "Plano de energia";
        t["GH_PlanBalanced"] = "Balanceado";
        t["GH_PlanPowerSaver"] = "Economia de energia";
        t["GH_UseUltimate"] = "Usar Ultimate Performance (cria o plano se não existir)";
        t["GH_UnhideAdvanced"] = "Exibir opções ocultas no Windows";
        t["GH_PwUnhidden"] = "As opções avançadas agora aparecem também no Painel de Controle do Windows.";
        t["GH_PwCpuMin"] = "Estado mínimo da CPU";
        t["GH_PwCpuMax"] = "Estado máximo da CPU";
        t["GH_PwBoost"] = "CPU Boost";
        t["GH_PwBoostOff"] = "Desativado";
        t["GH_PwBoostOn"] = "Ativado";
        t["GH_PwBoostAggressive"] = "Agressivo";
        t["GH_PwBoostEfficient"] = "Eficiente";
        t["GH_PwBoostEfficientAggressive"] = "Eficiente agressivo";
        t["GH_PwBoostAggressiveGuaranteed"] = "Agressivo (garantido)";
        t["GH_PwBoostEfficientAggressiveGuaranteed"] = "Eficiente agressivo (garantido)";
        t["GH_PwCooling"] = "Política de resfriamento";
        t["GH_PwCoolingPassive"] = "Passivo (reduz o clock antes da ventoinha)";
        t["GH_PwCoolingActive"] = "Ativo (acelera a ventoinha antes de reduzir o clock)";
        t["GH_PwPcie"] = "PCI Express (Link State)";
        t["GH_PwPcieOff"] = "Desligado";
        t["GH_PwPcieModerate"] = "Economia moderada";
        t["GH_PwPcieMax"] = "Economia máxima";
        t["GH_PwUsbSuspend"] = "Suspensão seletiva de USB";
        t["GH_PwDisabled"] = "Desabilitado";
        t["GH_PwEnabled"] = "Habilitado";
        t["GH_PwSleep"] = "Suspender após (min, 0 = nunca)";
        t["GH_PwVideo"] = "Desligar a tela após (min, 0 = nunca)";
        t["GH_PwDisk"] = "Desligar o disco após (min, 0 = nunca)";

        // ---- Fabricante ----
        t["GH_Vendor"] = "Software do fabricante";
        t["GH_DetectOem"] = "Detectar novamente";
        t["GH_OemNotDetected"] = "Nenhum software de fabricante compatível foi detectado nesta máquina.";
        t["GH_OemDetected"] = "{0} detectado — modo atual: {1}";
        t["GH_OemCustomVendor"] = "Comandos personalizados";
        t["GH_OemEco"] = "Eco";
        t["GH_OemQuiet"] = "Silencioso";
        t["GH_OemBalanced"] = "Balanceado";
        t["GH_OemPerformance"] = "Desempenho";
        t["GH_OemTurbo"] = "Turbo";

        // ---- CPU ----
        t["GH_Priority"] = "Prioridade do processo";
        t["GH_Affinity"] = "Afinidade de CPU";
        t["GH_CustomAffinity"] = "Escolher os núcleos manualmente";
        t["GH_ReserveFirstCore"] = "Reservar o primeiro núcleo para o sistema";
        t["GH_PrioIdle"] = "Ociosa";
        t["GH_PrioBelowNormal"] = "Abaixo do normal";
        t["GH_PrioNormal"] = "Normal";
        t["GH_PrioAboveNormal"] = "Acima do normal";
        t["GH_PrioHigh"] = "Alta";
        t["GH_PrioRealtime"] = "Tempo real (use com cautela)";

        // ---- Memória ----
        t["GH_OptimizeBefore"] = "Otimizar a RAM antes de iniciar";
        t["GH_OptimizeAfter"] = "Otimizar a RAM ao finalizar";

        // ---- Latência ----
        t["GH_TimerResolution"] = "Timer Resolution (ms)";
        t["GH_DisableUsbSuspend"] = "Desativar a suspensão seletiva de USB";
        t["GH_DisableWifiPower"] = "Desativar a economia de energia do Wi-Fi";
        t["GH_LowLatency"] = "Aplicar o perfil de baixa latência";

        // ---- Rede ----
        t["GH_NetworkProfile"] = "Perfil de rede";
        t["GH_FlushDns"] = "Limpar o cache de DNS antes de iniciar";
        t["GH_NetCompetitive"] = "Competitivo";
        t["GH_NetStability"] = "Estabilidade";
        t["GH_NetAllSafe"] = "Todas as otimizações seguras";

        // ---- Áudio ----
        t["GH_Volume"] = "Volume do Windows";
        t["GH_Mute"] = "Mudo";
        t["GH_OutputDevice"] = "Dispositivo de saída";
        t["GH_InputDevice"] = "Dispositivo de entrada";

        // ---- Tela ----
        t["GH_Display"] = "Monitor";
        t["GH_Brightness"] = "Brilho";
        t["GH_RefreshRate"] = "Taxa de atualização (Hz)";
        t["GH_Hdr"] = "HDR";

        // ---- Aplicativos ----
        t["GH_CloseBefore"] = "Fechar antes de iniciar";
        t["GH_StartWith"] = "Abrir junto com o jogo";
        t["GH_ReopenClosed"] = "Reabrir os aplicativos fechados ao finalizar";
        t["GH_CommandsBefore"] = "Comandos antes de iniciar";
        t["GH_CommandsAfter"] = "Comandos depois de finalizar";
        t["GH_Add"] = "Adicionar";
        t["GH_ProcessNameHint"] = "Nome do processo (ex.: chrome)";
        t["GH_CommandHint"] = "Comando ou script";

        // ---- Geral ----
        t["GH_RestoreOnExit"] = "Restaurar tudo automaticamente ao finalizar";
        t["GH_GamingMode"] = "Reduzir a atividade do Pulse1x durante o jogo";

        // ---- Sequência ----
        t["GH_ResetSteps"] = "Restaurar a ordem padrão";
        t["GH_MoveUp"] = "Subir";
        t["GH_MoveDown"] = "Descer";
        t["GH_SequenceHint"] = "Arraste a ordem das etapas para definir exatamente o que acontece antes de o jogo abrir.";
        t["GH_Step_SaveSnapshot"] = "Salvar o estado atual do sistema";
        t["GH_Step_PowerPlan"] = "Ativar o plano de energia";
        t["GH_Step_PowerAdvanced"] = "Aplicar as configurações avançadas de energia";
        t["GH_Step_OemMode"] = "Ajustar o modo do fabricante";
        t["GH_Step_Audio"] = "Ajustar o áudio";
        t["GH_Step_Display"] = "Ajustar a tela";
        t["GH_Step_TimerResolution"] = "Aplicar o Timer Resolution";
        t["GH_Step_LatencyTweaks"] = "Aplicar as otimizações de latência";
        t["GH_Step_MemoryOptimize"] = "Otimizar a memória RAM";
        t["GH_Step_NetworkProfile"] = "Aplicar o perfil de rede";
        t["GH_Step_CloseProcesses"] = "Fechar os processos selecionados";
        t["GH_Step_RunCommandsBefore"] = "Executar os comandos personalizados";
        t["GH_Step_StartApps"] = "Abrir os aplicativos selecionados";
        t["GH_Step_LaunchGame"] = "Iniciar o jogo";
        t["GH_Step_ProcessTuning"] = "Ajustar prioridade e afinidade do jogo";

        // ---- Etapas em execução ----
        t["GH_StepSnapshot"] = "Estado atual salvo";
        t["GH_StepPowerPlan"] = "Plano de energia aplicado";
        t["GH_StepPowerAdvanced"] = "Configurações avançadas de energia aplicadas";
        t["GH_StepOem"] = "Modo do fabricante aplicado";
        t["GH_StepOemUnavailable"] = "Software do fabricante indisponível — etapa ignorada";
        t["GH_StepAudio"] = "Áudio ajustado";
        t["GH_StepDisplay"] = "Tela ajustada";
        t["GH_StepTimer"] = "Timer Resolution aplicado";
        t["GH_StepLatency"] = "Otimizações de latência aplicadas";
        t["GH_StepMemory"] = "Memória otimizada";
        t["GH_StepNetwork"] = "Perfil de rede aplicado";
        t["GH_StepCloseApps"] = "Processos fechados";
        t["GH_StepCommands"] = "Comandos executados";
        t["GH_StepStartApps"] = "Aplicativos abertos";
        t["GH_StepLaunch"] = "Jogo iniciado";
        t["GH_StepProcessTuning"] = "Prioridade e afinidade ajustadas";
        t["GH_StepFailed"] = "Etapa não concluída";
        t["GH_StepRestored"] = "Configurações restauradas";

        // ---- Editor de item ----
        t["GH_Name"] = "Nome";
        t["GH_Executable"] = "Executável ou comando";
        t["GH_Arguments"] = "Argumentos de inicialização";
        t["GH_WorkingDirectory"] = "Diretório de trabalho";
        t["GH_Browse"] = "Procurar...";
        t["GH_IsApplication"] = "É um aplicativo (não um jogo)";
        t["GH_MarkFavorite"] = "Marcar como favorito";
        t["GH_ResetArt"] = "Buscar a arte novamente";
        t["GH_ArtReset"] = "Arte descartada — a busca automática vai preencher de novo.";
        t["GH_NameAndExeRequired"] = "Informe pelo menos o nome e o executável.";
        t["GH_ExeRequired"] = "Escolha o executável do emulador.";
        t["GH_PickExecutable"] = "Escolher o executável";
        t["GH_PickImage"] = "Escolher a imagem";

        // ---- Emuladores ----
        t["GH_EmulatorName"] = "Nome do emulador";
        t["GH_EmulatorExe"] = "Executável do emulador";
        t["GH_EmulatorDir"] = "Pasta do emulador";
        t["GH_RomsFolder"] = "Pasta das ROMs";
        t["GH_Extensions"] = "Extensões reconhecidas (ex.: nes, sfc, iso)";
        t["GH_ArgumentsTemplate"] = "Argumentos para iniciar uma ROM ({rom} = caminho do arquivo)";
        t["GH_EmulatorPlatform"] = "Plataforma exibida na biblioteca";
        t["GH_EmulatorScan"] = "Incluir as ROMs deste emulador na biblioteca";
        t["GH_EmulatorDefaultProfile"] = "Perfil padrão das ROMs";
        t["GH_NewEmulator"] = "Novo emulador";
        t["GH_EmulatorSaved"] = "Emulador \"{0}\" salvo. Use \"Detectar jogos\" para trazer as ROMs.";
        t["GH_EmulatorRemoveConfirm"] = "Remover o emulador \"{0}\" e as ROMs que ele trouxe para a biblioteca?";
        t["GH_PickEmulator"] = "Escolher o executável do emulador";
        t["GH_WhichEmulator"] = "Qual emulador você usa?";
        t["GH_EmuOther"] = "Outro emulador (configurar à mão)";
        t["GH_EmuAdvanced"] = "Configurações avançadas";
        t["GH_EmuHintRyujinx"] = "Escolha o Ryujinx.exe, na pasta do emulador.";
        t["GH_EmuHintEden"] = "Escolha o eden.exe, na pasta do emulador.";
        t["GH_EmuHintCitron"] = "Escolha o citron-cmd.exe — e não o citron.exe.";
        t["GH_EmuHintYuzu"] = "Escolha o executável principal do emulador (yuzu.exe, suyu.exe ou sudachi.exe).";
        t["GH_EmuNoteCitron"] = "O Citron só carrega a ROM pela linha de comando através do citron-cmd.exe. Apontar para o citron.exe faz o emulador abrir sem o jogo.";
        t["GH_EmuNoteRetroArch"] = "Troque {core} pelo caminho do core (.dll) do console que você quer emular.";
        t["GH_Close"] = "Fechar";
        t["GH_Delete"] = "Excluir";

        // ---- Personalização (Configurações) ----
        t["Settings_Personalization"] = "Personalização visual";
        t["Settings_PrimaryColor"] = "Cor principal";
        t["Settings_SecondaryColor"] = "Cor secundária";
        t["Settings_AccentColor"] = "Cor de destaque";
        t["Settings_Transparency"] = "Transparência";
        t["Settings_BlurIntensity"] = "Intensidade do blur";
        t["Settings_AnimationIntensity"] = "Intensidade das animações";
        t["Settings_Background"] = "Plano de fundo do aplicativo";
        t["Settings_BgDefault"] = "Padrão do Pulse1x";
        t["Settings_BgSolid"] = "Cor sólida";
        t["Settings_BgGradient"] = "Gradiente";
        t["Settings_BgImage"] = "Imagem personalizada";
        t["Settings_BgGame"] = "Baseado no jogo selecionado";
        t["Settings_BgImagePath"] = "Imagem";
        t["Settings_BgOpacity"] = "Opacidade";
        t["Settings_BgBlur"] = "Blur";
        t["Settings_BgDarken"] = "Escurecimento";
        t["Settings_BgSaturation"] = "Saturação";
        t["Settings_BgFit"] = "Preenchimento";
        t["Settings_BgFitFill"] = "Preencher";
        t["Settings_BgFitFit"] = "Ajustar";
        t["Settings_BgFitStretch"] = "Esticar";
        t["Settings_BgFitCenter"] = "Centralizar";
        t["Settings_BgFitTile"] = "Lado a lado";
        t["Settings_BgColor"] = "Cor do fundo";
        t["Settings_GradientStart"] = "Início do gradiente";
        t["Settings_GradientEnd"] = "Fim do gradiente";
        t["Settings_AdaptToGame"] = "Adaptar as cores da interface ao jogo selecionado";
        t["Settings_AdaptToGameHint"] = "Extrai a cor predominante da capa do jogo em destaque no GameHub e aplica suavemente aos detalhes da interface.";
        t["Settings_ResetAppearance"] = "Restaurar a aparência padrão";
    }

    private static void AddGameHubEnglish()
    {
        var t = En;

        t["Nav_GameHub"] = "GameHub";
        t["GH_Title"] = "GameHub";
        t["GH_Library"] = "Library";
        t["GH_EmulatorsTitle"] = "Emulators";
        t["GH_ProfileEditorTitle"] = "Game profile";
        t["GH_AddGameTitle"] = "Add game or application";
        t["GH_EditGameTitle"] = "Edit item";

        t["GH_Play"] = "Play";
        t["GH_PlayApp"] = "Open";
        t["GH_Stop"] = "End session";
        t["GH_Favorite"] = "Add to favorites";
        t["GH_Unfavorite"] = "Remove from favorites";
        t["GH_EditGame"] = "Edit";
        t["GH_EditProfile"] = "Profile";
        t["GH_Remove"] = "Remove";
        t["GH_Scan"] = "Detect games";
        t["GH_ScanShortcuts"] = "Scan shortcuts";
        t["GH_AddGame"] = "Add";
        t["GH_Emulators"] = "Emulators";
        t["GH_Search"] = "Search the library...";
        t["GH_Sort"] = "Sort by";
        t["GH_Category"] = "Category";
        t["GH_FavoritesOnly"] = "Favorites";
        t["GH_RecentOnly"] = "Recent";
        t["GH_ChangeCover"] = "Change cover";
        t["GH_ChangeHero"] = "Change background";
        t["GH_Profile"] = "Profile";
        t["GH_Playtime"] = "Playtime";
        t["GH_LastPlayed"] = "Last played";
        t["GH_Platform"] = "Platform";
        t["GH_GamepadConnected"] = "Controller connected";
        t["GH_ItemsCount"] = "{0} items";
        t["GH_EmptyLibrary"] = "Your library is empty. Use \"Detect games\" to find what is already installed, or \"Add\" to include a game or application manually.";
        t["GH_EmptyFilter"] = "No item matches the current search or filters.";
        t["GH_RemoveConfirm"] = "Remove \"{0}\" from the Pulse1x library?\n\nThe game stays installed on your computer — it only leaves this list.";
        t["GH_NeverPlayed"] = "Never opened";
        t["GH_Today"] = "Today";
        t["GH_Yesterday"] = "Yesterday";

        t["GH_AllPlatforms"] = "All";
        t["GH_AllCategories"] = "All categories";
        t["GH_PlatformEmulator"] = "Emulator";
        t["GH_PlatformShortcut"] = "Shortcut";
        t["GH_PlatformApp"] = "Application";
        t["GH_PlatformManual"] = "Manual";

        t["GH_SortName"] = "Name";
        t["GH_SortLastPlayed"] = "Last played";
        t["GH_SortPlaytime"] = "Playtime";
        t["GH_SortAdded"] = "Recently added";
        t["GH_SortLauncher"] = "Platform";

        t["GH_Scanning"] = "Looking for installed games...";
        t["GH_ScanningSource"] = "Reading {0}...";
        t["GH_ScanNothing"] = "Nothing new found — your library is up to date.";
        t["GH_ScanResult"] = "{0} added · {1} updated · {2} removed";
        t["GH_ScanFailed"] = "Detection could not be completed: {0}";

        t["GH_Preparing"] = "Preparing the system...";
        t["GH_SessionRunning"] = "{0} running — Pulse1x is in quiet mode";
        t["GH_SessionEnded"] = "{0} closed · {1} min · settings restored";
        t["GH_RecoveredTitle"] = "Settings restored";
        t["GH_RecoveredBody"] = "Pulse1x found a \"{0}\" session that did not end normally and returned your system settings to their previous state.";

        t["GH_SecPower"] = "Power";
        t["GH_SecOem"] = "Laptop";
        t["GH_SecCpu"] = "CPU and process";
        t["GH_SecMemory"] = "Memory";
        t["GH_SecLatency"] = "Latency";
        t["GH_SecNetwork"] = "Network";
        t["GH_SecAudio"] = "Audio";
        t["GH_SecDisplay"] = "Display";
        t["GH_SecApps"] = "Applications";
        t["GH_SecGeneral"] = "General";
        t["GH_SecSequence"] = "Startup sequence";
        t["GH_ProfileNoChanges"] = "Changes nothing on the system";
        t["GH_NoProfile"] = "No profile";
        t["GH_NoProfileHint"] = "This item starts without changing any computer setting.";

        t["GH_ModeUnchanged"] = "Leave unchanged";
        t["GH_ModeAuto"] = "Automatic";
        t["GH_ModeCustom"] = "Custom";
        t["GH_SectionLocked"] = "Switch the selector above to Custom to edit these fields.";

        t["GH_ProfileName"] = "Profile name";
        t["GH_Preset"] = "Start from";
        t["GH_ApplyPreset"] = "Apply preset";
        t["GH_SaveAsPreset"] = "Save as preset";
        t["GH_Save"] = "Save";
        t["GH_Cancel"] = "Cancel";
        t["GH_RemoveProfile"] = "Remove profile";
        t["GH_PresetApplied"] = "Preset \"{0}\" applied. Adjust anything you want before saving.";
        t["GH_PresetSaved"] = "Preset \"{0}\" saved and available to other games.";

        t["GH_PresetDefault"] = "Default";
        t["GH_PresetCompetitive"] = "Competitive";
        t["GH_PresetMaxPerformance"] = "Maximum Performance";
        t["GH_PresetBalanced"] = "Balanced";
        t["GH_PresetSilent"] = "Silent";
        t["GH_PresetPowerSaving"] = "Power Saving";

        t["GH_Plan"] = "Power plan";
        t["GH_PlanBalanced"] = "Balanced";
        t["GH_PlanPowerSaver"] = "Power saver";
        t["GH_UseUltimate"] = "Use Ultimate Performance (creates the plan if missing)";
        t["GH_UnhideAdvanced"] = "Show hidden options in Windows";
        t["GH_PwUnhidden"] = "The advanced options now also appear in the Windows Control Panel.";
        t["GH_PwCpuMin"] = "Minimum CPU state";
        t["GH_PwCpuMax"] = "Maximum CPU state";
        t["GH_PwBoost"] = "CPU Boost";
        t["GH_PwBoostOff"] = "Disabled";
        t["GH_PwBoostOn"] = "Enabled";
        t["GH_PwBoostAggressive"] = "Aggressive";
        t["GH_PwBoostEfficient"] = "Efficient";
        t["GH_PwBoostEfficientAggressive"] = "Efficient aggressive";
        t["GH_PwBoostAggressiveGuaranteed"] = "Aggressive (guaranteed)";
        t["GH_PwBoostEfficientAggressiveGuaranteed"] = "Efficient aggressive (guaranteed)";
        t["GH_PwCooling"] = "Cooling policy";
        t["GH_PwCoolingPassive"] = "Passive (lower the clock before the fan)";
        t["GH_PwCoolingActive"] = "Active (spin the fan before lowering the clock)";
        t["GH_PwPcie"] = "PCI Express (Link State)";
        t["GH_PwPcieOff"] = "Off";
        t["GH_PwPcieModerate"] = "Moderate power savings";
        t["GH_PwPcieMax"] = "Maximum power savings";
        t["GH_PwUsbSuspend"] = "USB selective suspend";
        t["GH_PwDisabled"] = "Disabled";
        t["GH_PwEnabled"] = "Enabled";
        t["GH_PwSleep"] = "Sleep after (min, 0 = never)";
        t["GH_PwVideo"] = "Turn off display after (min, 0 = never)";
        t["GH_PwDisk"] = "Turn off disk after (min, 0 = never)";

        t["GH_Vendor"] = "Manufacturer software";
        t["GH_DetectOem"] = "Detect again";
        t["GH_OemNotDetected"] = "No compatible manufacturer software was detected on this machine.";
        t["GH_OemDetected"] = "{0} detected — current mode: {1}";
        t["GH_OemCustomVendor"] = "Custom commands";
        t["GH_OemEco"] = "Eco";
        t["GH_OemQuiet"] = "Quiet";
        t["GH_OemBalanced"] = "Balanced";
        t["GH_OemPerformance"] = "Performance";
        t["GH_OemTurbo"] = "Turbo";

        t["GH_Priority"] = "Process priority";
        t["GH_Affinity"] = "CPU affinity";
        t["GH_CustomAffinity"] = "Pick the cores manually";
        t["GH_ReserveFirstCore"] = "Reserve the first core for the system";
        t["GH_PrioIdle"] = "Idle";
        t["GH_PrioBelowNormal"] = "Below normal";
        t["GH_PrioNormal"] = "Normal";
        t["GH_PrioAboveNormal"] = "Above normal";
        t["GH_PrioHigh"] = "High";
        t["GH_PrioRealtime"] = "Realtime (use with care)";

        t["GH_OptimizeBefore"] = "Optimize RAM before starting";
        t["GH_OptimizeAfter"] = "Optimize RAM after closing";

        t["GH_TimerResolution"] = "Timer Resolution (ms)";
        t["GH_DisableUsbSuspend"] = "Disable USB selective suspend";
        t["GH_DisableWifiPower"] = "Disable Wi-Fi power saving";
        t["GH_LowLatency"] = "Apply the low latency profile";

        t["GH_NetworkProfile"] = "Network profile";
        t["GH_FlushDns"] = "Flush the DNS cache before starting";
        t["GH_NetCompetitive"] = "Competitive";
        t["GH_NetStability"] = "Stability";
        t["GH_NetAllSafe"] = "All safe optimizations";

        t["GH_Volume"] = "Windows volume";
        t["GH_Mute"] = "Mute";
        t["GH_OutputDevice"] = "Output device";
        t["GH_InputDevice"] = "Input device";

        t["GH_Display"] = "Monitor";
        t["GH_Brightness"] = "Brightness";
        t["GH_RefreshRate"] = "Refresh rate (Hz)";
        t["GH_Hdr"] = "HDR";

        t["GH_CloseBefore"] = "Close before starting";
        t["GH_StartWith"] = "Open along with the game";
        t["GH_ReopenClosed"] = "Reopen the closed applications afterwards";
        t["GH_CommandsBefore"] = "Commands before starting";
        t["GH_CommandsAfter"] = "Commands after closing";
        t["GH_Add"] = "Add";
        t["GH_ProcessNameHint"] = "Process name (e.g. chrome)";
        t["GH_CommandHint"] = "Command or script";

        t["GH_RestoreOnExit"] = "Restore everything automatically when it closes";
        t["GH_GamingMode"] = "Reduce Pulse1x activity while playing";

        t["GH_ResetSteps"] = "Restore the default order";
        t["GH_MoveUp"] = "Move up";
        t["GH_MoveDown"] = "Move down";
        t["GH_SequenceHint"] = "Reorder the steps to define exactly what happens before the game opens.";
        t["GH_Step_SaveSnapshot"] = "Save the current system state";
        t["GH_Step_PowerPlan"] = "Activate the power plan";
        t["GH_Step_PowerAdvanced"] = "Apply the advanced power settings";
        t["GH_Step_OemMode"] = "Set the manufacturer mode";
        t["GH_Step_Audio"] = "Set up audio";
        t["GH_Step_Display"] = "Set up the display";
        t["GH_Step_TimerResolution"] = "Apply Timer Resolution";
        t["GH_Step_LatencyTweaks"] = "Apply the latency optimizations";
        t["GH_Step_MemoryOptimize"] = "Optimize RAM";
        t["GH_Step_NetworkProfile"] = "Apply the network profile";
        t["GH_Step_CloseProcesses"] = "Close the selected processes";
        t["GH_Step_RunCommandsBefore"] = "Run the custom commands";
        t["GH_Step_StartApps"] = "Open the selected applications";
        t["GH_Step_LaunchGame"] = "Start the game";
        t["GH_Step_ProcessTuning"] = "Set the game priority and affinity";

        t["GH_StepSnapshot"] = "Current state saved";
        t["GH_StepPowerPlan"] = "Power plan applied";
        t["GH_StepPowerAdvanced"] = "Advanced power settings applied";
        t["GH_StepOem"] = "Manufacturer mode applied";
        t["GH_StepOemUnavailable"] = "Manufacturer software unavailable — step skipped";
        t["GH_StepAudio"] = "Audio adjusted";
        t["GH_StepDisplay"] = "Display adjusted";
        t["GH_StepTimer"] = "Timer Resolution applied";
        t["GH_StepLatency"] = "Latency optimizations applied";
        t["GH_StepMemory"] = "Memory optimized";
        t["GH_StepNetwork"] = "Network profile applied";
        t["GH_StepCloseApps"] = "Processes closed";
        t["GH_StepCommands"] = "Commands executed";
        t["GH_StepStartApps"] = "Applications opened";
        t["GH_StepLaunch"] = "Game started";
        t["GH_StepProcessTuning"] = "Priority and affinity set";
        t["GH_StepFailed"] = "Step not completed";
        t["GH_StepRestored"] = "Settings restored";

        t["GH_Name"] = "Name";
        t["GH_Executable"] = "Executable or command";
        t["GH_Arguments"] = "Launch arguments";
        t["GH_WorkingDirectory"] = "Working directory";
        t["GH_Browse"] = "Browse...";
        t["GH_IsApplication"] = "This is an application (not a game)";
        t["GH_MarkFavorite"] = "Mark as favorite";
        t["GH_ResetArt"] = "Fetch the artwork again";
        t["GH_ArtReset"] = "Artwork discarded — the automatic search will fill it again.";
        t["GH_NameAndExeRequired"] = "Please provide at least the name and the executable.";
        t["GH_ExeRequired"] = "Choose the emulator's executable.";
        t["GH_PickExecutable"] = "Choose the executable";
        t["GH_PickImage"] = "Choose the image";

        t["GH_EmulatorName"] = "Emulator name";
        t["GH_EmulatorExe"] = "Emulator executable";
        t["GH_EmulatorDir"] = "Emulator folder";
        t["GH_RomsFolder"] = "ROMs folder";
        t["GH_Extensions"] = "Recognized extensions (e.g. nes, sfc, iso)";
        t["GH_ArgumentsTemplate"] = "Arguments to start a ROM ({rom} = file path)";
        t["GH_EmulatorPlatform"] = "Platform shown in the library";
        t["GH_EmulatorScan"] = "Include this emulator's ROMs in the library";
        t["GH_EmulatorDefaultProfile"] = "Default profile for the ROMs";
        t["GH_NewEmulator"] = "New emulator";
        t["GH_EmulatorSaved"] = "Emulator \"{0}\" saved. Use \"Detect games\" to bring in the ROMs.";
        t["GH_EmulatorRemoveConfirm"] = "Remove the emulator \"{0}\" and the ROMs it brought into the library?";
        t["GH_PickEmulator"] = "Choose the emulator executable";
        t["GH_WhichEmulator"] = "Which emulator do you use?";
        t["GH_EmuOther"] = "Another emulator (set up manually)";
        t["GH_EmuAdvanced"] = "Advanced settings";
        t["GH_EmuHintRyujinx"] = "Choose Ryujinx.exe, in the emulator's folder.";
        t["GH_EmuHintEden"] = "Choose eden.exe, in the emulator's folder.";
        t["GH_EmuHintCitron"] = "Choose citron-cmd.exe — not citron.exe.";
        t["GH_EmuHintYuzu"] = "Choose the emulator's main executable (yuzu.exe, suyu.exe or sudachi.exe).";
        t["GH_EmuNoteCitron"] = "Citron only loads the ROM from the command line through citron-cmd.exe. Pointing to citron.exe makes the emulator open without the game.";
        t["GH_EmuNoteRetroArch"] = "Replace {core} with the path to the core (.dll) for the console you want to emulate.";
        t["GH_Close"] = "Close";
        t["GH_Delete"] = "Delete";

        t["Settings_Personalization"] = "Visual personalization";
        t["Settings_PrimaryColor"] = "Primary color";
        t["Settings_SecondaryColor"] = "Secondary color";
        t["Settings_AccentColor"] = "Highlight color";
        t["Settings_Transparency"] = "Transparency";
        t["Settings_BlurIntensity"] = "Blur intensity";
        t["Settings_AnimationIntensity"] = "Animation intensity";
        t["Settings_Background"] = "Application background";
        t["Settings_BgDefault"] = "Pulse1x default";
        t["Settings_BgSolid"] = "Solid color";
        t["Settings_BgGradient"] = "Gradient";
        t["Settings_BgImage"] = "Custom image";
        t["Settings_BgGame"] = "Based on the selected game";
        t["Settings_BgImagePath"] = "Image";
        t["Settings_BgOpacity"] = "Opacity";
        t["Settings_BgBlur"] = "Blur";
        t["Settings_BgDarken"] = "Darkening";
        t["Settings_BgSaturation"] = "Saturation";
        t["Settings_BgFit"] = "Fill";
        t["Settings_BgFitFill"] = "Fill";
        t["Settings_BgFitFit"] = "Fit";
        t["Settings_BgFitStretch"] = "Stretch";
        t["Settings_BgFitCenter"] = "Center";
        t["Settings_BgFitTile"] = "Tile";
        t["Settings_BgColor"] = "Background color";
        t["Settings_GradientStart"] = "Gradient start";
        t["Settings_GradientEnd"] = "Gradient end";
        t["Settings_AdaptToGame"] = "Adapt the interface colors to the selected game";
        t["Settings_AdaptToGameHint"] = "Takes the dominant color from the highlighted game's cover in GameHub and applies it gently to the interface details.";
        t["Settings_ResetAppearance"] = "Restore the default appearance";
    }
}
