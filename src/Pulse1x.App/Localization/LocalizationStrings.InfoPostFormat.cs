namespace Pulse1x.App.Localization;

/// <summary>Vantagens e desvantagens ("&lt;chave&gt;_Pros" / "&lt;chave&gt;_Cons") das funções desta área.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddInfoPostFormat()
    {
        var pt = Pt;
        var en = En;

        pt["PfInfo_restore_point_Pros"] = "Guarda uma \"foto\" do Windows agora; se algum programa ou driver der problema depois, dá para voltar a este ponto.";
        pt["PfInfo_restore_point_Cons"] = "Quase nenhuma: ocupa um pouco de espaço no disco e leva um ou dois minutos para criar.";
        en["PfInfo_restore_point_Pros"] = "Saves a \"snapshot\" of Windows now; if a program or driver causes trouble later, you can go back to this point.";
        en["PfInfo_restore_point_Cons"] = "Almost none: it takes a little disk space and a minute or two to create.";

        pt["PfInfo_enable_system_restore_Pros"] = "Liga a Proteção do Sistema no C:, que vem desligada em muitas instalações; sem ela não dá para criar pontos de restauração.";
        pt["PfInfo_enable_system_restore_Cons"] = "Reserva uma parte do disco C: para guardar os pontos de restauração.";
        en["PfInfo_enable_system_restore_Pros"] = "Turns on System Protection for C:, which is off on many installs; without it you can't create restore points.";
        en["PfInfo_enable_system_restore_Cons"] = "Reserves part of drive C: to store the restore points.";

        pt["PfInfo_high_performance_Pros"] = "O processador fica pronto em alta velocidade, sem \"acordar\" devagar: menos engasgos em jogos e tarefas pesadas.";
        pt["PfInfo_high_performance_Cons"] = "Gasta mais energia e esquenta um pouco mais; no notebook, a bateria dura menos.";
        en["PfInfo_high_performance_Pros"] = "The CPU stays ready at high speed instead of \"waking up\" slowly: fewer stutters in games and heavy tasks.";
        en["PfInfo_high_performance_Cons"] = "Uses more power and runs a bit warmer; on a laptop, the battery lasts less.";

        pt["PfInfo_ultimate_performance_Pros"] = "Cria e ativa o plano mais agressivo do Windows, que desliga quase toda a economia de energia para tirar as menores latências.";
        pt["PfInfo_ultimate_performance_Cons"] = "Consumo e temperatura maiores mesmo com o PC parado; não vale a pena no notebook usando bateria.";
        en["PfInfo_ultimate_performance_Pros"] = "Creates and enables the most aggressive Windows plan, which turns off nearly all power saving for the lowest latency.";
        en["PfInfo_ultimate_performance_Cons"] = "Higher power draw and temperature even when idle; not worth it on a laptop running on battery.";

        pt["PfInfo_disable_suggestions_Pros"] = "Some com as dicas, apps sugeridos e propagandas do menu Iniciar e da tela de bloqueio.";
        pt["PfInfo_disable_suggestions_Cons"] = "Nenhuma relevante: você só deixa de ver dicas de recursos novos do Windows.";
        en["PfInfo_disable_suggestions_Pros"] = "Removes tips, suggested apps and ads from the Start menu and lock screen.";
        en["PfInfo_disable_suggestions_Cons"] = "Nothing significant: you just stop seeing tips about new Windows features.";

        pt["PfInfo_reduce_telemetry_Pros"] = "O Windows passa a enviar à Microsoft só os dados de diagnóstico obrigatórios, menos coleta em segundo plano.";
        pt["PfInfo_reduce_telemetry_Cons"] = "É aplicado como política: as Configurações podem mostrar \"gerenciado pela organização\" e não dá para participar do Programa Windows Insider.";
        en["PfInfo_reduce_telemetry_Pros"] = "Windows sends Microsoft only the required diagnostic data, with less background collection.";
        en["PfInfo_reduce_telemetry_Cons"] = "It's applied as a policy: Settings may show \"managed by your organization\" and you can't join the Windows Insider Program.";

        pt["PfInfo_show_extensions_Pros"] = "Mostra o final do nome dos arquivos (.exe, .pdf): fica fácil perceber um vírus disfarçado, como \"foto.jpg.exe\".";
        pt["PfInfo_show_extensions_Cons"] = "Quase nenhuma: ao renomear um arquivo, cuidado para não apagar a extensão sem querer.";
        en["PfInfo_show_extensions_Pros"] = "Shows the end of file names (.exe, .pdf): it becomes easy to spot disguised malware like \"photo.jpg.exe\".";
        en["PfInfo_show_extensions_Cons"] = "Almost none: when renaming a file, be careful not to delete the extension by accident.";

        pt["PfInfo_show_hidden_Pros"] = "Mostra pastas ocultas como AppData, onde ficam saves de jogos, configurações e mods.";
        pt["PfInfo_show_hidden_Cons"] = "As pastas ficam mais poluídas, e apagar algo oculto sem saber o que é pode quebrar um programa.";
        en["PfInfo_show_hidden_Pros"] = "Shows hidden folders like AppData, where game saves, settings and mods live.";
        en["PfInfo_show_hidden_Cons"] = "Folders look more cluttered, and deleting a hidden item without knowing what it is can break a program.";

        pt["PfInfo_explorer_this_pc_Pros"] = "O Explorador abre direto nos seus discos, sem a lista de arquivos recentes do Acesso Rápido.";
        pt["PfInfo_explorer_this_pc_Cons"] = "Nenhuma: o Acesso Rápido continua na barra lateral. Pode ser preciso reabrir o Explorador para valer.";
        en["PfInfo_explorer_this_pc_Pros"] = "File Explorer opens straight to your drives, without the Quick Access list of recent files.";
        en["PfInfo_explorer_this_pc_Cons"] = "None: Quick Access stays in the sidebar. You may need to reopen Explorer for it to apply.";

        pt["PfInfo_manage_startup_Pros"] = "Abre a lista de programas que ligam junto com o Windows; desativar os desnecessários deixa o boot mais rápido.";
        pt["PfInfo_manage_startup_Cons"] = "Nenhuma: só abre o Gerenciador de Tarefas. Desativar ali o driver de áudio ou do touchpad pode tirar recursos deles.";
        en["PfInfo_manage_startup_Pros"] = "Opens the list of programs that start with Windows; disabling the unneeded ones makes boot faster.";
        en["PfInfo_manage_startup_Cons"] = "None: it just opens Task Manager. Disabling the audio or touchpad utility there may remove some of their features.";
    }
}
