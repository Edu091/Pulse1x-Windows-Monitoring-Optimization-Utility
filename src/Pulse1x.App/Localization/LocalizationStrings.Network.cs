namespace Pulse1x.App.Localization;

/// <summary>Textos novos da categoria Latência: central de Wi-Fi, testes avançados (bufferbloat,
/// DNS, MTU), ajustes do driver da placa e o diagnóstico roteador × internet.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddNetworkPortuguese()
    {
        var t = Pt;

        // ---- Painel em tempo real ----
        t["Lat_PingInternet"] = "Ping (internet)";
        t["Lat_PingInternetTip"] = "Tempo de ida e volta até 1.1.1.1 (Cloudflare): é o que o jogo sente.";
        t["Lat_PingRouter"] = "Ping (roteador)";
        t["Lat_PingRouterTip"] = "Tempo até o seu roteador. Se este estiver alto, o problema está no Wi-Fi ou na rede de casa, não no provedor.";
        t["Lat_BandChannel"] = "Banda · canal";
        t["Lat_Adapter"] = "Adaptador";
        t["Lat_WifiLocationBlocked"] = "O Windows está escondendo os dados do Wi-Fi (sinal, canal e redes vizinhas) porque a localização está desligada para apps. Ative \"Localização\" e \"Permitir que os apps da área de trabalho acessem sua localização\".";
        t["Lat_OpenLocation"] = "Abrir configurações";

        // ---- DNS ----
        t["Lat_DnsAutomatic"] = "Automático (provedor)";
        t["Lat_DoneDnsSet"] = "DNS alterado para {0} ({1}) e cache limpo.";

        // ---- Central de Wi-Fi ----
        t["Lat_WifiTitle"] = "Central de Wi-Fi";
        t["Lat_WifiSubtitle"] = "Modo partida sem picos de ping, varredura de redes vizinhas com o melhor canal e ajustes do driver do adaptador sem fio.";
        t["Lat_WifiGameMode"] = "Modo partida (sem buscas em segundo plano)";
        t["Lat_WifiGameModeDesc"] = "O Windows procura outras redes a cada minuto, e cada busca tira o Wi-Fi do ar por alguns milissegundos: são os picos de ping periódicos. Ligado, a conexão atual continua, mas o PC não troca nem reconecta de rede sozinho. Desliga ao fechar o Pulse1x.";
        t["Lat_DoneWifiGameOn"] = "Modo partida ligado: buscas em segundo plano pausadas.";
        t["Lat_DoneWifiGameOff"] = "Modo partida desligado: o Windows voltou a gerenciar o Wi-Fi normalmente.";
        t["Lat_WifiScanTitle"] = "Redes vizinhas e canais";
        t["Lat_WifiScanDesc"] = "Faz uma varredura nova, mostra quem disputa o mesmo canal que você e qual canal está mais livre para configurar no roteador.";
        t["Lat_WifiScan"] = "Escanear redes";
        t["Lat_WifiScanning"] = "Procurando redes (leva alguns segundos)…";
        t["Lat_WifiScanSummary"] = "{0} pontos de acesso encontrados em {1} canais.";
        t["Lat_WifiNotConnected"] = "Conecte-se a uma rede Wi-Fi para ver a análise do seu canal.";
        t["Lat_WifiChannelNow"] = "Você está no canal {0} ({1}) com {2} rede(s) vizinha(s) interferindo.";
        t["Lat_WifiChangeChannel"] = "Canal congestionado: no painel do roteador, troque para o canal {0}, o mais livre agora.";
        t["Lat_WifiChannelOk"] = "Seu canal está tranquilo. Não precisa trocar.";
        t["Lat_WifiUtilization"] = "O roteador informa {0}% de ocupação do canal e {1} dispositivo(s) conectado(s).";
        t["Lat_WifiHas5Ghz"] = "Seu roteador também transmite em 5 GHz com bom sinal aqui. Conecte-se nela para ter menos interferência e ping mais estável.";
        t["Lat_WifiHidden"] = "(rede oculta)";
        t["Lat_WifiChannelShort"] = "Canal {0}";
        t["Lat_WifiStations"] = "{0} dispositivos";
        t["Lat_WifiYou"] = "VOCÊ";
        t["Lat_WifiInterferes"] = "INTERFERE";
        t["Lat_WifiDriverGroup"] = "Ajustes do driver Wi-Fi";
        t["Lat_AdapterRestartNote"] = "Os ajustes de driver usam só valores que o próprio driver oferece e reiniciam a placa uma vez (2 a 5 segundos sem conexão). Todos são desfeitos por \"Desfazer Todas as Alterações\".";
        t["Lat_ToolWifiRoaming"] = "Roaming no mínimo";
        t["Lat_ToolWifiRoamingDesc"] = "Impede o adaptador de ficar procurando um ponto de acesso \"melhor\" enquanto você joga, o que causa travadas. Em casas com repetidor/mesh, deixe como está se você anda pela casa. Reversível.";
        t["Lat_ToolWifiPrefer5"] = "Preferir 5 GHz";
        t["Lat_ToolWifiPrefer5Desc"] = "Quando o roteador tem 2,4 e 5 GHz com o mesmo nome, o adaptador escolhe 5 GHz: menos interferência e menos latência. Reversível.";
        t["Lat_ToolWlanReport"] = "Relatório de quedas do Wi-Fi";
        t["Lat_ToolWlanReportDesc"] = "Gera o relatório do Windows com cada desconexão e erro do Wi-Fi nos últimos 3 dias e abre no navegador.";
        t["Lat_Open"] = "Abrir";
        t["Lat_DoneWlanReport"] = "Relatório aberto no navegador.";
        t["Lat_AdvNoWifi"] = "Nenhum adaptador Wi-Fi encontrado.";
        t["Lat_AdvNotSupported"] = "O driver de \"{0}\" não oferece esse ajuste.";
        t["Lat_AdvAlready"] = "Já estava ajustado. Nada mudou.";
        t["Lat_AdvDone"] = "Ajustado em {1}: {0}.";

        // ---- Testes avançados ----
        t["Lat_AdvTitle"] = "Testes avançados de latência";
        t["Lat_AdvSubtitle"] = "Bufferbloat (lag quando a rede está cheia), o DNS mais rápido para você e o tamanho ideal de pacote (MTU).";
        t["Lat_BbTitle"] = "Bufferbloat: ping com a rede cheia";
        t["Lat_BbDesc"] = "Mede quanto o ping sobe quando alguém baixa algo ou assiste vídeo. Leva uns 20 segundos e usa a internet de propósito.";
        t["Lat_BbRun"] = "Testar";
        t["Lat_BbPhaseIdle"] = "Medindo o ping com a rede parada…";
        t["Lat_BbPhaseDownload"] = "Medindo o ping durante um download…";
        t["Lat_BbPhaseUpload"] = "Medindo o ping durante um upload…";
        t["Lat_BbIdle"] = "Rede parada";
        t["Lat_BbDownload"] = "Durante download";
        t["Lat_BbUpload"] = "Durante upload";
        t["Lat_BbAdviceGood"] = "Ótimo: o ping quase não muda com a rede cheia. Downloads de outras pessoas não vão atrapalhar suas partidas.";
        t["Lat_BbAdviceOk"] = "Aceitável: o ping sobe um pouco com a rede cheia. Evite downloads grandes durante partidas competitivas.";
        t["Lat_BbAdviceBad"] = "O ping dispara quando a rede enche (bufferbloat). Correção: ative SQM, QoS ou \"Smart Queue\" no roteador e limite a velocidade a uns 90% do plano. Enquanto isso, pause downloads, atualizações e backups na nuvem durante as partidas.";
        t["Lat_BbNoInternet"] = "Sem resposta da internet. Verifique a conexão e tente de novo.";
        t["Lat_DnsBenchTitle"] = "DNS mais rápido";
        t["Lat_DnsBenchDesc"] = "Compara o seu DNS atual com Cloudflare, Google, Quad9, OpenDNS e AdGuard, consultando cada um direto, sem o cache do Windows.";
        t["Lat_DnsBenchRun"] = "Comparar";
        t["Lat_DnsBenchRunning"] = "Consultando os servidores…";
        t["Lat_DnsCurrent"] = "Seu DNS atual";
        t["Lat_DnsCurrentShort"] = "atual";
        t["Lat_DnsBenchFailed"] = "Nenhum servidor DNS respondeu. A porta 53 pode estar bloqueada nesta rede.";
        t["Lat_DnsBenchCurrentBest"] = "Seu DNS atual já está entre os mais rápidos. Não precisa trocar.";
        t["Lat_DnsBenchFastest"] = "{0} é o mais rápido para você ({1} ms).";
        t["Lat_DnsUseFastest"] = "Usar o mais rápido";
        t["Lat_DnsFastestBadge"] = "⚡ mais rápido";
        t["Lat_MtuTitle"] = "MTU ideal";
        t["Lat_MtuDesc"] = "Descobre o maior pacote que chega à internet sem ser fragmentado. MTU alto demais causa retransmissões e engasgos, comum em conexões PPPoE e VPN.";
        t["Lat_MtuRun"] = "Descobrir";
        t["Lat_MtuRunning"] = "Testando tamanhos de pacote…";
        t["Lat_MtuFailed"] = "Não foi possível testar: a rede não responde a ping com \"não fragmentar\".";
        t["Lat_MtuLower"] = "MTU ideal: {0}. O seu está em {1}, então pacotes grandes são fragmentados. Aplicar corrige isso.";
        t["Lat_MtuHigher"] = "MTU ideal: {0}. O seu está em {1}, abaixo do que a rede aguenta.";
        t["Lat_MtuOk"] = "Seu MTU já está no ideal ({0}).";
        t["Lat_MtuApply"] = "Aplicar MTU";
        t["Lat_DoneMtu"] = "MTU {0} aplicado em {1}.";

        // ---- Diagnóstico ----
        t["Lat_DiagSubtitle"] = "Mede roteador, internet, velocidade, Wi-Fi e configurações, e explica em linguagem simples onde está o problema.";
        t["Lat_DiagHeadlineNoInternet"] = "Sem resposta da internet.";
        t["Lat_DiagRouter"] = "Roteador: {0} ms • jitter {1} ms • perda {2}%.";
        t["Lat_DiagPingFailed"] = "Internet: sem resposta.";
        t["Lat_FindLocationOff"] = "A localização do Windows está desligada, então os dados do Wi-Fi (sinal, canal, redes vizinhas) não puderam ser lidos. Ative-a para um diagnóstico completo.";
        t["Lat_FindLocalWifi"] = "O problema está entre o PC e o roteador: o próprio Wi-Fi tem latência, oscilação ou perda. Aproxime-se do roteador, use 5 GHz, troque o canal (Central de Wi-Fi) ou ligue o Modo partida. Cabo resolve de vez.";
        t["Lat_FindLocalLan"] = "O problema está na rede de casa (PC até o roteador). Teste outro cabo ou outra porta do roteador e reinicie o roteador.";
        t["Lat_FindIsp"] = "Sua rede de casa está boa; a instabilidade começa depois do roteador, no provedor. Se continuar, reinicie o modem e, persistindo, abra um chamado com a operadora.";
        t["Lat_FindNoInternet"] = "O roteador responde, mas a internet não. Reinicie o modem/roteador ou verifique com a operadora.";

        // ---- Seções ----
        t["Lat_TestServersSubtitle"] = "Latência até servidores de jogos e nuvem conhecidos, todos ao mesmo tempo.";
        t["Lat_ToolsSubtitle"] = "Ações rápidas, ajustes do Windows e do driver da placa e perfis prontos. Tudo reversível.";
        t["Lat_GroupAdapter"] = "Driver da placa de rede ativa";

        // ---- Reverter ----
        t["Lat_Revert"] = "Reverter";
        t["Lat_Reverted"] = "Revertido: {0} configuração(ões) voltaram ao valor de antes.";
        t["Lat_NothingToRevert"] = "Nada a reverter: este ajuste não tem alterações ativas.";
        t["Lat_RestoreAllTip"] = "Desfaz tudo o que o Pulse1x mudou na rede, de uma vez. Fica desabilitado quando não há nada aplicado.";
        t["Lat_RevertDnsTip"] = "Volta ao DNS que estava configurado antes da primeira troca feita pelo Pulse1x.";
        t["Lat_MtuRevert"] = "Reverter MTU";

        // ---- Ajustes novos ----
        t["Lat_ToolAdapterPower"] = "Impedir o Windows de desligar a placa";
        t["Lat_ToolAdapterPowerDesc"] = "Desmarca \"O computador pode desligar este dispositivo para economizar energia\", motivo clássico de o Wi-Fi sumir depois de um tempo parado ou ao voltar da suspensão. Vale após reiniciar a placa ou o PC. Reversível.";
        t["Lat_DoneAdapterPower"] = "Pronto. Vale após reiniciar a placa de rede ou o PC.";
        t["Lat_ToolDeliveryOpt"] = "Desligar upload P2P do Windows Update";
        t["Lat_ToolDeliveryOptDesc"] = "O Windows envia atualizações para outros PCs em segundo plano, o que enche o upload e sobe o ping \"do nada\". As atualizações continuam chegando normalmente. Reversível.";
        t["Lat_DoneDeliveryOpt"] = "Upload P2P do Windows Update desligado.";
        t["Lat_ToolDriverPower"] = "Desligar economia de energia do driver";
        t["Lat_ToolDriverPowerDesc"] = "Desativa no driver da placa ativa o que faz ela \"cochilar\" entre pacotes: Power Saving, Energy Efficient Ethernet, Green Ethernet, MIMO power save, U-APSD e Packet Coalescing (o que existir). Reinicia a placa. Reversível.";
        t["Lat_ToolInterruptMod"] = "Desligar moderação de interrupção";
        t["Lat_ToolInterruptModDesc"] = "A placa avisa o processador de cada pacote na hora, em vez de juntar vários. Menos latência em troca de um pouco mais de CPU. Mais comum em placas de cabo. Reinicia a placa. Reversível.";

        // ---- Reverter em tudo + ajustes extras ----
        t["Lat_ActiveBadge"] = "Ativo";
        t["Lat_RevertedReboot"] = "Revertido: {0} configuração(ões) restauradas. Reinicie o computador para concluir.";
        t["Lat_BackupFailed"] = "Não foi possível salvar uma cópia da configuração atual, então nada foi alterado.";
        t["Lat_ReversibleNote"] = "Todo ajuste aplicado mostra \"Ativo\" e um botão \"Reverter\", que volta só aquele ajuste ao valor de antes. \"Desfazer Todas as Alterações\" volta tudo de uma vez. Limpar cache DNS, renovar IP e reiniciar placa/serviços não mudam nenhuma configuração, por isso não têm o que reverter.";
        t["Lat_ToolResetWinsockDesc"] = "Restaura o catálogo Winsock ao padrão. Antes, salva uma cópia do atual para o \"Reverter\". Requer reiniciar o computador.";
        t["Lat_ToolResetTcpDesc"] = "Reseta a pilha TCP/IP ao padrão do Windows (apaga IP fixo e DNS manual). Antes, salva uma cópia para o \"Reverter\". Requer reiniciar.";
        t["Lat_ToolThrottling"] = "Remover limite de rede do Windows";
        t["Lat_ToolThrottlingDesc"] = "Enquanto toca áudio ou vídeo, o Windows limita o processamento de pacotes de rede (Network Throttling). Desligar evita picos de ping com música ou Discord abertos. Reversível.";
        t["Lat_DoneThrottling"] = "Limite de rede do Windows removido.";
        t["Lat_ToolNagle"] = "Desativar algoritmo de Nagle";
        t["Lat_ToolNagleDesc"] = "Pacotes pequenos saem na hora em vez de esperar para ir juntos, e cada um é confirmado sem atraso. Ajuda jogos que usam TCP (MMOs, MOBAs, Minecraft). Vale para a placa ativa. Reversível.";
        t["Lat_DoneNagle"] = "Nagle desativado em {0}. Vale para novas conexões (reabra o jogo).";
        t["Lat_ToolRsc"] = "Desativar Receive Segment Coalescing (RSC)";
        t["Lat_ToolRscDesc"] = "O Windows segura pacotes recebidos para juntá-los em um só, o que economiza CPU em downloads mas atrasa cada pacote. Desligar dá menos latência, com um pouco mais de CPU em downloads grandes. Reversível.";
        t["Lat_DoneRsc"] = "Receive Segment Coalescing desativado.";
        t["Lat_ToolLso"] = "Desligar Large Send Offload (LSO)";
        t["Lat_ToolLsoDesc"] = "A placa deixa de acumular dados de envio para cortá-los em pacotes por conta própria, o que pode atrasar o upload e causar picos de ping em alguns drivers. Reinicia a placa. Reversível.";
        t["Lat_ToolFlowControl"] = "Desligar controle de fluxo";
        t["Lat_ToolFlowControlDesc"] = "Impede que o roteador ou switch mande a placa \"pausar\" o envio quando está cheio, o que congela pacotes por alguns milissegundos. Mais comum em placas de cabo. Reinicia a placa. Reversível.";
    }

    internal static void AddNetworkEnglish()
    {
        var t = En;

        // ---- Live panel ----
        t["Lat_PingInternet"] = "Ping (internet)";
        t["Lat_PingInternetTip"] = "Round trip to 1.1.1.1 (Cloudflare): what your game actually feels.";
        t["Lat_PingRouter"] = "Ping (router)";
        t["Lat_PingRouterTip"] = "Time to your router. If this one is high, the problem is your Wi-Fi or home network, not your ISP.";
        t["Lat_BandChannel"] = "Band · channel";
        t["Lat_Adapter"] = "Adapter";
        t["Lat_WifiLocationBlocked"] = "Windows is hiding Wi-Fi data (signal, channel and nearby networks) because location is off for apps. Turn on \"Location\" and \"Let desktop apps access your location\".";
        t["Lat_OpenLocation"] = "Open settings";

        // ---- DNS ----
        t["Lat_DnsAutomatic"] = "Automatic (ISP)";
        t["Lat_DoneDnsSet"] = "DNS changed to {0} ({1}) and cache cleared.";

        // ---- Wi-Fi hub ----
        t["Lat_WifiTitle"] = "Wi-Fi hub";
        t["Lat_WifiSubtitle"] = "Match mode without ping spikes, a scan of nearby networks with the best channel, and wireless adapter driver tweaks.";
        t["Lat_WifiGameMode"] = "Match mode (no background scans)";
        t["Lat_WifiGameModeDesc"] = "Windows looks for other networks every minute, and each scan takes the Wi-Fi off the air for a few milliseconds: those are the periodic ping spikes. When on, the current connection stays, but the PC won't switch or reconnect networks on its own. Turns off when Pulse1x closes.";
        t["Lat_DoneWifiGameOn"] = "Match mode on: background scans paused.";
        t["Lat_DoneWifiGameOff"] = "Match mode off: Windows manages the Wi-Fi normally again.";
        t["Lat_WifiScanTitle"] = "Nearby networks and channels";
        t["Lat_WifiScanDesc"] = "Runs a fresh scan, shows who shares your channel and which channel is the least crowded to set on your router.";
        t["Lat_WifiScan"] = "Scan networks";
        t["Lat_WifiScanning"] = "Looking for networks (takes a few seconds)…";
        t["Lat_WifiScanSummary"] = "{0} access points found on {1} channels.";
        t["Lat_WifiNotConnected"] = "Connect to a Wi-Fi network to see your channel analysis.";
        t["Lat_WifiChannelNow"] = "You are on channel {0} ({1}) with {2} nearby network(s) interfering.";
        t["Lat_WifiChangeChannel"] = "Crowded channel: in your router settings, switch to channel {0}, the least used right now.";
        t["Lat_WifiChannelOk"] = "Your channel is quiet. No need to change it.";
        t["Lat_WifiUtilization"] = "The router reports {0}% channel usage and {1} connected device(s).";
        t["Lat_WifiHas5Ghz"] = "Your router also broadcasts 5 GHz with good signal here. Connect to it for less interference and steadier ping.";
        t["Lat_WifiHidden"] = "(hidden network)";
        t["Lat_WifiChannelShort"] = "Channel {0}";
        t["Lat_WifiStations"] = "{0} devices";
        t["Lat_WifiYou"] = "YOU";
        t["Lat_WifiInterferes"] = "INTERFERES";
        t["Lat_WifiDriverGroup"] = "Wi-Fi driver tweaks";
        t["Lat_AdapterRestartNote"] = "Driver tweaks only use values the driver itself offers and restart the adapter once (2 to 5 seconds offline). All of them are undone by \"Undo All Changes\".";
        t["Lat_ToolWifiRoaming"] = "Minimum roaming";
        t["Lat_ToolWifiRoamingDesc"] = "Stops the adapter from hunting for a \"better\" access point while you play, which causes stutters. With repeaters/mesh, leave it alone if you move around the house. Reversible.";
        t["Lat_ToolWifiPrefer5"] = "Prefer 5 GHz";
        t["Lat_ToolWifiPrefer5Desc"] = "When the router has 2.4 and 5 GHz under the same name, the adapter picks 5 GHz: less interference and lower latency. Reversible.";
        t["Lat_ToolWlanReport"] = "Wi-Fi drop report";
        t["Lat_ToolWlanReportDesc"] = "Generates the Windows report with every Wi-Fi disconnect and error from the last 3 days and opens it in your browser.";
        t["Lat_Open"] = "Open";
        t["Lat_DoneWlanReport"] = "Report opened in your browser.";
        t["Lat_AdvNoWifi"] = "No Wi-Fi adapter found.";
        t["Lat_AdvNotSupported"] = "The \"{0}\" driver doesn't offer this setting.";
        t["Lat_AdvAlready"] = "Already set. Nothing changed.";
        t["Lat_AdvDone"] = "Set on {1}: {0}.";

        // ---- Advanced tests ----
        t["Lat_AdvTitle"] = "Advanced latency tests";
        t["Lat_AdvSubtitle"] = "Bufferbloat (lag when the network is busy), the fastest DNS for you and the ideal packet size (MTU).";
        t["Lat_BbTitle"] = "Bufferbloat: ping under load";
        t["Lat_BbDesc"] = "Measures how much your ping rises when someone downloads or streams. Takes about 20 seconds and uses your internet on purpose.";
        t["Lat_BbRun"] = "Test";
        t["Lat_BbPhaseIdle"] = "Measuring ping with the network idle…";
        t["Lat_BbPhaseDownload"] = "Measuring ping during a download…";
        t["Lat_BbPhaseUpload"] = "Measuring ping during an upload…";
        t["Lat_BbIdle"] = "Idle";
        t["Lat_BbDownload"] = "During download";
        t["Lat_BbUpload"] = "During upload";
        t["Lat_BbAdviceGood"] = "Great: your ping barely moves when the network is busy. Other people's downloads won't hurt your matches.";
        t["Lat_BbAdviceOk"] = "Acceptable: ping rises a little under load. Avoid big downloads during competitive matches.";
        t["Lat_BbAdviceBad"] = "Your ping spikes when the network fills up (bufferbloat). Fix: enable SQM, QoS or \"Smart Queue\" on your router and cap the speed at about 90% of your plan. Meanwhile, pause downloads, updates and cloud backups while you play.";
        t["Lat_BbNoInternet"] = "No response from the internet. Check your connection and try again.";
        t["Lat_DnsBenchTitle"] = "Fastest DNS";
        t["Lat_DnsBenchDesc"] = "Compares your current DNS with Cloudflare, Google, Quad9, OpenDNS and AdGuard, querying each one directly, bypassing the Windows cache.";
        t["Lat_DnsBenchRun"] = "Compare";
        t["Lat_DnsBenchRunning"] = "Querying servers…";
        t["Lat_DnsCurrent"] = "Your current DNS";
        t["Lat_DnsCurrentShort"] = "current";
        t["Lat_DnsBenchFailed"] = "No DNS server answered. Port 53 may be blocked on this network.";
        t["Lat_DnsBenchCurrentBest"] = "Your current DNS is already among the fastest. No need to change it.";
        t["Lat_DnsBenchFastest"] = "{0} is the fastest for you ({1} ms).";
        t["Lat_DnsUseFastest"] = "Use the fastest";
        t["Lat_DnsFastestBadge"] = "⚡ fastest";
        t["Lat_MtuTitle"] = "Ideal MTU";
        t["Lat_MtuDesc"] = "Finds the largest packet that reaches the internet without being fragmented. An MTU that is too high causes retransmissions and hiccups, common on PPPoE and VPN connections.";
        t["Lat_MtuRun"] = "Find";
        t["Lat_MtuRunning"] = "Testing packet sizes…";
        t["Lat_MtuFailed"] = "Couldn't test: the network doesn't answer \"don't fragment\" pings.";
        t["Lat_MtuLower"] = "Ideal MTU: {0}. Yours is {1}, so large packets get fragmented. Applying fixes it.";
        t["Lat_MtuHigher"] = "Ideal MTU: {0}. Yours is {1}, below what the network handles.";
        t["Lat_MtuOk"] = "Your MTU is already ideal ({0}).";
        t["Lat_MtuApply"] = "Apply MTU";
        t["Lat_DoneMtu"] = "MTU {0} applied to {1}.";

        // ---- Diagnostics ----
        t["Lat_DiagSubtitle"] = "Measures router, internet, speed, Wi-Fi and settings, and explains in plain words where the problem is.";
        t["Lat_DiagHeadlineNoInternet"] = "No response from the internet.";
        t["Lat_DiagRouter"] = "Router: {0} ms • jitter {1} ms • loss {2}%.";
        t["Lat_DiagPingFailed"] = "Internet: no response.";
        t["Lat_FindLocationOff"] = "Windows location is off, so Wi-Fi data (signal, channel, nearby networks) couldn't be read. Turn it on for a complete diagnosis.";
        t["Lat_FindLocalWifi"] = "The problem is between the PC and the router: the Wi-Fi itself has latency, jitter or loss. Move closer to the router, use 5 GHz, change the channel (Wi-Fi hub) or turn on Match mode. A cable fixes it for good.";
        t["Lat_FindLocalLan"] = "The problem is in your home network (PC to router). Try another cable or router port and restart the router.";
        t["Lat_FindIsp"] = "Your home network is fine; the instability starts after the router, at your ISP. If it continues, restart the modem and, if it persists, contact your provider.";
        t["Lat_FindNoInternet"] = "The router answers, but the internet doesn't. Restart the modem/router or check with your provider.";

        // ---- Sections ----
        t["Lat_TestServersSubtitle"] = "Latency to well-known game and cloud servers, all at once.";
        t["Lat_ToolsSubtitle"] = "Quick actions, Windows and adapter driver tweaks, and ready-made profiles. All reversible.";
        t["Lat_GroupAdapter"] = "Active network adapter driver";

        // ---- Revert ----
        t["Lat_Revert"] = "Revert";
        t["Lat_Reverted"] = "Reverted: {0} setting(s) back to their previous value.";
        t["Lat_NothingToRevert"] = "Nothing to revert: this setting has no active changes.";
        t["Lat_RestoreAllTip"] = "Undoes everything Pulse1x changed on the network at once. Disabled when nothing is applied.";
        t["Lat_RevertDnsTip"] = "Goes back to the DNS you had before Pulse1x first changed it.";
        t["Lat_MtuRevert"] = "Revert MTU";

        // ---- New tweaks ----
        t["Lat_ToolAdapterPower"] = "Stop Windows from powering down the adapter";
        t["Lat_ToolAdapterPowerDesc"] = "Unchecks \"Allow the computer to turn off this device to save power\", the classic reason Wi-Fi disappears after sitting idle or after sleep. Takes effect after restarting the adapter or the PC. Reversible.";
        t["Lat_DoneAdapterPower"] = "Done. Takes effect after restarting the network adapter or the PC.";
        t["Lat_ToolDeliveryOpt"] = "Turn off Windows Update P2P upload";
        t["Lat_ToolDeliveryOptDesc"] = "Windows uploads updates to other PCs in the background, which fills your upload and raises ping \"out of nowhere\". Updates keep arriving normally. Reversible.";
        t["Lat_DoneDeliveryOpt"] = "Windows Update P2P upload turned off.";
        t["Lat_ToolDriverPower"] = "Turn off driver power saving";
        t["Lat_ToolDriverPowerDesc"] = "Disables, in the active adapter's driver, what makes it doze between packets: Power Saving, Energy Efficient Ethernet, Green Ethernet, MIMO power save, U-APSD and Packet Coalescing (whatever exists). Restarts the adapter. Reversible.";
        t["Lat_ToolInterruptMod"] = "Turn off interrupt moderation";
        t["Lat_ToolInterruptModDesc"] = "The adapter notifies the CPU of each packet right away instead of batching them. Lower latency for a bit more CPU. Most common on wired adapters. Restarts the adapter. Reversible.";

        // ---- Revert everywhere + extra tweaks ----
        t["Lat_ActiveBadge"] = "Active";
        t["Lat_RevertedReboot"] = "Reverted: {0} setting(s) restored. Restart your computer to finish.";
        t["Lat_BackupFailed"] = "Couldn't save a copy of the current settings, so nothing was changed.";
        t["Lat_ReversibleNote"] = "Every applied tweak shows \"Active\" and a \"Revert\" button that returns just that tweak to its previous value. \"Undo All Changes\" reverts everything at once. Flushing DNS, renewing the IP and restarting the adapter/services don't change any setting, so there's nothing to revert.";
        t["Lat_ToolResetWinsockDesc"] = "Restores the Winsock catalog to default. Saves a copy of the current one first so you can Revert. Requires a restart.";
        t["Lat_ToolResetTcpDesc"] = "Resets the TCP/IP stack to Windows defaults (clears static IP and manual DNS). Saves a copy first so you can Revert. Requires a restart.";
        t["Lat_ToolThrottling"] = "Remove Windows network throttling";
        t["Lat_ToolThrottlingDesc"] = "While audio or video plays, Windows limits network packet processing (Network Throttling). Turning it off avoids ping spikes with music or Discord open. Reversible.";
        t["Lat_DoneThrottling"] = "Windows network throttling removed.";
        t["Lat_ToolNagle"] = "Disable Nagle's algorithm";
        t["Lat_ToolNagleDesc"] = "Small packets go out right away instead of waiting to be bundled, and each one is acknowledged without delay. Helps games that use TCP (MMOs, MOBAs, Minecraft). Applies to the active adapter. Reversible.";
        t["Lat_DoneNagle"] = "Nagle disabled on {0}. Applies to new connections (reopen the game).";
        t["Lat_ToolRsc"] = "Disable Receive Segment Coalescing (RSC)";
        t["Lat_ToolRscDesc"] = "Windows holds incoming packets to merge them into one, which saves CPU on downloads but delays each packet. Turning it off lowers latency for a bit more CPU on big downloads. Reversible.";
        t["Lat_DoneRsc"] = "Receive Segment Coalescing disabled.";
        t["Lat_ToolLso"] = "Turn off Large Send Offload (LSO)";
        t["Lat_ToolLsoDesc"] = "The adapter stops buffering outgoing data to split it into packets itself, which can delay uploads and cause ping spikes on some drivers. Restarts the adapter. Reversible.";
        t["Lat_ToolFlowControl"] = "Turn off flow control";
        t["Lat_ToolFlowControlDesc"] = "Stops the router or switch from telling the adapter to \"pause\" sending when it's full, which freezes packets for a few milliseconds. Most common on wired adapters. Restarts the adapter. Reversible.";
    }
}
