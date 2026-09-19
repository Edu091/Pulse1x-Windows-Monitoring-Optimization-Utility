namespace Pulse1x.App.Localization;

/// <summary>
/// Textos da seção Personalização do Windows. Segue o mesmo padrão do GameHub: um arquivo
/// próprio, preenchido pelo construtor estático da tabela principal.
/// </summary>
internal static partial class LocalizationStrings
{
    private static void AddWinCustomPortuguese()
    {
        var t = Pt;

        // ---- Navegação e cabeçalho ----
        t["Nav_WinCustom"] = "Personalização do Windows";
        t["WinCustom_Title"] = "Personalização do Windows";
        t["WinCustom_Subtitle"] = "Mude o visual da barra de tarefas, do Menu Iniciar, do Explorer e das Configurações. Tudo é reversível.";

        // ---- Componentes ----
        t["WinCustom_Component_Taskbar"] = "Barra de tarefas";
        t["WinCustom_Component_StartMenu"] = "Menu Iniciar";
        t["WinCustom_Component_Explorer"] = "Explorador de Arquivos";
        t["WinCustom_Component_Settings"] = "Configurações do Windows";

        // ---- Regiões ----
        t["WinCustom_Region_Taskbar"] = "Barra de tarefas";

        t["WinCustom_ExplorerRegion_Window"] = "Janela inteira";
        t["WinCustom_ExplorerRegion_Content"] = "Fundo principal (arquivos e pastas)";
        t["WinCustom_ExplorerRegion_Sidebar"] = "Painel lateral";
        t["WinCustom_ExplorerRegion_TopBar"] = "Barra superior";
        t["WinCustom_ExplorerRegion_AddressBar"] = "Barra de endereço";
        t["WinCustom_ExplorerRegion_CommandBar"] = "Barra de comandos";
        t["WinCustom_ExplorerRegion_Search"] = "Pesquisa";
        t["WinCustom_ExplorerRegion_Tabs"] = "Abas";
        t["WinCustom_ExplorerRegion_DetailsPane"] = "Painel de detalhes";

        t["WinCustom_StartRegion_Window"] = "Menu inteiro";
        t["WinCustom_StartRegion_Pinned"] = "Aplicativos fixados";
        t["WinCustom_StartRegion_Recommended"] = "Recomendados";
        t["WinCustom_StartRegion_Search"] = "Pesquisa";
        t["WinCustom_StartRegion_Footer"] = "Rodapé";

        t["WinCustom_SettingsRegion_Window"] = "Janela inteira";
        t["WinCustom_SettingsRegion_Content"] = "Fundo principal";
        t["WinCustom_SettingsRegion_Sidebar"] = "Barra lateral";
        t["WinCustom_SettingsRegion_Cards"] = "Cards";
        t["WinCustom_SettingsRegion_Containers"] = "Contêineres";
        t["WinCustom_SettingsRegion_Header"] = "Áreas superiores";

        // ---- Tipos de aparência ----
        t["WinCustom_Kind_WindowsDefault"] = "Padrão do Windows";
        t["WinCustom_Kind_Transparent"] = "Fundo transparente";
        t["WinCustom_Kind_Translucent"] = "Fundo translúcido";
        t["WinCustom_Kind_Blur"] = "Blur";
        t["WinCustom_Kind_Glass"] = "Glass";
        t["WinCustom_Kind_Acrylic"] = "Acrylic";
        t["WinCustom_Kind_Mica"] = "Mica";
        t["WinCustom_Kind_SolidColor"] = "Cor sólida";
        t["WinCustom_Kind_Gradient"] = "Gradiente";
        t["WinCustom_Kind_Image"] = "Imagem personalizada";

        // ---- Ajustes ----
        t["WinCustom_Appearance"] = "Aparência";
        t["WinCustom_Opacity"] = "Opacidade";
        t["WinCustom_Transparency"] = "Transparência";
        t["WinCustom_BlurRadius"] = "Intensidade do blur";
        t["WinCustom_EffectIntensity"] = "Intensidade dos efeitos";
        t["WinCustom_TintColor"] = "Cor / Tint";
        t["WinCustom_TintIntensity"] = "Intensidade do tint";
        t["WinCustom_Saturation"] = "Saturação";
        t["WinCustom_Luminosity"] = "Luminosidade";
        t["WinCustom_Darken"] = "Escurecimento";
        t["WinCustom_CornerRadius"] = "Border radius";
        t["WinCustom_SolidColorLabel"] = "Cor";
        t["WinCustom_GradientStart"] = "Cor inicial";
        t["WinCustom_GradientEnd"] = "Cor final";
        t["WinCustom_GradientDirection"] = "Direção";
        t["WinCustom_GradientVertical"] = "Vertical";
        t["WinCustom_GradientHorizontal"] = "Horizontal";
        t["WinCustom_GradientDiagonalDown"] = "Diagonal ↘";
        t["WinCustom_GradientDiagonalUp"] = "Diagonal ↗";

        t["WinCustom_Image"] = "Imagem de fundo";
        t["WinCustom_PickImage"] = "Escolher imagem...";
        t["WinCustom_ClearImage"] = "Remover";
        t["WinCustom_ImageFit"] = "Ajuste";
        t["WinCustom_Fit_Fill"] = "Preencher";
        t["WinCustom_Fit_Fit"] = "Ajustar";
        t["WinCustom_Fit_Stretch"] = "Esticar";
        t["WinCustom_Fit_Center"] = "Centralizar";
        t["WinCustom_Fit_Tile"] = "Lado a lado";
        t["WinCustom_Fit_Crop"] = "Recorte manual";
        t["WinCustom_ImageOffsetX"] = "Posição horizontal";
        t["WinCustom_ImageOffsetY"] = "Posição vertical";
        t["WinCustom_ImageScale"] = "Escala";
        t["WinCustom_ImageOpacity"] = "Opacidade da imagem";

        t["WinCustom_Preview"] = "Preview";
        t["WinCustom_PreviewHint"] = "Prévia aproximada. Efeitos de desfoque dependem do que estiver atrás da janela no Windows.";
        t["WinCustom_ResetTarget"] = "Voltar ao padrão";

        // ---- Disponibilidade ----
        t["WinCustom_NeedsInjection"] = "Requer o motor avançado (beta)";
        t["WinCustom_UnsupportedBuild"] = "Não disponível nesta versão do Windows";
        t["WinCustom_NotApplicable"] = "Não se aplica a este componente";
        t["WinCustom_Unavailable"] = "Indisponível";
        t["WinCustom_Beta"] = "BETA";
        t["WinCustom_InjectionMissing"] =
            "O motor avançado não está incluído nesta instalação. Menu Iniciar, Configurações e as " +
            "áreas internas do Explorer dependem dele e aparecem desativados. A barra de tarefas e a " +
            "janela do Explorer funcionam normalmente sem ele.";
        t["WinCustom_EnableInjection"] = "Motor avançado (beta)";
        t["WinCustom_EnableInjectionHint"] =
            "Necessário para Menu Iniciar, Configurações e áreas internas do Explorer. " +
            "Por atuar dentro desses processos, é a parte de maior risco — ative por sua conta e risco.";

        // ---- Temas ----
        t["WinCustom_Themes"] = "Temas";
        t["WinCustom_NewTheme"] = "Novo";
        t["WinCustom_NewThemeName"] = "Meu tema";
        t["WinCustom_Duplicate"] = "Duplicar";
        t["WinCustom_Rename"] = "Renomear";
        t["WinCustom_Delete"] = "Excluir";
        t["WinCustom_Apply"] = "Aplicar";
        t["WinCustom_Import"] = "Importar";
        t["WinCustom_Export"] = "Exportar";
        t["WinCustom_BuiltInTheme"] = "Tema de fábrica — duplique para editar.";
        t["WinCustom_RenameTitle"] = "Renomear tema";
        t["WinCustom_RenamePrompt"] = "Novo nome do tema:";
        t["WinCustom_DeleteConfirm"] = "Excluir o tema \"{0}\"? Esta ação não pode ser desfeita.";
        t["WinCustom_Exported"] = "Tema exportado.";
        t["WinCustom_Imported"] = "Tema importado.";
        t["WinCustom_ImportFailed"] = "Não foi possível importar o arquivo.";
        t["WinCustom_ThemeFilter"] = "Tema do Pulse1x (*.pulsetheme)|*.pulsetheme";
        t["WinCustom_ImageFilter"] = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Todos os arquivos|*.*";

        // ---- Sincronização ----
        t["WinCustom_Sync"] = "Sincronizar aparência";
        t["WinCustom_SyncHint"] =
            "Aplica o estilo da barra de tarefas aos demais componentes. Desligue para personalizar cada um separadamente.";

        // ---- Status ----
        t["WinCustom_Status"] = "Status";
        t["WinCustom_StatusLabel"] = "Status:";
        t["WinCustom_StartupLabel"] = "Inicialização com Windows:";
        t["WinCustom_ThemeLabel"] = "Tema atual:";
        t["WinCustom_HostLabel"] = "Serviço/Host:";
        t["WinCustom_CompatLabel"] = "Compatibilidade:";
        t["WinCustom_StateActive"] = "Ativa";
        t["WinCustom_StatePaused"] = "Pausada";
        t["WinCustom_StateStopped"] = "Parada";
        t["WinCustom_StateUnsupported"] = "Não suportada";
        t["WinCustom_HostRunning"] = "Executando";
        t["WinCustom_HostStopped"] = "Parado";
        t["WinCustom_Yes"] = "Ativa";
        t["WinCustom_No"] = "Desativada";
        t["WinCustom_NoTheme"] = "Nenhum";
        t["WinCustom_CompatOk"] = "Windows 11 (build {0}) — compatível";
        t["WinCustom_CompatUnsupported"] = "Build {0} — a personalização exige Windows 11";

        t["WinCustom_Enabled"] = "Personalização ativa";
        t["WinCustom_StartWithWindows"] = "Iniciar personalização junto com o Windows";
        t["WinCustom_RestoreLastTheme"] = "Restaurar último tema automaticamente";
        t["WinCustom_ReapplyOnExplorer"] = "Reaplicar personalização se o Explorer reiniciar";

        t["WinCustom_Restart"] = "Reiniciar personalização";
        t["WinCustom_Pause"] = "Parar temporariamente";
        t["WinCustom_Resume"] = "Reativar";
        t["WinCustom_Reapply"] = "Restaurar o tema atual";
        t["WinCustom_Restarted"] = "Personalização reiniciada.";
        t["WinCustom_Paused"] = "Personalização pausada.";
        t["WinCustom_Resumed"] = "Personalização reativada.";
        t["WinCustom_Reapplied"] = "Tema reaplicado.";

        // ---- Restauração ----
        t["WinCustom_Restore"] = "Reversibilidade";
        t["WinCustom_RestoreTaskbar"] = "Restaurar barra de tarefas";
        t["WinCustom_RestoreExplorer"] = "Restaurar Explorer";
        t["WinCustom_RestoreStart"] = "Restaurar Menu Iniciar";
        t["WinCustom_RestoreSettings"] = "Restaurar Configurações";
        t["WinCustom_RestoreAll"] = "RESTAURAR TODA A PERSONALIZAÇÃO DO WINDOWS";
        t["WinCustom_RestoreAllConfirm"] =
            "Isto devolve a barra de tarefas, o Menu Iniciar, o Explorer e as Configurações ao visual " +
            "padrão do Windows, desliga a inicialização automática e desativa a personalização.\n\nContinuar?";
        t["WinCustom_RestoredTaskbar"] = "Barra de tarefas restaurada.";
        t["WinCustom_RestoredExplorer"] = "Explorer restaurado.";
        t["WinCustom_RestoredStart"] = "Menu Iniciar restaurado.";
        t["WinCustom_RestoredSettings"] = "Configurações restauradas.";
        t["WinCustom_RestoredAll"] = "Toda a personalização foi removida. O Windows está no visual padrão.";
        t["WinCustom_RestartExplorer"] = "Reiniciar o Explorer";
        t["WinCustom_ExplorerRestarted"] = "Explorer reiniciado.";

        t["WinCustom_Emergency"] = "Restauração de emergência";
        t["WinCustom_EmergencyHint"] =
            "Cria um atalho que devolve o Windows ao padrão mesmo que o Pulse não abra ou um tema " +
            "quebre. Guarde-o em um lugar fácil de achar.";
        t["WinCustom_CreateEmergency"] = "Criar atalho de recuperação";
        t["WinCustom_EmergencyCreated"] = "Atalho de recuperação criado em: {0}";

        // ---- Proteção contra falhas ----
        t["WinCustom_Quarantine"] = "Proteção contra falhas";
        t["WinCustom_QuarantinedTarget"] =
            "A personalização de {0} foi interrompida automaticamente após falhas repetidas e o " +
            "componente foi restaurado.";
        t["WinCustom_ClearQuarantine"] = "Tentar novamente";

        // ---- Confirmação com reversão automática ----
        t["WinCustom_ConfirmTitle"] = "Manter esta personalização?";
        t["WinCustom_ConfirmMessage"] =
            "O tema \"{0}\" foi aplicado. Se algo ficou ilegível ou fora do lugar, não faça nada — " +
            "o Pulse volta ao visual anterior sozinho.";
        t["WinCustom_ConfirmCountdown"] = "Revertendo automaticamente em {0}s...";
        t["WinCustom_ConfirmKeep"] = "Manter";
        t["WinCustom_ConfirmRevert"] = "Reverter agora";
        t["WinCustom_Applied"] = "Tema \"{0}\" aplicado.";
        t["WinCustom_Reverted"] = "Personalização revertida ao estado anterior.";

        // Comum a diálogos desta seção.
        t["Common_Cancel"] = "Cancelar";
    }

    private static void AddWinCustomEnglish()
    {
        var t = En;

        t["Nav_WinCustom"] = "Windows Personalization";
        t["WinCustom_Title"] = "Windows Personalization";
        t["WinCustom_Subtitle"] = "Change how the taskbar, Start menu, File Explorer and Settings look. Everything is reversible.";

        t["WinCustom_Component_Taskbar"] = "Taskbar";
        t["WinCustom_Component_StartMenu"] = "Start menu";
        t["WinCustom_Component_Explorer"] = "File Explorer";
        t["WinCustom_Component_Settings"] = "Windows Settings";

        t["WinCustom_Region_Taskbar"] = "Taskbar";

        t["WinCustom_ExplorerRegion_Window"] = "Whole window";
        t["WinCustom_ExplorerRegion_Content"] = "Main background (files and folders)";
        t["WinCustom_ExplorerRegion_Sidebar"] = "Navigation pane";
        t["WinCustom_ExplorerRegion_TopBar"] = "Top bar";
        t["WinCustom_ExplorerRegion_AddressBar"] = "Address bar";
        t["WinCustom_ExplorerRegion_CommandBar"] = "Command bar";
        t["WinCustom_ExplorerRegion_Search"] = "Search";
        t["WinCustom_ExplorerRegion_Tabs"] = "Tabs";
        t["WinCustom_ExplorerRegion_DetailsPane"] = "Details pane";

        t["WinCustom_StartRegion_Window"] = "Whole menu";
        t["WinCustom_StartRegion_Pinned"] = "Pinned apps";
        t["WinCustom_StartRegion_Recommended"] = "Recommended";
        t["WinCustom_StartRegion_Search"] = "Search";
        t["WinCustom_StartRegion_Footer"] = "Footer";

        t["WinCustom_SettingsRegion_Window"] = "Whole window";
        t["WinCustom_SettingsRegion_Content"] = "Main background";
        t["WinCustom_SettingsRegion_Sidebar"] = "Sidebar";
        t["WinCustom_SettingsRegion_Cards"] = "Cards";
        t["WinCustom_SettingsRegion_Containers"] = "Containers";
        t["WinCustom_SettingsRegion_Header"] = "Header areas";

        t["WinCustom_Kind_WindowsDefault"] = "Windows default";
        t["WinCustom_Kind_Transparent"] = "Transparent";
        t["WinCustom_Kind_Translucent"] = "Translucent";
        t["WinCustom_Kind_Blur"] = "Blur";
        t["WinCustom_Kind_Glass"] = "Glass";
        t["WinCustom_Kind_Acrylic"] = "Acrylic";
        t["WinCustom_Kind_Mica"] = "Mica";
        t["WinCustom_Kind_SolidColor"] = "Solid color";
        t["WinCustom_Kind_Gradient"] = "Gradient";
        t["WinCustom_Kind_Image"] = "Custom image";

        t["WinCustom_Appearance"] = "Appearance";
        t["WinCustom_Opacity"] = "Opacity";
        t["WinCustom_Transparency"] = "Transparency";
        t["WinCustom_BlurRadius"] = "Blur strength";
        t["WinCustom_EffectIntensity"] = "Effect strength";
        t["WinCustom_TintColor"] = "Color / Tint";
        t["WinCustom_TintIntensity"] = "Tint strength";
        t["WinCustom_Saturation"] = "Saturation";
        t["WinCustom_Luminosity"] = "Brightness";
        t["WinCustom_Darken"] = "Darkening";
        t["WinCustom_CornerRadius"] = "Border radius";
        t["WinCustom_SolidColorLabel"] = "Color";
        t["WinCustom_GradientStart"] = "Start color";
        t["WinCustom_GradientEnd"] = "End color";
        t["WinCustom_GradientDirection"] = "Direction";
        t["WinCustom_GradientVertical"] = "Vertical";
        t["WinCustom_GradientHorizontal"] = "Horizontal";
        t["WinCustom_GradientDiagonalDown"] = "Diagonal ↘";
        t["WinCustom_GradientDiagonalUp"] = "Diagonal ↗";

        t["WinCustom_Image"] = "Background image";
        t["WinCustom_PickImage"] = "Choose image...";
        t["WinCustom_ClearImage"] = "Remove";
        t["WinCustom_ImageFit"] = "Fit";
        t["WinCustom_Fit_Fill"] = "Fill";
        t["WinCustom_Fit_Fit"] = "Fit";
        t["WinCustom_Fit_Stretch"] = "Stretch";
        t["WinCustom_Fit_Center"] = "Center";
        t["WinCustom_Fit_Tile"] = "Tile";
        t["WinCustom_Fit_Crop"] = "Manual crop";
        t["WinCustom_ImageOffsetX"] = "Horizontal position";
        t["WinCustom_ImageOffsetY"] = "Vertical position";
        t["WinCustom_ImageScale"] = "Scale";
        t["WinCustom_ImageOpacity"] = "Image opacity";

        t["WinCustom_Preview"] = "Preview";
        t["WinCustom_PreviewHint"] = "Approximate preview. Blur effects depend on what sits behind the window in Windows.";
        t["WinCustom_ResetTarget"] = "Back to default";

        t["WinCustom_NeedsInjection"] = "Requires the advanced engine (beta)";
        t["WinCustom_UnsupportedBuild"] = "Not available on this Windows version";
        t["WinCustom_NotApplicable"] = "Does not apply to this component";
        t["WinCustom_Unavailable"] = "Unavailable";
        t["WinCustom_Beta"] = "BETA";
        t["WinCustom_InjectionMissing"] =
            "The advanced engine is not included in this installation. Start menu, Settings and the " +
            "inner areas of Explorer depend on it and appear disabled. The taskbar and the Explorer " +
            "window work normally without it.";
        t["WinCustom_EnableInjection"] = "Advanced engine (beta)";
        t["WinCustom_EnableInjectionHint"] =
            "Required for Start menu, Settings and inner Explorer areas. Because it runs inside those " +
            "processes, it is the riskiest part — enable at your own risk.";

        t["WinCustom_Themes"] = "Themes";
        t["WinCustom_NewTheme"] = "New";
        t["WinCustom_NewThemeName"] = "My theme";
        t["WinCustom_Duplicate"] = "Duplicate";
        t["WinCustom_Rename"] = "Rename";
        t["WinCustom_Delete"] = "Delete";
        t["WinCustom_Apply"] = "Apply";
        t["WinCustom_Import"] = "Import";
        t["WinCustom_Export"] = "Export";
        t["WinCustom_BuiltInTheme"] = "Built-in theme — duplicate it to edit.";
        t["WinCustom_RenameTitle"] = "Rename theme";
        t["WinCustom_RenamePrompt"] = "New theme name:";
        t["WinCustom_DeleteConfirm"] = "Delete the theme \"{0}\"? This cannot be undone.";
        t["WinCustom_Exported"] = "Theme exported.";
        t["WinCustom_Imported"] = "Theme imported.";
        t["WinCustom_ImportFailed"] = "The file could not be imported.";
        t["WinCustom_ThemeFilter"] = "Pulse1x theme (*.pulsetheme)|*.pulsetheme";
        t["WinCustom_ImageFilter"] = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*";

        t["WinCustom_Sync"] = "Sync appearance";
        t["WinCustom_SyncHint"] =
            "Applies the taskbar style to the other components. Turn it off to customize each one separately.";

        t["WinCustom_Status"] = "Status";
        t["WinCustom_StatusLabel"] = "Status:";
        t["WinCustom_StartupLabel"] = "Start with Windows:";
        t["WinCustom_ThemeLabel"] = "Current theme:";
        t["WinCustom_HostLabel"] = "Service/Host:";
        t["WinCustom_CompatLabel"] = "Compatibility:";
        t["WinCustom_StateActive"] = "Active";
        t["WinCustom_StatePaused"] = "Paused";
        t["WinCustom_StateStopped"] = "Stopped";
        t["WinCustom_StateUnsupported"] = "Unsupported";
        t["WinCustom_HostRunning"] = "Running";
        t["WinCustom_HostStopped"] = "Stopped";
        t["WinCustom_Yes"] = "Enabled";
        t["WinCustom_No"] = "Disabled";
        t["WinCustom_NoTheme"] = "None";
        t["WinCustom_CompatOk"] = "Windows 11 (build {0}) — compatible";
        t["WinCustom_CompatUnsupported"] = "Build {0} — personalization requires Windows 11";

        t["WinCustom_Enabled"] = "Personalization active";
        t["WinCustom_StartWithWindows"] = "Start personalization with Windows";
        t["WinCustom_RestoreLastTheme"] = "Restore last theme automatically";
        t["WinCustom_ReapplyOnExplorer"] = "Reapply personalization if Explorer restarts";

        t["WinCustom_Restart"] = "Restart personalization";
        t["WinCustom_Pause"] = "Pause temporarily";
        t["WinCustom_Resume"] = "Resume";
        t["WinCustom_Reapply"] = "Restore current theme";
        t["WinCustom_Restarted"] = "Personalization restarted.";
        t["WinCustom_Paused"] = "Personalization paused.";
        t["WinCustom_Resumed"] = "Personalization resumed.";
        t["WinCustom_Reapplied"] = "Theme reapplied.";

        t["WinCustom_Restore"] = "Reversibility";
        t["WinCustom_RestoreTaskbar"] = "Restore taskbar";
        t["WinCustom_RestoreExplorer"] = "Restore Explorer";
        t["WinCustom_RestoreStart"] = "Restore Start menu";
        t["WinCustom_RestoreSettings"] = "Restore Settings";
        t["WinCustom_RestoreAll"] = "RESTORE ALL WINDOWS PERSONALIZATION";
        t["WinCustom_RestoreAllConfirm"] =
            "This returns the taskbar, Start menu, Explorer and Settings to the default Windows look, " +
            "turns off automatic startup and disables personalization.\n\nContinue?";
        t["WinCustom_RestoredTaskbar"] = "Taskbar restored.";
        t["WinCustom_RestoredExplorer"] = "Explorer restored.";
        t["WinCustom_RestoredStart"] = "Start menu restored.";
        t["WinCustom_RestoredSettings"] = "Settings restored.";
        t["WinCustom_RestoredAll"] = "All personalization removed. Windows is back to its default look.";
        t["WinCustom_RestartExplorer"] = "Restart Explorer";
        t["WinCustom_ExplorerRestarted"] = "Explorer restarted.";

        t["WinCustom_Emergency"] = "Emergency recovery";
        t["WinCustom_EmergencyHint"] =
            "Creates a shortcut that returns Windows to default even if Pulse will not open or a theme " +
            "breaks. Keep it somewhere easy to find.";
        t["WinCustom_CreateEmergency"] = "Create recovery shortcut";
        t["WinCustom_EmergencyCreated"] = "Recovery shortcut created at: {0}";

        t["WinCustom_Quarantine"] = "Failure protection";
        t["WinCustom_QuarantinedTarget"] =
            "Personalization of {0} was stopped automatically after repeated failures and the component " +
            "was restored.";
        t["WinCustom_ClearQuarantine"] = "Try again";

        t["WinCustom_ConfirmTitle"] = "Keep this personalization?";
        t["WinCustom_ConfirmMessage"] =
            "The theme \"{0}\" has been applied. If anything became unreadable or misplaced, do nothing — " +
            "Pulse will return to the previous look on its own.";
        t["WinCustom_ConfirmCountdown"] = "Reverting automatically in {0}s...";
        t["WinCustom_ConfirmKeep"] = "Keep";
        t["WinCustom_ConfirmRevert"] = "Revert now";
        t["WinCustom_Applied"] = "Theme \"{0}\" applied.";
        t["WinCustom_Reverted"] = "Personalization reverted to the previous state.";

        t["Common_Cancel"] = "Cancel";
    }
}
