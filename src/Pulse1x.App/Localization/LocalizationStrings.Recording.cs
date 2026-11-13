namespace Pulse1x.App.Localization;

/// <summary>Textos da categoria Gravação.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddRecordingPortuguese()
    {
        var t = Pt;
        t["Nav_Recording"] = "Gravação";
        t["Rec_PageTitle"] = "Gravação";
        t["Rec_PageSubtitle"] = "Grave a tela inteira ou só uma área, com o FPS, a qualidade e o codec que quiser, usando a placa de vídeo para não pesar no jogo. Atalhos funcionam de qualquer lugar, até com o Pulse1x na bandeja.";
        t["Rec_OpenFolder"] = "Abrir pasta";

        // Motor
        t["Rec_EngineTitle"] = "Motor de gravação";
        t["Rec_EngineExplain"] = "A captura e a codificação são feitas pelo FFmpeg, gratuito e de código aberto, que usa o mesmo recurso do Windows que a Game Bar e o OBS (Desktop Duplication) e os encoders da sua placa de vídeo. Se ele já estiver instalado no PC, o Pulse1x encontra sozinho.";
        t["Rec_EngineInstall"] = "Instalar FFmpeg";
        t["Rec_BrowseFfmpeg"] = "Escolher ffmpeg.exe";
        t["Rec_Cancel"] = "Cancelar";
        t["Rec_EngineMissing"] = "FFmpeg não encontrado. Clique em \"Instalar FFmpeg\" (cerca de 150 MB, baixado do GitHub) ou escolha um ffmpeg.exe que você já tenha.";
        t["Rec_EngineReady"] = "Pronto. FFmpeg {0}, captura por {1}.";
        t["Rec_EngineDownloading"] = "Baixando o FFmpeg...";
        t["Rec_EngineDownloadingPct"] = "Baixando o FFmpeg... {0}%";
        t["Rec_EngineDownloadFailed"] = "Não foi possível baixar o FFmpeg: {0}";
        t["RecInfo_Engine_Pros"] = "Grava com os encoders da placa de vídeo (NVENC, AMF, Quick Sync), quase sem custo de FPS, e não instala nada no Windows: é um único arquivo na pasta do Pulse1x.";
        t["RecInfo_Engine_Cons"] = "O download tem cerca de 150 MB. Sem placa de vídeo compatível, a gravação usa a CPU e pode pesar em jogos.";

        // Controle
        t["Rec_StateIdle"] = "Pronto para gravar";
        t["Rec_StateStarting"] = "Iniciando...";
        t["Rec_StateRecording"] = "Gravando";
        t["Rec_StatePaused"] = "Pausado";
        t["Rec_StateSaving"] = "Salvando o vídeo...";
        t["Rec_StateCountdown"] = "Contagem regressiva...";
        t["Rec_PausedShort"] = "PAUSA";
        t["Rec_Start"] = "Gravar";
        t["Rec_Stop"] = "Terminar";
        t["Rec_Pause"] = "Pausar";
        t["Rec_Resume"] = "Continuar";
        t["Rec_ActiveInfo"] = "{0}×{1} · {2} FPS · {3}";
        t["Rec_FallbackUsed"] = "o encoder escolhido não abriu, foi usado o próximo disponível";
        t["Rec_SavedAs"] = "Vídeo salvo: {0}";
        t["Rec_Estimate"] = "Vai gravar em {0}×{1} a {2} FPS. Até cerca de {3} por minuto (tela parada gera bem menos).";

        // O que gravar
        t["Rec_WhatTitle"] = "O que gravar";
        t["Rec_Monitor"] = "Monitor";
        t["Rec_MonitorName"] = "Monitor {0} ({1}×{2})";
        t["Rec_Primary"] = "principal";
        t["Rec_RefreshMonitors"] = "Atualizar a lista de monitores";
        t["Rec_Area"] = "Área";
        t["Rec_AreaFull"] = "Tela inteira";
        t["Rec_AreaRegion"] = "Só uma área da tela";
        t["Rec_SelectArea"] = "Selecionar área";
        t["Rec_RegionNone"] = "Nenhuma área escolhida.";
        t["Rec_RegionValue"] = "{0}×{1} a partir de ({2}, {3})";
        t["Rec_RegionHint"] = "Arraste para marcar a área a gravar. Esc cancela.";
        t["Rec_DrawMouse"] = "Mostrar o cursor do mouse no vídeo";

        // Vídeo
        t["Rec_VideoTitle"] = "Vídeo";
        t["Rec_Fps"] = "Quadros por segundo";
        t["Rec_Resolution"] = "Resolução";
        t["Rec_ResNative"] = "Nativa (igual à tela)";
        t["Rec_Codec"] = "Codec";
        t["Rec_CodecH264"] = "H.264 (abre em tudo)";
        t["Rec_CodecHevc"] = "HEVC / H.265 (arquivo menor)";
        t["Rec_CodecAv1"] = "AV1 (o menor, placas novas)";
        t["Rec_Encoder"] = "Aceleração por GPU";
        t["Rec_EncAuto"] = "Automática (recomendado)";
        t["Rec_EncCpu"] = "Desligada (usar a CPU)";
        t["Rec_Unavailable"] = "(indisponível)";
        t["Rec_EncoderHintGpu"] = "Aceleração disponível neste PC: {0}. No modo automático, a placa onde o monitor está ligado é usada primeiro.";
        t["Rec_EncoderHintCpu"] = "Nenhum encoder de placa de vídeo abriu para este codec. A gravação vai usar a CPU; prefira H.264 e qualidade Média em jogos.";
        t["Rec_Quality"] = "Qualidade";
        t["Rec_QualityLow"] = "Leve (arquivos pequenos)";
        t["Rec_QualityMedium"] = "Média";
        t["Rec_QualityHigh"] = "Alta (recomendada)";
        t["Rec_QualityUltra"] = "Máxima";
        t["Rec_QualityCustom"] = "Taxa fixa (Mbps)";
        t["Rec_Bitrate"] = "Taxa";
        t["Rec_Container"] = "Formato do arquivo";
        t["Rec_ContainerMp4"] = "MP4 (abre em qualquer lugar)";
        t["Rec_ContainerMkv"] = "MKV (não corrompe se o PC desligar)";
        t["Rec_VideoHint"] = "As qualidades ajustam a nitidez sozinhas conforme o movimento na tela. A taxa fixa é útil para enviar a sites com limite de tamanho.";

        // Áudio
        t["Rec_AudioTitle"] = "Áudio";
        t["Rec_SystemAudio"] = "Gravar o som do PC (jogo, vídeos, chamadas)";
        t["Rec_Microphone"] = "Gravar o microfone";
        t["Rec_MicDefault"] = "Microfone padrão do Windows";
        t["Rec_AudioSync"] = "Ajuste fino da sincronia do áudio";
        t["Rec_AudioHint"] = "O som do PC e o microfone viram uma única faixa no vídeo. Se a voz chegar antes ou depois da imagem, mova a sincronia (positivo atrasa o áudio).";

        // Atalhos
        t["Rec_HotkeysTitle"] = "Atalhos";
        t["Rec_HkStart"] = "Iniciar gravação";
        t["Rec_HkStop"] = "Terminar gravação";
        t["Rec_HkPause"] = "Pausar / continuar";
        t["Rec_HkOverlay"] = "Mostrar / ocultar a barra flutuante";
        t["Rec_HotkeyPress"] = "Pressione as teclas...";
        t["Rec_HotkeysHint"] = "Clique num atalho e pressione a combinação nova. Backspace deixa sem atalho; Esc cancela. Usar a mesma tecla em Iniciar e Terminar faz ela alternar.";
        t["Rec_HotkeyConflict"] = "Estes atalhos estão em uso por outro programa (ou repetidos) e não foram ativados: {0}. Escolha outra combinação.";
        t["Rec_RestoreHotkeys"] = "Restaurar atalhos padrão";
        t["RecInfo_Hotkeys_Pros"] = "Grava sem sair do jogo: os atalhos funcionam mesmo com o Pulse1x minimizado ou na bandeja.";
        t["RecInfo_Hotkeys_Cons"] = "Enquanto o Pulse1x estiver aberto, essas combinações deixam de chegar aos outros programas.";

        // Comportamento
        t["Rec_BehaviorTitle"] = "Arquivos e comportamento";
        t["Rec_Folder"] = "Pasta dos vídeos";
        t["Rec_ChangeFolder"] = "Alterar";
        t["Rec_DefaultFolder"] = "Padrão";
        t["Rec_DefaultFolderTip"] = "Volta para a pasta Downloads.";
        t["Rec_ChooseFolder"] = "Escolha onde salvar as gravações";
        t["Rec_Countdown"] = "Contagem antes de gravar";
        t["Rec_CountdownNone"] = "Sem contagem";
        t["Rec_CountdownSeconds"] = "{0} segundos";
        t["Rec_ShowIndicator"] = "Mostrar o indicador REC na tela (não aparece no vídeo)";
        t["Rec_SoundCues"] = "Bipe ao iniciar, pausar e terminar";
        t["Rec_OpenWhenDone"] = "Abrir a pasta ao terminar";

        // Game Bar
        t["Rec_GameBarTitle"] = "Xbox Game Bar";
        t["Rec_GameBarExplain"] = "A Game Bar do Windows grava em segundo plano e ocupa o atalho Win + G. Com a gravação do Pulse1x, dá para desligar a gravação contínua dela e recuperar um pouco de desempenho. Os dois ajustes são os mesmos da categoria Otimização e podem ser revertidos aqui ou lá.";
        t["Rec_GameDvrName"] = "Gravação em segundo plano (Game DVR)";
        t["Rec_GameDvrOn"] = "Ligada: o Windows grava a partida o tempo todo para \"gravar o que aconteceu\".";
        t["Rec_GameDvrOff"] = "Desligada pelo Pulse1x.";
        t["Rec_GameDvrDisable"] = "Desligar";
        t["Rec_GameBarName"] = "Sobreposição da Game Bar";
        t["Rec_GameBarOn"] = "Ligada: o botão Xbox do controle abre a Game Bar por cima do jogo.";
        t["Rec_GameBarOff"] = "Desligada pelo Pulse1x.";
        t["Rec_GameBarDisable"] = "Desligar";
        t["Rec_Revert"] = "Reverter";
        t["Rec_OpenGameBarSettings"] = "Configurações da Game Bar";
        t["Rec_OpenCaptureSettings"] = "Capturas do Windows";

        // Gravações recentes
        t["Rec_RecentTitle"] = "Gravações recentes";
        t["Rec_RecentEmpty"] = "Nenhuma gravação ainda. Os vídeos gravados pelo Pulse1x aparecem aqui.";
        t["Rec_LibraryInfo"] = "{0} vídeos · {1}";
        t["Rec_RefreshLibrary"] = "Atualizar a lista";
        t["Rec_DeleteAll"] = "Excluir todas";
        t["Rec_Play"] = "Assistir";
        t["Rec_ShowInFolder"] = "Mostrar na pasta";
        t["Rec_Delete"] = "Excluir";
        t["Rec_DeleteTitle"] = "Excluir gravação";
        t["Rec_DeleteConfirm"] = "Mandar \"{0}\" para a Lixeira?";
        t["Rec_DeleteAllConfirm"] = "Mandar as {0} gravações do Pulse1x desta pasta para a Lixeira?";

        // Barra flutuante e aviso
        t["Rec_ShowBar"] = "Mostrar barra flutuante";
        t["Rec_HideBar"] = "Ocultar barra flutuante";
        t["Rec_BarMove"] = "Arraste para mover";
        t["Rec_BarQuickSettings"] = "Configurações rápidas";
        t["Rec_BarHide"] = "Ocultar (mostre de novo com {0})";
        t["Rec_BarReady"] = "Pronto";
        t["Rec_BarStarting"] = "Iniciando";
        t["Rec_BarSaving"] = "Salvando";
        t["Rec_BarLockedWhileRecording"] = "Durante a gravação só dá para silenciar o microfone. O resto vale para a próxima.";
        t["Rec_BarSystemAudio"] = "Som do PC";
        t["Rec_BarMicrophone"] = "Microfone";
        t["Rec_BarMicMute"] = "Silenciar microfone";
        t["Rec_BarQualityLow"] = "Leve";
        t["Rec_BarQualityHigh"] = "Alta";
        t["Rec_BarResNative"] = "Nativa";
        t["Rec_BarArea"] = "Selecionar área";
        t["Rec_BarMoreSettings"] = "Mais configurações no Pulse1x";
        t["Rec_ToastSaved"] = "Gravação salva";
        t["Rec_ToastStopped"] = "A gravação parou";
        t["Rec_ToastFailed"] = "Não foi possível gravar";
        t["Rec_ToastOpen"] = "Abrir no Pulse1x";
        t["Rec_OverlayMicMute"] = "Silenciar o microfone na gravação";

        // Erros
        t["Rec_ErrNoEngine"] = "Instale o FFmpeg na categoria Gravação para poder gravar.";
        t["Rec_ErrNoMonitor"] = "Nenhum monitor encontrado para gravar.";
        t["Rec_ErrFolder"] = "Não foi possível usar a pasta dos vídeos. Escolha outra.";
        t["Rec_ErrNotRecording"] = "Não há gravação em andamento.";
        t["Rec_ErrStart"] = "A gravação não conseguiu começar com nenhum encoder. Atualize o driver da placa de vídeo ou escolha \"Desligada (usar a CPU)\".";
        t["Rec_ErrCrashed"] = "A gravação parou sozinha (troca de resolução ou do driver de vídeo). O que já tinha sido gravado foi salvo.";
        t["Rec_ErrEmpty"] = "Nada foi gravado.";
        t["Rec_ErrFinalize"] = "Não foi possível montar o vídeo final. Os trechos gravados continuam nesta pasta oculta:";
        t["Rec_ErrOpen"] = "O Windows não tem um programa para abrir este vídeo.";
        t["Rec_ErrDelete"] = "Não foi possível excluir. O vídeo pode estar aberto em outro programa.";
    }

    internal static void AddRecordingEnglish()
    {
        var t = En;
        t["Nav_Recording"] = "Recording";
        t["Rec_PageTitle"] = "Recording";
        t["Rec_PageSubtitle"] = "Record the whole screen or just an area, at the FPS, quality and codec you want, using your graphics card so games don't slow down. Hotkeys work from anywhere, even with Pulse1x in the tray.";
        t["Rec_OpenFolder"] = "Open folder";

        t["Rec_EngineTitle"] = "Recording engine";
        t["Rec_EngineExplain"] = "Capture and encoding are done by FFmpeg, free and open source, which uses the same Windows feature as the Game Bar and OBS (Desktop Duplication) plus your graphics card's encoders. If it's already installed, Pulse1x finds it on its own.";
        t["Rec_EngineInstall"] = "Install FFmpeg";
        t["Rec_BrowseFfmpeg"] = "Choose ffmpeg.exe";
        t["Rec_Cancel"] = "Cancel";
        t["Rec_EngineMissing"] = "FFmpeg not found. Click \"Install FFmpeg\" (about 150 MB, downloaded from GitHub) or pick an ffmpeg.exe you already have.";
        t["Rec_EngineReady"] = "Ready. FFmpeg {0}, capturing with {1}.";
        t["Rec_EngineDownloading"] = "Downloading FFmpeg...";
        t["Rec_EngineDownloadingPct"] = "Downloading FFmpeg... {0}%";
        t["Rec_EngineDownloadFailed"] = "Couldn't download FFmpeg: {0}";
        t["RecInfo_Engine_Pros"] = "Records with your graphics card's encoders (NVENC, AMF, Quick Sync) at almost no FPS cost, and installs nothing in Windows: it's a single file in the Pulse1x folder.";
        t["RecInfo_Engine_Cons"] = "The download is about 150 MB. Without a compatible graphics card, recording uses the CPU and can slow games down.";

        t["Rec_StateIdle"] = "Ready to record";
        t["Rec_StateStarting"] = "Starting...";
        t["Rec_StateRecording"] = "Recording";
        t["Rec_StatePaused"] = "Paused";
        t["Rec_StateSaving"] = "Saving the video...";
        t["Rec_StateCountdown"] = "Countdown...";
        t["Rec_PausedShort"] = "PAUSED";
        t["Rec_Start"] = "Record";
        t["Rec_Stop"] = "Stop";
        t["Rec_Pause"] = "Pause";
        t["Rec_Resume"] = "Resume";
        t["Rec_ActiveInfo"] = "{0}×{1} · {2} FPS · {3}";
        t["Rec_FallbackUsed"] = "the chosen encoder didn't open, the next available one was used";
        t["Rec_SavedAs"] = "Video saved: {0}";
        t["Rec_Estimate"] = "Will record at {0}×{1}, {2} FPS. Up to about {3} per minute (a still screen uses much less).";

        t["Rec_WhatTitle"] = "What to record";
        t["Rec_Monitor"] = "Monitor";
        t["Rec_MonitorName"] = "Monitor {0} ({1}×{2})";
        t["Rec_Primary"] = "primary";
        t["Rec_RefreshMonitors"] = "Refresh the monitor list";
        t["Rec_Area"] = "Area";
        t["Rec_AreaFull"] = "Whole screen";
        t["Rec_AreaRegion"] = "Just an area of the screen";
        t["Rec_SelectArea"] = "Select area";
        t["Rec_RegionNone"] = "No area selected.";
        t["Rec_RegionValue"] = "{0}×{1} starting at ({2}, {3})";
        t["Rec_RegionHint"] = "Drag to mark the area to record. Esc cancels.";
        t["Rec_DrawMouse"] = "Show the mouse cursor in the video";

        t["Rec_VideoTitle"] = "Video";
        t["Rec_Fps"] = "Frames per second";
        t["Rec_Resolution"] = "Resolution";
        t["Rec_ResNative"] = "Native (same as the screen)";
        t["Rec_Codec"] = "Codec";
        t["Rec_CodecH264"] = "H.264 (plays everywhere)";
        t["Rec_CodecHevc"] = "HEVC / H.265 (smaller file)";
        t["Rec_CodecAv1"] = "AV1 (smallest, newer cards)";
        t["Rec_Encoder"] = "GPU acceleration";
        t["Rec_EncAuto"] = "Automatic (recommended)";
        t["Rec_EncCpu"] = "Off (use the CPU)";
        t["Rec_Unavailable"] = "(unavailable)";
        t["Rec_EncoderHintGpu"] = "Acceleration available on this PC: {0}. In automatic mode, the card the monitor is connected to is tried first.";
        t["Rec_EncoderHintCpu"] = "No graphics card encoder opened for this codec. Recording will use the CPU; prefer H.264 and Medium quality in games.";
        t["Rec_Quality"] = "Quality";
        t["Rec_QualityLow"] = "Light (small files)";
        t["Rec_QualityMedium"] = "Medium";
        t["Rec_QualityHigh"] = "High (recommended)";
        t["Rec_QualityUltra"] = "Maximum";
        t["Rec_QualityCustom"] = "Fixed bitrate (Mbps)";
        t["Rec_Bitrate"] = "Bitrate";
        t["Rec_Container"] = "File format";
        t["Rec_ContainerMp4"] = "MP4 (opens anywhere)";
        t["Rec_ContainerMkv"] = "MKV (survives a power loss)";
        t["Rec_VideoHint"] = "The quality presets adjust sharpness to the amount of motion on screen. A fixed bitrate helps when uploading to sites with a size limit.";

        t["Rec_AudioTitle"] = "Audio";
        t["Rec_SystemAudio"] = "Record PC sound (game, videos, calls)";
        t["Rec_Microphone"] = "Record the microphone";
        t["Rec_MicDefault"] = "Windows default microphone";
        t["Rec_AudioSync"] = "Audio sync fine-tuning";
        t["Rec_AudioHint"] = "PC sound and microphone become a single track in the video. If the voice arrives before or after the picture, move the sync (positive delays the audio).";

        t["Rec_HotkeysTitle"] = "Hotkeys";
        t["Rec_HkStart"] = "Start recording";
        t["Rec_HkStop"] = "Stop recording";
        t["Rec_HkPause"] = "Pause / resume";
        t["Rec_HkOverlay"] = "Show / hide the floating bar";
        t["Rec_HotkeyPress"] = "Press the keys...";
        t["Rec_HotkeysHint"] = "Click a hotkey and press the new combination. Backspace clears it; Esc cancels. Using the same key for Start and Stop makes it toggle.";
        t["Rec_HotkeyConflict"] = "These hotkeys are in use by another program (or repeated) and weren't enabled: {0}. Pick another combination.";
        t["Rec_RestoreHotkeys"] = "Restore default hotkeys";
        t["RecInfo_Hotkeys_Pros"] = "Record without leaving your game: hotkeys work even with Pulse1x minimized or in the tray.";
        t["RecInfo_Hotkeys_Cons"] = "While Pulse1x is open, these combinations no longer reach other programs.";

        t["Rec_BehaviorTitle"] = "Files and behavior";
        t["Rec_Folder"] = "Video folder";
        t["Rec_ChangeFolder"] = "Change";
        t["Rec_DefaultFolder"] = "Default";
        t["Rec_DefaultFolderTip"] = "Goes back to the Downloads folder.";
        t["Rec_ChooseFolder"] = "Choose where to save recordings";
        t["Rec_Countdown"] = "Countdown before recording";
        t["Rec_CountdownNone"] = "No countdown";
        t["Rec_CountdownSeconds"] = "{0} seconds";
        t["Rec_ShowIndicator"] = "Show the REC indicator on screen (not in the video)";
        t["Rec_SoundCues"] = "Beep on start, pause and stop";
        t["Rec_OpenWhenDone"] = "Open the folder when done";

        t["Rec_GameBarTitle"] = "Xbox Game Bar";
        t["Rec_GameBarExplain"] = "The Windows Game Bar records in the background and takes the Win + G shortcut. With Pulse1x recording, you can turn off its continuous recording and get some performance back. Both settings are the same as in the Optimization category and can be reverted here or there.";
        t["Rec_GameDvrName"] = "Background recording (Game DVR)";
        t["Rec_GameDvrOn"] = "On: Windows records your match all the time for \"record what happened\".";
        t["Rec_GameDvrOff"] = "Turned off by Pulse1x.";
        t["Rec_GameDvrDisable"] = "Turn off";
        t["Rec_GameBarName"] = "Game Bar overlay";
        t["Rec_GameBarOn"] = "On: the controller's Xbox button opens the Game Bar over your game.";
        t["Rec_GameBarOff"] = "Turned off by Pulse1x.";
        t["Rec_GameBarDisable"] = "Turn off";
        t["Rec_Revert"] = "Revert";
        t["Rec_OpenGameBarSettings"] = "Game Bar settings";
        t["Rec_OpenCaptureSettings"] = "Windows captures";

        t["Rec_RecentTitle"] = "Recent recordings";
        t["Rec_RecentEmpty"] = "No recordings yet. Videos recorded by Pulse1x show up here.";
        t["Rec_LibraryInfo"] = "{0} videos · {1}";
        t["Rec_RefreshLibrary"] = "Refresh the list";
        t["Rec_DeleteAll"] = "Delete all";
        t["Rec_Play"] = "Watch";
        t["Rec_ShowInFolder"] = "Show in folder";
        t["Rec_Delete"] = "Delete";
        t["Rec_DeleteTitle"] = "Delete recording";
        t["Rec_DeleteConfirm"] = "Send \"{0}\" to the Recycle Bin?";
        t["Rec_DeleteAllConfirm"] = "Send the {0} Pulse1x recordings in this folder to the Recycle Bin?";

        t["Rec_OverlayMicMute"] = "Mute the microphone in the recording";
        t["Rec_ShowBar"] = "Show floating bar";
        t["Rec_HideBar"] = "Hide floating bar";
        t["Rec_BarMove"] = "Drag to move";
        t["Rec_BarQuickSettings"] = "Quick settings";
        t["Rec_BarHide"] = "Hide (show it again with {0})";
        t["Rec_BarReady"] = "Ready";
        t["Rec_BarStarting"] = "Starting";
        t["Rec_BarSaving"] = "Saving";
        t["Rec_BarLockedWhileRecording"] = "While recording you can only mute the microphone. The rest applies to the next one.";
        t["Rec_BarSystemAudio"] = "PC sound";
        t["Rec_BarMicrophone"] = "Microphone";
        t["Rec_BarMicMute"] = "Mute microphone";
        t["Rec_BarQualityLow"] = "Light";
        t["Rec_BarQualityHigh"] = "High";
        t["Rec_BarResNative"] = "Native";
        t["Rec_BarArea"] = "Select area";
        t["Rec_BarMoreSettings"] = "More settings in Pulse1x";
        t["Rec_ToastSaved"] = "Recording saved";
        t["Rec_ToastStopped"] = "Recording stopped";
        t["Rec_ToastFailed"] = "Couldn't record";
        t["Rec_ToastOpen"] = "Open in Pulse1x";

        t["Rec_ErrNoEngine"] = "Install FFmpeg in the Recording category to record.";
        t["Rec_ErrNoMonitor"] = "No monitor found to record.";
        t["Rec_ErrFolder"] = "Couldn't use the video folder. Choose another one.";
        t["Rec_ErrNotRecording"] = "No recording in progress.";
        t["Rec_ErrStart"] = "Recording couldn't start with any encoder. Update your graphics driver or choose \"Off (use the CPU)\".";
        t["Rec_ErrCrashed"] = "Recording stopped on its own (resolution or graphics driver change). What had been recorded was saved.";
        t["Rec_ErrEmpty"] = "Nothing was recorded.";
        t["Rec_ErrFinalize"] = "Couldn't build the final video. The recorded parts are still in this hidden folder:";
        t["Rec_ErrOpen"] = "Windows has no program to open this video.";
        t["Rec_ErrDelete"] = "Couldn't delete. The video may be open in another program.";
    }
}
