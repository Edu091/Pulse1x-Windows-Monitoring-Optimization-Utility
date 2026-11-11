namespace Pulse1x.App.Localization;

/// <summary>Vantagens e desvantagens ("&lt;chave&gt;_Pros" / "&lt;chave&gt;_Cons") das funções desta área.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddInfoAdvanced()
    {
        var pt = Pt;
        var en = En;

        // ---------- PRIVACIDADE ----------

        pt["AdvInfo_copilot_Pros"] = "O botão do Copilot some da barra de tarefas e o assistente integrado fica desligado. O ganho é de privacidade e de barra limpa, não de FPS.";
        pt["AdvInfo_copilot_Cons"] = "Você perde o assistente de IA do Windows; para usar o Copilot, só pelo navegador ou revertendo.";
        en["AdvInfo_copilot_Pros"] = "The Copilot button disappears from the taskbar and the built-in assistant is turned off. The gain is privacy and a cleaner taskbar, not FPS.";
        en["AdvInfo_copilot_Cons"] = "You lose the Windows AI assistant; to use Copilot you'll need the browser or to revert this.";

        pt["AdvInfo_cortana_Pros"] = "A Cortana deixa de iniciar com o Windows e de rodar em segundo plano. No Windows 11 atual ela quase não existe mais, então o ganho costuma ser pequeno.";
        pt["AdvInfo_cortana_Cons"] = "A Cortana para de responder a comandos de voz e pesquisas enquanto estiver desativada.";
        en["AdvInfo_cortana_Pros"] = "Cortana stops starting with Windows and running in the background. On current Windows 11 it is mostly gone already, so the gain is usually small.";
        en["AdvInfo_cortana_Cons"] = "Cortana stops answering voice commands and searches while this is on.";

        pt["AdvInfo_telemetry_Pros"] = "O Windows envia à Microsoft só o mínimo de dados de diagnóstico. É um ajuste de privacidade; não espere ganho de desempenho.";
        pt["AdvInfo_telemetry_Cons"] = "O Programa Windows Insider (versões de teste) exige esses dados e deixa de funcionar. As atualizações normais continuam chegando.";
        en["AdvInfo_telemetry_Pros"] = "Windows sends Microsoft only the minimum diagnostic data. It's a privacy tweak; don't expect a performance gain.";
        en["AdvInfo_telemetry_Cons"] = "The Windows Insider Program (preview builds) requires this data and stops working. Regular updates keep arriving.";

        pt["AdvInfo_telemetry-tasks_Pros"] = "Param algumas tarefas que rodam de tempos em tempos para coletar dados, evitando picos curtos de disco e CPU, às vezes no meio do jogo.";
        pt["AdvInfo_telemetry-tasks_Cons"] = "Nenhuma relevante no uso comum. A checagem de compatibilidade para grandes atualizações do Windows fica parada, o que pode atrasar a oferta de uma versão nova.";
        en["AdvInfo_telemetry-tasks_Pros"] = "Stops a few tasks that run every so often to collect data, avoiding short disk and CPU spikes, sometimes in the middle of a game.";
        en["AdvInfo_telemetry-tasks_Cons"] = "Nothing relevant for everyday use. The compatibility check for major Windows upgrades stops running, which may delay a new version being offered.";

        pt["AdvInfo_nvidia-telemetry_Pros"] = "O driver da NVIDIA para de enviar relatórios de uso. A placa e os jogos funcionam igual; o ganho é de privacidade, não de desempenho.";
        pt["AdvInfo_nvidia-telemetry_Cons"] = "Nenhuma relevante. Em drivers antigos, alguns recursos do GeForce Experience podiam reclamar da falta do serviço; nesse caso, reverta.";
        en["AdvInfo_nvidia-telemetry_Pros"] = "The NVIDIA driver stops sending usage reports. The card and your games work the same; the gain is privacy, not performance.";
        en["AdvInfo_nvidia-telemetry_Cons"] = "Nothing relevant. On older drivers, some GeForce Experience features could complain about the missing service; if so, revert.";

        pt["AdvInfo_suggestions_Pros"] = "Somem as sugestões e propagandas do menu Iniciar, as dicas automáticas e a instalação silenciosa de apps promovidos pela Microsoft.";
        pt["AdvInfo_suggestions_Cons"] = "Nenhuma relevante: você só deixa de ver recomendações de apps e dicas do Windows.";
        en["AdvInfo_suggestions_Pros"] = "Start menu suggestions and ads, automatic tips and the silent install of apps promoted by Microsoft go away.";
        en["AdvInfo_suggestions_Cons"] = "Nothing relevant: you just stop seeing app recommendations and Windows tips.";

        pt["AdvInfo_browser-telemetry_Pros"] = "Edge e Chrome deixam de enviar estatísticas de uso e diagnóstico à Microsoft e ao Google. Favoritos, senhas e histórico não são tocados.";
        pt["AdvInfo_browser-telemetry_Cons"] = "O navegador passa a mostrar \"gerenciado pela sua organização\", porque o ajuste é feito por política. Isso não muda o uso.";
        en["AdvInfo_browser-telemetry_Pros"] = "Edge and Chrome stop sending usage and diagnostic statistics to Microsoft and Google. Bookmarks, passwords and history are not touched.";
        en["AdvInfo_browser-telemetry_Cons"] = "The browser shows \"managed by your organization\", because the change is made through a policy. It doesn't affect normal use.";

        pt["AdvInfo_notifications_Pros"] = "Nenhuma notificação aparece por cima do jogo ou do trabalho: acabam os pop-ups no canto da tela.";
        pt["AdvInfo_notifications_Cons"] = "Você também deixa de ver avisos que podem importar, como mensagens do Discord, lembretes e alertas do sistema, até reverter.";
        en["AdvInfo_notifications_Pros"] = "No notification pops up over your game or work: no more pop-ups in the corner of the screen.";
        en["AdvInfo_notifications_Cons"] = "You also miss alerts that may matter, such as Discord messages, reminders and system warnings, until you revert.";

        pt["AdvInfo_recall_Pros"] = "O Windows para de tirar fotos da sua tela e o componente do Recall é removido: conversas, senhas e páginas de banco deixam de ser gravadas.";
        pt["AdvInfo_recall_Cons"] = "As capturas já salvas são apagadas e a busca do tipo \"o que eu vi no PC\" deixa de existir. Para usar de novo, é preciso reverter e reativar o Recall.";
        en["AdvInfo_recall_Pros"] = "Windows stops taking pictures of your screen and the Recall component is removed: chats, passwords and banking pages are no longer recorded.";
        en["AdvInfo_recall_Cons"] = "Snapshots already saved are deleted and \"what did I see on my PC\" search is gone. To use it again you must revert and turn Recall back on.";

        pt["AdvInfo_advertising-id_Pros"] = "Os apps deixam de usar um identificador único do seu Windows para montar um perfil seu e mostrar anúncios direcionados.";
        pt["AdvInfo_advertising-id_Cons"] = "Nenhuma relevante: a quantidade de anúncios é a mesma, só que sem relação com o que você faz.";
        en["AdvInfo_advertising-id_Pros"] = "Apps stop using a unique ID of your Windows to build a profile of you and show targeted ads.";
        en["AdvInfo_advertising-id_Cons"] = "Nothing relevant: you see the same amount of ads, just not based on what you do.";

        pt["AdvInfo_tailored-experiences_Pros"] = "A Microsoft deixa de usar seus dados de diagnóstico para escolher dicas e ofertas para você dentro do Windows.";
        pt["AdvInfo_tailored-experiences_Cons"] = "Nenhuma relevante: você só deixa de ver recomendações personalizadas.";
        en["AdvInfo_tailored-experiences_Pros"] = "Microsoft stops using your diagnostic data to pick tips and offers for you inside Windows.";
        en["AdvInfo_tailored-experiences_Cons"] = "Nothing relevant: you just stop seeing personalized recommendations.";

        pt["AdvInfo_diagtrack-service_Pros"] = "Para o serviço que coleta e envia dados de uso à Microsoft o tempo todo, poupando um pouco de CPU, disco e rede. O ganho costuma ser pequeno.";
        pt["AdvInfo_diagtrack-service_Cons"] = "O Hub de Feedback, alguns relatórios de diagnóstico e o Programa Windows Insider param de funcionar. Atualizações e segurança não são afetadas.";
        en["AdvInfo_diagtrack-service_Pros"] = "Stops the service that constantly collects and sends usage data to Microsoft, saving a bit of CPU, disk and network. The gain is usually small.";
        en["AdvInfo_diagtrack-service_Cons"] = "Feedback Hub, some diagnostic reports and the Windows Insider Program stop working. Updates and security are not affected.";

        pt["AdvInfo_start-web-search_Pros"] = "O que você digita no menu Iniciar não vai mais para o Bing, e a busca fica mais rápida, mostrando só o que está no PC.";
        pt["AdvInfo_start-web-search_Cons"] = "Você perde os resultados da internet no Iniciar; para pesquisar na web, use o navegador.";
        en["AdvInfo_start-web-search_Pros"] = "What you type in the Start menu no longer goes to Bing, and search gets faster, showing only what's on your PC.";
        en["AdvInfo_start-web-search_Cons"] = "You lose web results in Start; to search the web, use your browser.";

        pt["AdvInfo_lockscreen-ads_Pros"] = "Somem os textos, dicas e propagandas de jogos e apps que aparecem na tela de bloqueio.";
        pt["AdvInfo_lockscreen-ads_Cons"] = "Nenhuma relevante: só somem as dicas e curiosidades exibidas sobre a imagem.";
        en["AdvInfo_lockscreen-ads_Pros"] = "The text, tips and game and app ads shown on the lock screen go away.";
        en["AdvInfo_lockscreen-ads_Cons"] = "Nothing relevant: only the tips and fun facts shown over the picture go away.";

        pt["AdvInfo_feedback-requests_Pros"] = "Para de aparecer a janela em que o Windows pergunta o que você achou de algum recurso.";
        pt["AdvInfo_feedback-requests_Cons"] = "Nenhuma relevante: o Hub de Feedback continua disponível se você quiser abri-lo.";
        en["AdvInfo_feedback-requests_Pros"] = "The window where Windows asks what you think of some feature stops showing up.";
        en["AdvInfo_feedback-requests_Cons"] = "Nothing relevant: Feedback Hub is still there if you want to open it yourself.";

        pt["AdvInfo_activity-history_Pros"] = "O Windows para de registrar e enviar à sua conta Microsoft a lista de apps e arquivos que você abre.";
        pt["AdvInfo_activity-history_Cons"] = "O \"continuar de onde parei\" em outro dispositivo e a Linha do Tempo (onde ainda existir) deixam de funcionar.";
        en["AdvInfo_activity-history_Pros"] = "Windows stops recording, and sending to your Microsoft account, the list of apps and files you open.";
        en["AdvInfo_activity-history_Cons"] = "\"Pick up where you left off\" on another device and Timeline (where it still exists) stop working.";

        // ---------- DESEMPENHO (SERVIÇOS) ----------

        pt["AdvInfo_xbox-services_Pros"] = "Quatro serviços do Xbox deixam de iniciar, liberando um pouco de memória em segundo plano. O ganho é pequeno e só vale para quem não usa nada do Xbox.";
        pt["AdvInfo_xbox-services_Cons"] = "Jogos do Game Pass e da Microsoft Store podem não entrar na conta Xbox, os saves na nuvem param de sincronizar e acessórios Xbox perdem configurações e atualizações. Se você joga pelo app Xbox, não ative.";
        en["AdvInfo_xbox-services_Pros"] = "Four Xbox services stop starting, freeing a bit of background memory. The gain is small and only makes sense if you use nothing from Xbox.";
        en["AdvInfo_xbox-services_Cons"] = "Game Pass and Microsoft Store games may fail to sign in to Xbox, cloud saves stop syncing and Xbox accessories lose settings and updates. If you play through the Xbox app, don't turn this on.";

        pt["AdvInfo_fax-service_Pros"] = "Desliga um serviço antigo de fax que quase ninguém usa. O ganho é mínimo; é mais uma limpeza.";
        pt["AdvInfo_fax-service_Cons"] = "Nenhuma relevante, a não ser que você envie fax pelo computador.";
        en["AdvInfo_fax-service_Pros"] = "Turns off an old fax service almost nobody uses. The gain is minimal; it's mostly cleanup.";
        en["AdvInfo_fax-service_Cons"] = "Nothing relevant, unless you send faxes from your computer.";

        pt["AdvInfo_windows-update-auto_Pros"] = "O Windows para de baixar e instalar atualizações sozinho, então não reinicia nem ocupa a internet no meio de uma partida.";
        pt["AdvInfo_windows-update-auto_Cons"] = "Correções de segurança param de chegar sozinhas; se você esquecer de atualizar manualmente, o PC fica exposto. Procure atualizações em Configurações de tempos em tempos.";
        en["AdvInfo_windows-update-auto_Pros"] = "Windows stops downloading and installing updates on its own, so it won't restart or eat your bandwidth mid-match.";
        en["AdvInfo_windows-update-auto_Cons"] = "Security fixes stop arriving automatically; if you forget to update manually, your PC stays exposed. Check for updates in Settings every so often.";

        pt["AdvInfo_windows-search_Pros"] = "Para a indexação de arquivos em segundo plano, que às vezes usa disco e CPU na hora errada. Ajuda mais em HD comum e PCs fracos.";
        pt["AdvInfo_windows-search_Cons"] = "A busca do menu Iniciar e do Explorador fica lenta ou deixa de achar arquivos, e a busca do Outlook e de outros apps que usam o índice pode parar de funcionar.";
        en["AdvInfo_windows-search_Pros"] = "Stops background file indexing, which sometimes uses disk and CPU at the wrong time. Helps most on hard drives and weaker PCs.";
        en["AdvInfo_windows-search_Cons"] = "Start menu and File Explorer search get slow or stop finding files, and search in Outlook and other apps that use the index may stop working.";

        pt["AdvInfo_sysmain_Pros"] = "Para o pré-carregamento de apps na memória, reduzindo a atividade de disco em segundo plano. Em SSDs o ganho costuma ser pequeno.";
        pt["AdvInfo_sysmain_Cons"] = "Apps que você usa com frequência podem abrir um pouco mais devagar, principalmente em HD comum.";
        en["AdvInfo_sysmain_Pros"] = "Stops apps from being preloaded into memory, reducing background disk activity. On SSDs the gain is usually small.";
        en["AdvInfo_sysmain_Cons"] = "Apps you use often may open a bit slower, especially on a hard drive.";

        pt["AdvInfo_print-spooler_Pros"] = "Desliga a fila de impressão, que fica ativa mesmo sem impressora e já teve falhas de segurança graves (PrintNightmare). O ganho de desempenho é quase nulo.";
        pt["AdvInfo_print-spooler_Cons"] = "Nada imprime enquanto estiver desativado, nem \"Imprimir em PDF\", e alguns programas podem dar erro ao abrir a janela de impressão.";
        en["AdvInfo_print-spooler_Pros"] = "Turns off the print queue, which runs even with no printer and has had serious security flaws (PrintNightmare). The performance gain is close to zero.";
        en["AdvInfo_print-spooler_Cons"] = "Nothing prints while it's off, not even \"Print to PDF\", and some programs may show errors when opening the print dialog.";

        pt["AdvInfo_remote-registry_Pros"] = "Garante que nenhum outro computador da rede consiga ler ou alterar o Registro deste PC. No Windows 10/11 ele já vem desligado, então em geral nada muda.";
        pt["AdvInfo_remote-registry_Cons"] = "Nenhuma em uso pessoal: o Registro do próprio PC funciona normalmente.";
        en["AdvInfo_remote-registry_Pros"] = "Makes sure no other computer on the network can read or change this PC's Registry. Windows 10/11 already ship with it off, so usually nothing changes.";
        en["AdvInfo_remote-registry_Cons"] = "None for personal use: this PC's own Registry works normally.";

        pt["AdvInfo_remote-access_Pros"] = "Fecha a porta para conexões de Área de Trabalho Remota vindas de fora, reduzindo os caminhos de ataque ao PC.";
        pt["AdvInfo_remote-access_Cons"] = "Você não consegue mais acessar este PC por Área de Trabalho Remota. Conectar-se a outros computadores, TeamViewer e AnyDesk continuam funcionando.";
        en["AdvInfo_remote-access_Pros"] = "Closes the door to incoming Remote Desktop connections, reducing the ways your PC can be attacked.";
        en["AdvInfo_remote-access_Cons"] = "You can no longer reach this PC through Remote Desktop. Connecting to other computers, TeamViewer and AnyDesk keep working.";

        pt["AdvInfo_retail-demo_Pros"] = "Desliga um serviço usado só em PCs de exposição em loja. É limpeza, sem ganho perceptível.";
        pt["AdvInfo_retail-demo_Cons"] = "Nenhuma num PC de uso pessoal.";
        en["AdvInfo_retail-demo_Pros"] = "Turns off a service used only on store display PCs. It's cleanup, with no noticeable gain.";
        en["AdvInfo_retail-demo_Cons"] = "None on a personal PC.";

        pt["AdvInfo_map-broker_Pros"] = "Para a atualização de mapas offline em segundo plano, que gasta disco e internet sem você perceber. O ganho é pequeno.";
        pt["AdvInfo_map-broker_Cons"] = "O app Mapas do Windows deixa de atualizar e usar mapas baixados para uso offline. Mapas no navegador não mudam.";
        en["AdvInfo_map-broker_Pros"] = "Stops offline maps from updating in the background, which uses disk and internet without you noticing. The gain is small.";
        en["AdvInfo_map-broker_Cons"] = "The Windows Maps app stops updating and using downloaded offline maps. Maps in your browser are not affected.";

        pt["AdvInfo_biometrics_Pros"] = "Desliga o serviço de biometria, que fica disponível mesmo em PCs sem leitor de digital ou câmera compatível. O ganho é pequeno.";
        pt["AdvInfo_biometrics_Cons"] = "Entrar com digital ou reconhecimento facial (Windows Hello) para de funcionar; você passa a usar senha ou PIN.";
        en["AdvInfo_biometrics_Pros"] = "Turns off the biometrics service, which stays available even on PCs with no fingerprint reader or compatible camera. The gain is small.";
        en["AdvInfo_biometrics_Cons"] = "Signing in with fingerprint or face recognition (Windows Hello) stops working; you go back to password or PIN.";

        pt["AdvInfo_delivery-optimization_Pros"] = "Seu PC para de enviar pedaços de atualizações para outros computadores, deixando o upload livre para jogos e chamadas.";
        pt["AdvInfo_delivery-optimization_Cons"] = "Se você tem vários PCs em casa, cada um baixa as atualizações inteiras da internet, em vez de compartilhar entre eles.";
        en["AdvInfo_delivery-optimization_Pros"] = "Your PC stops uploading pieces of updates to other computers, keeping your upload free for games and calls.";
        en["AdvInfo_delivery-optimization_Cons"] = "If you have several PCs at home, each one downloads full updates from the internet instead of sharing them.";

        // ---------- SISTEMA ----------

        pt["AdvInfo_hibernation_Pros"] = "Apaga o arquivo hiberfil.sys e libera alguns GB no disco do sistema, em geral uma boa parte do tamanho da sua RAM.";
        pt["AdvInfo_hibernation_Cons"] = "A hibernação e a Inicialização Rápida deixam de existir: ligar o PC desligado pode levar alguns segundos a mais, e no notebook não dá mais para hibernar.";
        en["AdvInfo_hibernation_Pros"] = "Deletes the hiberfil.sys file and frees a few GB on the system drive, usually a good chunk of your RAM size.";
        en["AdvInfo_hibernation_Cons"] = "Hibernate and Fast Startup go away: booting from shutdown may take a few seconds longer, and laptops can no longer hibernate.";

        // ---------- HARDWARE ----------

        pt["AdvInfo_hw-gpu-scheduling_Pros"] = "A placa de vídeo passa a organizar o próprio trabalho, o que pode reduzir um pouco a latência. Também é exigido pela geração de quadros do DLSS 3.";
        pt["AdvInfo_hw-gpu-scheduling_Cons"] = "Só vale depois de reiniciar. Em alguns jogos e drivers causa engasgos ou instabilidade em vez de ajudar; o resultado varia de PC para PC.";
        en["AdvInfo_hw-gpu-scheduling_Pros"] = "The graphics card manages its own work queue, which may cut latency a little. DLSS 3 Frame Generation also requires it.";
        en["AdvInfo_hw-gpu-scheduling_Cons"] = "Only takes effect after a restart. In some games and drivers it causes stutter or instability instead of helping; results vary from PC to PC.";

        pt["AdvInfo_core-parking_Pros"] = "Todos os núcleos da CPU ficam sempre acordados, evitando pequenos engasgos quando o jogo pede mais processamento de repente.";
        pt["AdvInfo_core-parking_Cons"] = "O PC gasta mais energia e esquenta um pouco mais parado; no notebook a bateria dura menos. Em Ryzen X3D com dois grupos de núcleos (ex.: 7950X3D) pode atrapalhar o driver que manda o jogo para os núcleos certos.";
        en["AdvInfo_core-parking_Pros"] = "All CPU cores stay awake, avoiding small stutters when a game suddenly asks for more processing.";
        en["AdvInfo_core-parking_Cons"] = "The PC uses more power and runs a bit warmer at idle; laptop battery lasts less. On dual-CCD Ryzen X3D chips (e.g. 7950X3D) it can interfere with the driver that sends games to the right cores.";

        pt["AdvInfo_power-throttling_Pros"] = "O Windows para de jogar programas para núcleos econômicos e frequências baixas. Ajuda principalmente em CPUs Intel com núcleos de desempenho e de eficiência.";
        pt["AdvInfo_power-throttling_Cons"] = "No notebook a bateria dura menos e a CPU esquenta mais, porque nada mais roda em modo econômico.";
        en["AdvInfo_power-throttling_Pros"] = "Windows stops pushing programs onto efficiency cores and low clock speeds. Helps mostly on Intel CPUs with performance and efficiency cores.";
        en["AdvInfo_power-throttling_Cons"] = "On laptops, battery lasts less and the CPU runs warmer, because nothing runs in power-saving mode anymore.";

        pt["AdvInfo_gpu-msi_Pros"] = "A placa de vídeo usa um jeito mais moderno de avisar a CPU, sem dividir a linha com outros dispositivos, o que pode reduzir engasgos esporádicos. O efeito varia de placa para placa.";
        pt["AdvInfo_gpu-msi_Cons"] = "Só vale depois de reiniciar. Se o driver não lidar bem com esse modo, pode haver tela preta ou travamentos ao iniciar o Windows, e aí é preciso reverter, às vezes pelo Modo de Segurança.";
        en["AdvInfo_gpu-msi_Pros"] = "The graphics card uses a more modern way to signal the CPU, without sharing a line with other devices, which may reduce random stutters. The effect varies from card to card.";
        en["AdvInfo_gpu-msi_Cons"] = "Only takes effect after a restart. If the driver doesn't handle this mode well, you may get a black screen or freezes at startup, and then you have to revert, sometimes from Safe Mode.";

        pt["AdvInfo_game-priority_Pros"] = "Jogos que usam o agendador multimídia do Windows recebem prioridade alta de CPU, GPU e disco, com valores do próprio perfil da Microsoft.";
        pt["AdvInfo_game-priority_Cons"] = "Nenhuma relevante. Só vale para jogos que se registram nesse agendador, então em muitos jogos não muda nada.";
        en["AdvInfo_game-priority_Pros"] = "Games that use the Windows multimedia scheduler get high CPU, GPU and disk priority, using values from Microsoft's own profile.";
        en["AdvInfo_game-priority_Cons"] = "Nothing relevant. It only applies to games that register with that scheduler, so in many games nothing changes.";

        pt["AdvInfo_mmcss-responsiveness_Pros"] = "Enquanto um jogo ou app de áudio e vídeo roda, sobra mais CPU para ele: a reserva para tarefas de fundo cai de 20% para 10%.";
        pt["AdvInfo_mmcss-responsiveness_Cons"] = "Tarefas em segundo plano, como downloads e gravação, podem ficar um pouco mais lentas sob carga pesada. Na prática, ganho e perda são pequenos.";
        en["AdvInfo_mmcss-responsiveness_Pros"] = "While a game or audio/video app runs, more CPU is left for it: the reserve for background tasks drops from 20% to 10%.";
        en["AdvInfo_mmcss-responsiveness_Cons"] = "Background tasks such as downloads and recording may get a bit slower under heavy load. In practice, both gain and loss are small.";

        pt["AdvInfo_foreground-priority_Pros"] = "A janela ativa, como o jogo, recebe a CPU com mais frequência do que os programas que rodam atrás dela.";
        pt["AdvInfo_foreground-priority_Cons"] = "Em muitos PCs o Windows já se comporta quase assim, então a diferença costuma ser pequena. Programas em segundo plano, como gravação ou stream, podem perder um pouco de desempenho.";
        en["AdvInfo_foreground-priority_Pros"] = "The active window, such as your game, gets the CPU more often than the programs running behind it.";
        en["AdvInfo_foreground-priority_Cons"] = "On many PCs Windows already behaves almost like this, so the difference is usually small. Background programs such as recording or streaming may lose a little performance.";

        pt["AdvInfo_ultimate-power_Pros"] = "A CPU deixa de reduzir a frequência entre picos de carga, eliminando pequenos atrasos do gerenciamento de energia.";
        pt["AdvInfo_ultimate-power_Cons"] = "O PC consome mais energia e esquenta mais mesmo parado; no notebook a bateria dura bem menos.";
        en["AdvInfo_ultimate-power_Pros"] = "The CPU stops lowering its clock between load spikes, removing small delays caused by power management.";
        en["AdvInfo_ultimate-power_Cons"] = "The PC uses more power and runs warmer even at idle; on laptops, battery life drops noticeably.";

        pt["AdvInfo_windowed-game-opt_Pros"] = "Jogos DirectX 10/11 em janela ou tela cheia sem bordas ganham menos latência e suporte a VRR (G-Sync/FreeSync) e Auto HDR.";
        pt["AdvInfo_windowed-game-opt_Cons"] = "Nenhuma relevante na maioria dos casos. Raramente, um jogo antigo ou uma sobreposição pode apresentar falha; nesse caso, reverta.";
        en["AdvInfo_windowed-game-opt_Pros"] = "DirectX 10/11 games in windowed or borderless fullscreen get lower latency plus VRR (G-Sync/FreeSync) and Auto HDR support.";
        en["AdvInfo_windowed-game-opt_Cons"] = "Nothing relevant in most cases. Rarely, an old game or an overlay may misbehave; if so, revert.";

        // ---------- JOGOS ----------

        pt["AdvInfo_game-dvr_Pros"] = "O Windows para de gravar a partida o tempo todo em segundo plano, poupando um pouco de CPU, GPU, disco e memória.";
        pt["AdvInfo_game-dvr_Cons"] = "Você perde o \"Gravar o que aconteceu\" e a gravação de jogos do próprio Windows. OBS, ShadowPlay e Radeon ReLive não são afetados.";
        en["AdvInfo_game-dvr_Pros"] = "Windows stops recording your match in the background all the time, saving a bit of CPU, GPU, disk and memory.";
        en["AdvInfo_game-dvr_Cons"] = "You lose \"Record what happened\" and Windows' own game recording. OBS, ShadowPlay and Radeon ReLive are not affected.";

        pt["AdvInfo_xbox-gamebar_Pros"] = "O botão Xbox do controle não abre mais a sobreposição por cima do jogo, e o app da Game Bar para de rodar em segundo plano.";
        pt["AdvInfo_xbox-gamebar_Cons"] = "Quem usa a Game Bar (widgets de desempenho, áudio, chat do Xbox) perde o atalho pelo controle, e ela deixa de ficar pronta em segundo plano.";
        en["AdvInfo_xbox-gamebar_Pros"] = "The controller's Xbox button no longer opens the overlay on top of your game, and the Game Bar app stops running in the background.";
        en["AdvInfo_xbox-gamebar_Cons"] = "If you use the Game Bar (performance, audio and Xbox chat widgets), you lose the controller shortcut and it's no longer ready in the background.";

        // ---------- ACELERAÇÃO DE HARDWARE EM APLICATIVOS ----------

        pt["AdvInfo_hwaccel-chrome_Pros"] = "O Chrome aberto ao lado do jogo deixa de usar a placa de vídeo, o que pode resolver tela piscando, vídeo travando ou falhas visuais causadas pelo driver.";
        pt["AdvInfo_hwaccel-chrome_Cons"] = "Vídeos e páginas pesadas passam a usar mais a CPU e podem ficar menos fluidos. O Chrome mostra \"gerenciado pela sua organização\" enquanto o ajuste estiver ativo.";
        en["AdvInfo_hwaccel-chrome_Pros"] = "Chrome open next to your game stops using the graphics card, which may fix flickering, stuttering video or visual glitches caused by the driver.";
        en["AdvInfo_hwaccel-chrome_Cons"] = "Videos and heavy pages use more CPU and may be less smooth. Chrome shows \"managed by your organization\" while this is on.";

        pt["AdvInfo_hwaccel-edge_Pros"] = "O Edge aberto ao lado do jogo deixa de usar a placa de vídeo, o que pode resolver tela piscando, vídeo travando ou falhas visuais causadas pelo driver.";
        pt["AdvInfo_hwaccel-edge_Cons"] = "Vídeos e páginas pesadas passam a usar mais a CPU e podem ficar menos fluidos. O Edge mostra \"gerenciado pela sua organização\" enquanto o ajuste estiver ativo.";
        en["AdvInfo_hwaccel-edge_Pros"] = "Edge open next to your game stops using the graphics card, which may fix flickering, stuttering video or visual glitches caused by the driver.";
        en["AdvInfo_hwaccel-edge_Cons"] = "Videos and heavy pages use more CPU and may be less smooth. Edge shows \"managed by your organization\" while this is on.";

        pt["AdvInfo_hwaccel-brave_Pros"] = "O Brave aberto ao lado do jogo deixa de usar a placa de vídeo, o que pode resolver tela piscando, vídeo travando ou falhas visuais causadas pelo driver.";
        pt["AdvInfo_hwaccel-brave_Cons"] = "Vídeos e páginas pesadas passam a usar mais a CPU e podem ficar menos fluidos. O Brave mostra \"gerenciado pela sua organização\" enquanto o ajuste estiver ativo.";
        en["AdvInfo_hwaccel-brave_Pros"] = "Brave open next to your game stops using the graphics card, which may fix flickering, stuttering video or visual glitches caused by the driver.";
        en["AdvInfo_hwaccel-brave_Cons"] = "Videos and heavy pages use more CPU and may be less smooth. Brave shows \"managed by your organization\" while this is on.";

        pt["AdvInfo_hwaccel-firefox_Pros"] = "O Firefox aberto ao lado do jogo deixa de usar a placa de vídeo, o que pode resolver tela piscando, vídeo travando ou falhas visuais causadas pelo driver.";
        pt["AdvInfo_hwaccel-firefox_Cons"] = "Vídeos e páginas pesadas passam a usar mais a CPU e podem ficar menos fluidos. O Firefox mostra que é \"gerenciado pela sua organização\" enquanto o ajuste estiver ativo.";
        en["AdvInfo_hwaccel-firefox_Pros"] = "Firefox open next to your game stops using the graphics card, which may fix flickering, stuttering video or visual glitches caused by the driver.";
        en["AdvInfo_hwaccel-firefox_Cons"] = "Videos and heavy pages use more CPU and may be less smooth. Firefox shows it is \"managed by your organization\" while this is on.";

        pt["AdvInfo_hwaccel-discord_Pros"] = "O Discord deixa de usar a placa de vídeo, o que pode resolver tela piscando, quedas de FPS ou falhas visuais no jogo enquanto ele está aberto.";
        pt["AdvInfo_hwaccel-discord_Cons"] = "Chamadas de vídeo, transmissões de tela e GIFs passam a usar mais a CPU e podem ficar menos fluidos. É preciso fechar e abrir o Discord para valer.";
        en["AdvInfo_hwaccel-discord_Pros"] = "Discord stops using the graphics card, which may fix flickering, FPS drops or visual glitches in your game while it's open.";
        en["AdvInfo_hwaccel-discord_Cons"] = "Video calls, screen shares and GIFs use more CPU and may be less smooth. You need to close and reopen Discord for it to apply.";

        pt["AdvInfo_hwaccel-spotify_Pros"] = "O Spotify deixa de usar a placa de vídeo, o que pode resolver falhas visuais no app ou ele disputando a GPU com o jogo. O ganho costuma ser pequeno.";
        pt["AdvInfo_hwaccel-spotify_Cons"] = "Animações e a rolagem do app podem ficar um pouco menos suaves. É preciso reabrir o Spotify para valer.";
        en["AdvInfo_hwaccel-spotify_Pros"] = "Spotify stops using the graphics card, which may fix visual glitches in the app or it competing with your game for the GPU. The gain is usually small.";
        en["AdvInfo_hwaccel-spotify_Cons"] = "Animations and scrolling in the app may be slightly less smooth. You need to reopen Spotify for it to apply.";
    }
}
