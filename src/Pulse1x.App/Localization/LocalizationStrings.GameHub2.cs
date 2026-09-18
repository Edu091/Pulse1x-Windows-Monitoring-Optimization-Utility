namespace Pulse1x.App.Localization;

/// <summary>
/// Segunda leva de textos do GameHub: assistente de adicionar jogos, escolha de capas, sons e as
/// preferências próprias do hub. Fica em arquivo separado apenas para manter cada tabela legível.
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddGameHub2Portuguese()
    {
        var t = Pt;

        // ---- Assistente de adicionar jogos ----
        t["GH_AddTitle"] = "Adicionar jogos";
        t["GH_AddSubtitle"] = "Escolha de onde o Pulse1x deve trazer os jogos para a sua biblioteca.";
        t["GH_AddStores"] = "Procurar nas lojas instaladas";
        t["GH_AddStoresHint"] = "Detecta automaticamente o que já está instalado pela Steam, Epic Games, GOG, EA App e Ubisoft Connect.";
        t["GH_AddFolder"] = "Varrer uma pasta";
        t["GH_AddFolderHint"] = "Procura executáveis dentro de uma pasta do seu computador — ideal para jogos portáteis ou instalados à mão.";
        t["GH_AddFile"] = "Adicionar um arquivo ou atalho";
        t["GH_AddFileHint"] = "Aponte um .exe, um atalho ou um comando específico. Serve para jogos e também para aplicativos.";
        t["GH_AddEmulator"] = "Adicionar um emulador e suas ROMs";
        t["GH_AddEmulatorHint"] = "Cadastre o emulador e a pasta das ROMs: cada ROM vira um item normal da biblioteca, com capa e botão Jogar.";
        t["GH_AddShortcuts"] = "Importar atalhos do sistema";
        t["GH_AddShortcutsHint"] = "Traz os atalhos do Menu Iniciar e da Área de Trabalho. É a fonte mais ampla — e a que mais precisa de limpeza depois.";
        t["GH_AddRun"] = "Procurar agora";
        t["GH_AddApply"] = "Adicionar";
        t["GH_AddedOne"] = "\"{0}\" adicionado à biblioteca.";
        t["GH_FolderRequired"] = "Escolha uma pasta válida para varrer.";
        t["GH_RomsRequired"] = "Escolha a pasta onde estão as ROMs.";
        t["GH_ExtensionsRequired"] = "Informe pelo menos uma extensão de ROM (ex.: nes, sfc, iso).";
        t["GH_RememberFolder"] = "Continuar vigiando esta pasta nas próximas detecções";
        t["GH_ExistingEmulator"] = "Emulador já cadastrado";
        t["GH_NewEmulatorOption"] = "Novo emulador";

        // ---- Escolha de capa ----
        t["GH_CoverPickerTitle"] = "Escolher a capa";
        t["GH_CoverSearchHint"] = "Nome do jogo para procurar";
        t["GH_SearchingCovers"] = "Procurando capas...";
        t["GH_NoCoversFound"] = "Nenhuma capa encontrada com esse nome. Tente escrever o título como ele aparece na loja.";
        t["GH_ApplyingCover"] = "Aplicando a capa...";
        t["GH_CoverApplyFailed"] = "Não foi possível baixar esta capa. Tente outra.";
        t["GH_UseLocalImage"] = "Usar uma imagem do computador";
        t["GH_SearchAgain"] = "Procurar";
        t["GH_ApplyCover"] = "Usar esta capa";

        // ---- Configurações do GameHub ----
        t["Settings_GameHubSection"] = "GameHub";
        t["Settings_StartInGameHub"] = "Abrir o Pulse1x direto no GameHub";
        t["Settings_StartInGameHubHint"] = "O app inicia já na biblioteca, em tela cheia. Um botão discreto devolve você ao Pulse1x normal.";
        t["Settings_MaximizeGameHub"] = "Maximizar a janela ao entrar no GameHub";
        t["Settings_ImmersiveGameHub"] = "Ocultar a navegação lateral dentro do GameHub";
        t["Settings_SoundEnabled"] = "Sons de navegação";
        t["Settings_SoundVolume"] = "Volume dos sons";
        t["Settings_SoundReset"] = "Restaurar os sons padrão";
        t["Settings_SoundNavigate"] = "Som ao mudar de jogo";
        t["Settings_SoundConfirm"] = "Som ao confirmar";
        t["Settings_SoundBack"] = "Som ao voltar";
        t["Settings_SoundLaunch"] = "Som ao iniciar um jogo";
        t["Settings_SoundCustom"] = "Escolher .wav";
        t["Settings_SoundDefault"] = "Padrão";
        t["Settings_OnlineArt"] = "Procurar capas na internet pelo nome do jogo";
        t["Settings_OnlineArtHint"] = "Quando não houver capa no computador, o Pulse1x procura pelo nome no catálogo público da Steam. Desligue para trabalhar totalmente offline.";
        t["Settings_CardSize"] = "Tamanho das capas";
        t["Settings_CardSmall"] = "Pequeno";
        t["Settings_CardMedium"] = "Médio";
        t["Settings_CardLarge"] = "Grande";
        t["Settings_ShowTitles"] = "Mostrar o nome sobre as capas";
        t["Settings_ScanOnOpen"] = "Procurar jogos novos ao abrir o GameHub";

        // ---- Interface do GameHub ----
        t["GH_ExitHub"] = "Sair do GameHub";
        t["GH_AllGames"] = "Todos";
        t["GH_Recent"] = "Recentes";
        t["GH_Favorites"] = "Favoritos";
        t["GH_Settings"] = "Ajustes";
        t["GH_LibraryCount"] = "{0} de {1}";
        t["GH_NoResults"] = "Nada encontrado";
        t["GH_Details"] = "Detalhes";
        t["GH_Cover"] = "Capa";
    }

    internal static void AddGameHub2English()
    {
        var t = En;

        t["GH_AddTitle"] = "Add games";
        t["GH_AddSubtitle"] = "Choose where Pulse1x should bring games into your library from.";
        t["GH_AddStores"] = "Search installed stores";
        t["GH_AddStoresHint"] = "Automatically detects what is already installed through Steam, Epic Games, GOG, EA App and Ubisoft Connect.";
        t["GH_AddFolder"] = "Scan a folder";
        t["GH_AddFolderHint"] = "Looks for executables inside a folder on your computer — ideal for portable or manually installed games.";
        t["GH_AddFile"] = "Add a file or shortcut";
        t["GH_AddFileHint"] = "Point to an .exe, a shortcut or a specific command. Works for games and for applications too.";
        t["GH_AddEmulator"] = "Add an emulator and its ROMs";
        t["GH_AddEmulatorHint"] = "Register the emulator and the ROMs folder: each ROM becomes a normal library item, with cover and a Play button.";
        t["GH_AddShortcuts"] = "Import system shortcuts";
        t["GH_AddShortcutsHint"] = "Brings in Start Menu and Desktop shortcuts. It is the broadest source — and the one that needs the most cleanup afterwards.";
        t["GH_AddRun"] = "Search now";
        t["GH_AddApply"] = "Add";
        t["GH_AddedOne"] = "\"{0}\" added to the library.";
        t["GH_FolderRequired"] = "Choose a valid folder to scan.";
        t["GH_RomsRequired"] = "Choose the folder where the ROMs are.";
        t["GH_ExtensionsRequired"] = "Provide at least one ROM extension (e.g. nes, sfc, iso).";
        t["GH_RememberFolder"] = "Keep watching this folder on future scans";
        t["GH_ExistingEmulator"] = "Already registered emulator";
        t["GH_NewEmulatorOption"] = "New emulator";

        t["GH_CoverPickerTitle"] = "Choose the cover";
        t["GH_CoverSearchHint"] = "Game name to search for";
        t["GH_SearchingCovers"] = "Searching for covers...";
        t["GH_NoCoversFound"] = "No cover found with that name. Try writing the title as it appears in the store.";
        t["GH_ApplyingCover"] = "Applying the cover...";
        t["GH_CoverApplyFailed"] = "This cover could not be downloaded. Try another one.";
        t["GH_UseLocalImage"] = "Use an image from this computer";
        t["GH_SearchAgain"] = "Search";
        t["GH_ApplyCover"] = "Use this cover";

        t["Settings_GameHubSection"] = "GameHub";
        t["Settings_StartInGameHub"] = "Open Pulse1x straight into GameHub";
        t["Settings_StartInGameHubHint"] = "The app starts in the library, full window. A discreet button takes you back to regular Pulse1x.";
        t["Settings_MaximizeGameHub"] = "Maximize the window when entering GameHub";
        t["Settings_ImmersiveGameHub"] = "Hide the side navigation inside GameHub";
        t["Settings_SoundEnabled"] = "Navigation sounds";
        t["Settings_SoundVolume"] = "Sound volume";
        t["Settings_SoundReset"] = "Restore the default sounds";
        t["Settings_SoundNavigate"] = "Sound when moving between games";
        t["Settings_SoundConfirm"] = "Sound when confirming";
        t["Settings_SoundBack"] = "Sound when going back";
        t["Settings_SoundLaunch"] = "Sound when starting a game";
        t["Settings_SoundCustom"] = "Choose .wav";
        t["Settings_SoundDefault"] = "Default";
        t["Settings_OnlineArt"] = "Search the internet for covers by game name";
        t["Settings_OnlineArtHint"] = "When no cover exists on this computer, Pulse1x searches by name in Steam's public catalog. Turn it off to work fully offline.";
        t["Settings_CardSize"] = "Cover size";
        t["Settings_CardSmall"] = "Small";
        t["Settings_CardMedium"] = "Medium";
        t["Settings_CardLarge"] = "Large";
        t["Settings_ShowTitles"] = "Show the name over the covers";
        t["Settings_ScanOnOpen"] = "Look for new games when opening GameHub";

        t["GH_ExitHub"] = "Exit GameHub";
        t["GH_AllGames"] = "All";
        t["GH_Recent"] = "Recent";
        t["GH_Favorites"] = "Favorites";
        t["GH_Settings"] = "Settings";
        t["GH_LibraryCount"] = "{0} of {1}";
        t["GH_NoResults"] = "Nothing found";
        t["GH_Details"] = "Details";
        t["GH_Cover"] = "Cover";
    }
}
