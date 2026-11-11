namespace Pulse1x.App.Localization;

/// <summary>Vantagens e desvantagens ("&lt;chave&gt;_Pros" / "&lt;chave&gt;_Cons") das funções desta área.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddInfoSpecial()
    {
        var pt = Pt;
        var en = En;

        // ---- Reparo do Sistema ----
        pt["Special_SfcTitle_Pros"] = "Encontra e conserta arquivos do Windows corrompidos, que causam travamentos, telas azuis e erros estranhos.";
        pt["Special_SfcTitle_Cons"] = "Nenhuma de verdade: só demora de 5 a 15 minutos e usa um pouco do processador enquanto roda.";
        en["Special_SfcTitle_Pros"] = "Finds and repairs corrupted Windows files that cause freezes, blue screens and odd errors.";
        en["Special_SfcTitle_Cons"] = "None really: it just takes 5 to 15 minutes and uses some CPU while it runs.";

        pt["Special_DismRestoreTitle_Pros"] = "Conserta a \"cópia original\" do Windows que o SFC usa para reparar arquivos; resolve casos em que o SFC sozinho não dá conta.";
        pt["Special_DismRestoreTitle_Cons"] = "Pode levar de 10 a 30 minutos e baixar arquivos do Windows Update, gastando internet.";
        en["Special_DismRestoreTitle_Pros"] = "Repairs the Windows \"master copy\" that SFC uses to fix files; solves cases SFC alone can't handle.";
        en["Special_DismRestoreTitle_Cons"] = "May take 10 to 30 minutes and download files from Windows Update, using bandwidth.";

        pt["Special_ChkdskTitle_Pros"] = "Mostra se o disco C: tem erros no sistema de arquivos, que podem causar lentidão ou arquivos corrompidos.";
        pt["Special_ChkdskTitle_Cons"] = "Quase nenhuma: roda só em modo leitura, então aponta os erros mas não os corrige. Pode levar alguns minutos.";
        en["Special_ChkdskTitle_Pros"] = "Shows whether drive C: has file system errors, which can cause slowdowns or corrupted files.";
        en["Special_ChkdskTitle_Cons"] = "Almost none: it runs read-only, so it reports errors but doesn't fix them. It may take a few minutes.";

        pt["Special_DismCleanupTitle_Pros"] = "Apaga versões antigas de componentes deixadas pelas atualizações e libera espaço no disco (às vezes vários GB).";
        pt["Special_DismCleanupTitle_Cons"] = "Depois da limpeza, algumas atualizações já instaladas podem não poder mais ser desinstaladas. Pode demorar bastante.";
        en["Special_DismCleanupTitle_Pros"] = "Deletes old component versions left behind by updates and frees disk space (sometimes several GB).";
        en["Special_DismCleanupTitle_Cons"] = "After the cleanup, some installed updates may no longer be uninstallable. It can take a while.";

        // ---- Desempenho ----
        pt["Special_BalancedPlanTitle_Pros"] = "Volta ao plano padrão do Windows: o processador acelera quando precisa e economiza energia no resto do tempo.";
        pt["Special_BalancedPlanTitle_Cons"] = "Nenhuma: é o padrão do Windows. Em alguns PCs o processador demora um instante a mais para acelerar.";
        en["Special_BalancedPlanTitle_Pros"] = "Returns to the Windows default plan: the CPU speeds up when needed and saves power the rest of the time.";
        en["Special_BalancedPlanTitle_Cons"] = "None: it's the Windows default. On some PCs the CPU takes a moment longer to ramp up.";

        pt["Special_HighPerfTitle_Pros"] = "O processador fica pronto em alta velocidade, sem \"acordar\" devagar: menos engasgos em jogos e tarefas pesadas.";
        pt["Special_HighPerfTitle_Cons"] = "Gasta mais energia e esquenta um pouco mais; no notebook, a bateria dura menos.";
        en["Special_HighPerfTitle_Pros"] = "The CPU stays ready at high speed instead of \"waking up\" slowly: fewer stutters in games and heavy tasks.";
        en["Special_HighPerfTitle_Cons"] = "Uses more power and runs a bit warmer; on a laptop, the battery lasts less.";

        pt["Special_UltimateTitle_Pros"] = "O plano mais agressivo do Windows: desliga quase toda a economia de energia para tirar as menores latências.";
        pt["Special_UltimateTitle_Cons"] = "Consumo e temperatura maiores mesmo com o PC parado; não vale a pena no notebook usando bateria.";
        en["Special_UltimateTitle_Pros"] = "The most aggressive Windows plan: turns off nearly all power saving to squeeze out the lowest latency.";
        en["Special_UltimateTitle_Cons"] = "Higher power draw and temperature even when idle; not worth it on a laptop running on battery.";

        pt["Special_OptimizeDrivesTitle_Pros"] = "Abre a otimização do Windows: manda o TRIM no SSD e desfragmenta o HD, mantendo a velocidade do disco.";
        pt["Special_OptimizeDrivesTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows. A desfragmentação de HD pode deixar o PC lento enquanto roda.";
        en["Special_OptimizeDrivesTitle_Pros"] = "Opens Windows optimization: sends TRIM to SSDs and defragments HDDs, keeping the drive fast.";
        en["Special_OptimizeDrivesTitle_Cons"] = "None: it just opens the Windows tool. HDD defragmentation can slow the PC down while it runs.";

        // ---- Recuperação ----
        pt["Special_SystemRestoreTitle_Pros"] = "Volta o Windows para como estava em um ponto anterior: desfaz drivers, programas e ajustes que deram problema.";
        pt["Special_SystemRestoreTitle_Cons"] = "Programas e drivers instalados depois daquele ponto somem e precisam ser reinstalados. O PC reinicia durante o processo.";
        en["Special_SystemRestoreTitle_Pros"] = "Takes Windows back to how it was at an earlier point: undoes drivers, programs and settings that caused trouble.";
        en["Special_SystemRestoreTitle_Cons"] = "Programs and drivers installed after that point are removed and must be reinstalled. The PC restarts during the process.";

        // ---- Ferramentas do Windows ----
        pt["Special_GodModeTitle_Pros"] = "Cria na Área de Trabalho um atalho com centenas de configurações do Windows reunidas em uma lista só.";
        pt["Special_GodModeTitle_Cons"] = "Nenhuma: é só uma pasta especial; para remover, basta apagá-la.";
        en["Special_GodModeTitle_Pros"] = "Creates a Desktop shortcut with hundreds of Windows settings gathered in a single list.";
        en["Special_GodModeTitle_Cons"] = "None: it's just a special folder; delete it to remove it.";

        pt["Special_DeviceManagerTitle_Pros"] = "Mostra todo o hardware do PC e aponta peças sem driver ou com erro (o famoso triângulo amarelo).";
        pt["Special_DeviceManagerTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows. Cuidado apenas ao desinstalar ou desativar dispositivos por lá.";
        en["Special_DeviceManagerTitle_Pros"] = "Shows all the PC's hardware and flags parts with missing or broken drivers (the familiar yellow triangle).";
        en["Special_DeviceManagerTitle_Cons"] = "None: it just opens the Windows tool. Only be careful when uninstalling or disabling devices there.";

        pt["Special_DiskManagementTitle_Pros"] = "Mostra todos os discos e partições; serve para dar letra a um disco novo ou ver por que um HD não aparece.";
        pt["Special_DiskManagementTitle_Cons"] = "Abrir não muda nada, mas excluir ou formatar uma partição lá apaga todos os dados dela de vez.";
        en["Special_DiskManagementTitle_Pros"] = "Shows all drives and partitions; useful to assign a letter to a new drive or see why a disk isn't showing up.";
        en["Special_DiskManagementTitle_Cons"] = "Opening it changes nothing, but deleting or formatting a partition there wipes all its data for good.";

        pt["Special_ResourceMonitorTitle_Pros"] = "Mostra em detalhe qual programa está usando processador, memória, disco e internet agora.";
        pt["Special_ResourceMonitorTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows, sem mudar nada.";
        en["Special_ResourceMonitorTitle_Pros"] = "Shows in detail which program is using CPU, memory, disk and network right now.";
        en["Special_ResourceMonitorTitle_Cons"] = "None: it just opens the Windows tool, without changing anything.";

        pt["Special_PerfMonitorTitle_Pros"] = "Gera gráficos e relatórios detalhados do desempenho do PC ao longo do tempo.";
        pt["Special_PerfMonitorTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows. É mais técnica e pode confundir quem nunca usou.";
        en["Special_PerfMonitorTitle_Pros"] = "Produces detailed charts and reports of the PC's performance over time.";
        en["Special_PerfMonitorTitle_Cons"] = "None: it just opens the Windows tool. It's fairly technical and can be confusing at first.";

        pt["Special_EventViewerTitle_Pros"] = "Mostra o histórico de erros do Windows; ajuda a descobrir a causa de travamentos e reinícios sozinhos.";
        pt["Special_EventViewerTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows. É normal ver muitos avisos, a maioria inofensiva.";
        en["Special_EventViewerTitle_Pros"] = "Shows the Windows error history; helps find the cause of crashes and random restarts.";
        en["Special_EventViewerTitle_Cons"] = "None: it just opens the Windows tool. Seeing many warnings is normal; most are harmless.";

        pt["Special_ServicesTitle_Pros"] = "Lista os serviços que rodam em segundo plano e permite iniciar, parar ou mudar como cada um inicia.";
        pt["Special_ServicesTitle_Cons"] = "Abrir não muda nada, mas desativar o serviço errado pode quebrar o som, a internet ou o Windows Update.";
        en["Special_ServicesTitle_Pros"] = "Lists background services and lets you start, stop or change how each one starts.";
        en["Special_ServicesTitle_Cons"] = "Opening it changes nothing, but disabling the wrong service can break sound, internet or Windows Update.";

        pt["Special_GroupPolicyTitle_Pros"] = "Dá acesso a ajustes avançados do Windows que não aparecem nas Configurações normais.";
        pt["Special_GroupPolicyTitle_Cons"] = "Só existe no Windows Pro/Enterprise. Uma política mal configurada pode travar recursos e ser difícil de achar depois.";
        en["Special_GroupPolicyTitle_Pros"] = "Gives access to advanced Windows settings that don't show up in the regular Settings app.";
        en["Special_GroupPolicyTitle_Cons"] = "Only on Windows Pro/Enterprise. A misconfigured policy can lock features and be hard to track down later.";

        pt["Special_MemoryDiagnosticTitle_Pros"] = "Testa a memória RAM e confirma se ela é a causa de telas azuis e travamentos aleatórios.";
        pt["Special_MemoryDiagnosticTitle_Cons"] = "O teste exige reiniciar o PC e leva de 10 a 30 minutos; salve seus trabalhos antes de escolher \"Reiniciar agora\".";
        en["Special_MemoryDiagnosticTitle_Pros"] = "Tests your RAM and confirms whether it's behind blue screens and random crashes.";
        en["Special_MemoryDiagnosticTitle_Cons"] = "The test requires a restart and takes 10 to 30 minutes; save your work before choosing \"Restart now\".";

        pt["Special_TaskManagerTitle_Pros"] = "Mostra o que está pesando no PC e permite fechar programas travados.";
        pt["Special_TaskManagerTitle_Cons"] = "Nenhuma: só abre a ferramenta do Windows. Encerrar um processo do sistema por lá pode travar o PC.";
        en["Special_TaskManagerTitle_Pros"] = "Shows what's weighing on the PC and lets you close frozen programs.";
        en["Special_TaskManagerTitle_Cons"] = "None: it just opens the Windows tool. Ending a system process there can freeze the PC.";

        // ---- Rede ----
        pt["Special_RenewNetworkTitle_Pros"] = "Limpa o cache de DNS e pede um endereço novo ao roteador; resolve sites que não abrem e falhas de conexão simples.";
        pt["Special_RenewNetworkTitle_Cons"] = "Quase nenhuma: a conexão pode piscar por um ou dois segundos, então evite no meio de uma partida.";
        en["Special_RenewNetworkTitle_Pros"] = "Clears the DNS cache and asks the router for a fresh address; fixes sites that won't load and simple connection failures.";
        en["Special_RenewNetworkTitle_Cons"] = "Almost none: the connection may blink for a second or two, so avoid it mid-match.";

        pt["Special_ResetNetworkTitle_Pros"] = "Restaura a pilha de rede do Windows (Winsock e IP) ao padrão; resolve internet quebrada que nada mais conserta.";
        pt["Special_ResetNetworkTitle_Cons"] = "Exige reiniciar o PC, apaga IP e DNS configurados à mão e pode fazer VPNs e alguns antivírus precisarem ser reinstalados.";
        en["Special_ResetNetworkTitle_Pros"] = "Resets the Windows network stack (Winsock and IP) to defaults; fixes broken internet nothing else can fix.";
        en["Special_ResetNetworkTitle_Cons"] = "Requires a restart, wipes manually set IP and DNS, and may make VPNs and some antivirus need reinstalling.";
    }
}
