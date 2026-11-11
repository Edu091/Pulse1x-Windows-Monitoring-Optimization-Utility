namespace Pulse1x.App.Localization;

/// <summary>Vantagens e desvantagens ("&lt;chave&gt;_Pros" / "&lt;chave&gt;_Cons") das funções desta área.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddInfoSound()
    {
        var pt = Pt;
        var en = En;

        // Motor de áudio (Equalizer APO)
        pt["SndInfo_AudioEngine_Pros"] = "Libera microfone acima de 100%, equalizador, modificador de voz e mesa de sons em qualquer app, sem microfone virtual.";
        pt["SndInfo_AudioEngine_Cons"] = "Instala um componente dentro do som do Windows. Em alguns drivers ou após atualizações, o som pode falhar até reinstalar ou desativar o Equalizer APO.";
        en["SndInfo_AudioEngine_Pros"] = "Unlocks mic above 100%, equalizer, voice changer and soundboard in any app, with no virtual microphone.";
        en["SndInfo_AudioEngine_Cons"] = "Installs a component inside Windows audio. With some drivers or after updates, sound may break until you reinstall or disable Equalizer APO.";

        // Ativar em apps de chamada (modo Comunicações)
        pt["SndInfo_CallModes_Pros"] = "Volume extra, filtros e efeito de voz passam a valer também no Discord, WhatsApp, Teams e Meet.";
        pt["SndInfo_CallModes_Cons"] = "Reinicia o serviço de som do Windows: o áudio some por 1 ou 2 segundos. Alguns apps de chamada podem estranhar o processamento extra.";
        en["SndInfo_CallModes_Pros"] = "Extra volume, filters and voice effect also work in Discord, WhatsApp, Teams and Meet.";
        en["SndInfo_CallModes_Cons"] = "Restarts the Windows audio service: sound cuts out for 1 or 2 seconds. Some call apps may not like the extra processing.";

        // Definir / remover dispositivo padrão
        pt["SndInfo_DefaultDevice_Pros"] = "Jogos e apps passam a usar este fone ou microfone sem precisar escolher em cada um.";
        pt["SndInfo_DefaultDevice_Cons"] = "Vale para o Windows todo: apps que estavam tocando ou gravando podem trocar de dispositivo no meio do uso.";
        en["SndInfo_DefaultDevice_Pros"] = "Games and apps use this headset or mic without picking it in each one.";
        en["SndInfo_DefaultDevice_Cons"] = "Affects all of Windows: apps that were playing or recording may switch device mid-use.";

        // Volume do microfone acima de 100%
        pt["SndInfo_MicBoost_Pros"] = "Sua voz chega mais alta nas calls, mesmo com microfone fraco ou longe da boca.";
        pt["SndInfo_MicBoost_Cons"] = "Valores altos aumentam o chiado do ambiente e podem estourar (distorcer) quando você grita.";
        en["SndInfo_MicBoost_Pros"] = "Your voice comes through louder in calls, even with a weak mic or one far from your mouth.";
        en["SndInfo_MicBoost_Cons"] = "High values also raise room hiss and can clip (distort) when you shout.";

        // Amplificação de hardware
        pt["SndInfo_HwBoost_Pros"] = "Aumenta bastante o volume direto no driver, sem depender do motor de áudio.";
        pt["SndInfo_HwBoost_Cons"] = "Amplifica junto o chiado e os ruídos de fundo. Em níveis altos a voz pode distorcer.";
        en["SndInfo_HwBoost_Pros"] = "Raises volume a lot right in the driver, without needing the audio engine.";
        en["SndInfo_HwBoost_Cons"] = "Also amplifies hiss and background noise. At high levels your voice may distort.";

        // Filtros do microfone (ruído grave, clareza, silenciar entre falas)
        pt["SndInfo_MicFilters_Pros"] = "Tira o ronco de ventoinha e mesa, deixa a voz mais nítida e corta o ruído quando você está calado.";
        pt["SndInfo_MicFilters_Cons"] = "O silenciador pode comer o começo de palavras ditas baixinho. A voz pode ficar um pouco mais fina.";
        en["SndInfo_MicFilters_Pros"] = "Removes fan and desk rumble, makes your voice clearer and cuts noise while you're silent.";
        en["SndInfo_MicFilters_Cons"] = "The gate may swallow the start of quietly spoken words. Your voice may sound slightly thinner.";

        // Equalizador e perfis
        pt["SndInfo_Equalizer_Pros"] = "Destaca passos e tiros nos jogos ou dá mais peso a músicas e filmes, em qualquer app.";
        pt["SndInfo_Equalizer_Cons"] = "Vale para todo o som da saída escolhida. Graves e faixas muito altas podem distorcer, e o som 3D pode confundir a direção dos passos.";
        en["SndInfo_Equalizer_Pros"] = "Brings out footsteps and gunfire in games or adds punch to music and movies, in any app.";
        en["SndInfo_Equalizer_Cons"] = "Applies to all sound on the chosen output. Very high bass or bands can distort, and 3D sound can blur footstep direction.";

        // Volume extra (além de 100%)
        pt["SndInfo_OutputGain_Pros"] = "Deixa audível o jogo ou vídeo que continua baixo mesmo com o Windows em 100%.";
        pt["SndInfo_OutputGain_Cons"] = "Valores altos distorcem e podem dar um susto (ou machucar o ouvido) quando vem um som forte.";
        en["SndInfo_OutputGain_Pros"] = "Makes a game or video audible when it's still quiet with Windows at 100%.";
        en["SndInfo_OutputGain_Cons"] = "High values distort and can startle you (or hurt your ears) when a loud sound hits.";

        // Modificador de voz
        pt["SndInfo_VoiceChanger_Pros"] = "Muda sua voz direto no microfone, valendo no Discord, WhatsApp e jogos sem configurar nada neles.";
        pt["SndInfo_VoiceChanger_Cons"] = "Vale em todos os apps ao mesmo tempo. Efeitos de tom e robô atrasam a voz em cerca de 50 ms.";
        en["SndInfo_VoiceChanger_Pros"] = "Changes your voice right in the mic, working in Discord, WhatsApp and games without setting anything up in them.";
        en["SndInfo_VoiceChanger_Cons"] = "Applies to every app at once. Pitch and robot effects delay your voice by about 50 ms.";

        // Mesa de sons
        pt["SndInfo_Soundboard_Pros"] = "Toca sons e memes direto na call, junto com a sua voz, sem programa extra.";
        pt["SndInfo_Soundboard_Cons"] = "Sons altos podem estourar no microfone para quem ouve. Os atalhos Ctrl+Alt+1 a 9 deixam de funcionar em outros programas.";
        en["SndInfo_Soundboard_Pros"] = "Plays sounds and memes straight into the call along with your voice, with no extra program.";
        en["SndInfo_Soundboard_Cons"] = "Loud sounds may clip for listeners. The Ctrl+Alt+1 to 9 shortcuts stop working in other programs.";

        // Aplicar ao iniciar o Windows
        pt["SndInfo_ApplyOnStartup_Pros"] = "Seus ajustes voltam sozinhos a cada logon, mesmo se o Windows ou um driver resetar o microfone.";
        pt["SndInfo_ApplyOnStartup_Cons"] = "Adiciona uma tarefa rápida na inicialização do Windows.";
        en["SndInfo_ApplyOnStartup_Pros"] = "Your settings come back on every sign-in, even if Windows or a driver resets the mic.";
        en["SndInfo_ApplyOnStartup_Cons"] = "Adds a quick task to Windows startup.";

        // Travar o volume do microfone
        pt["SndInfo_LockMic_Pros"] = "O volume do microfone não muda sozinho quando Discord, Teams ou Zoom tentam ajustar.";
        pt["SndInfo_LockMic_Cons"] = "O ajuste automático de volume desses apps deixa de funcionar. Para mudar o volume, use o Pulse1x.";
        en["SndInfo_LockMic_Pros"] = "Your mic volume doesn't change on its own when Discord, Teams or Zoom try to adjust it.";
        en["SndInfo_LockMic_Cons"] = "Those apps' automatic volume adjustment stops working. To change the volume, use Pulse1x.";
    }
}
