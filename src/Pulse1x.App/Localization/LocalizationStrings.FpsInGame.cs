namespace Pulse1x.App.Localization;

/// <summary>Textos da categoria Fps-In-Game (estimativa de FPS por jogo).</summary>
internal static partial class LocalizationStrings
{
    internal static void AddFpsInGamePortuguese()
    {
        var t = Pt;
        t["Nav_FpsInGame"] = "Fps-In-Game";
        t["Fps_Title"] = "Fps-In-Game";
        t["Fps_Subtitle"] = "Descubra se um jogo roda no seu PC e com qual FPS médio, em mais de 16 mil títulos.";
        t["Fps_YourGpu"] = "PLACA DE VÍDEO";
        t["Fps_YourCpu"] = "PROCESSADOR";
        t["Fps_YourRam"] = "MEMÓRIA RAM";
        t["Fps_Resolution"] = "RESOLUÇÃO";
        t["Fps_GpuScore"] = "Score G3D: {0}";
        t["Fps_UseDetected"] = "Usar a detectada";
        t["Fps_GpuNotRecognized"] = "Não reconhecemos sua placa automaticamente — escolha-a na lista acima. Detectada:";
        t["Fps_SearchPlaceholder"] = "Buscar jogo (ex.: gta 5, cyberpunk, rdr2, bg3)";
        t["Fps_FilterPopular"] = "Mais populares";
        t["Fps_FilterLibrary"] = "Na minha biblioteca";
        t["Fps_FilterAll"] = "Todos os jogos";
        t["Fps_Loading"] = "Carregando a base de jogos...";
        t["Fps_LoadError"] = "Não foi possível carregar a base de jogos: {0}";
        t["Fps_ShowingOf"] = "Mostrando {0} de {1} jogos — refine a busca para ver outros";
        t["Fps_Showing"] = "{0} jogos";
        t["Fps_PopularSummary"] = "Em {3}, {0} dos {2} jogos mais populares rodam a 60+ FPS e {1} são jogáveis.";
        t["Fps_NoResults"] = "Nenhum jogo encontrado. Na \"Minha biblioteca\" aparecem os jogos do GameHub que existem na base.";
        t["Fps_InLibrary"] = "Na sua biblioteca do GameHub";
        t["Fps_EstimateHeader"] = "FPS MÉDIO ESTIMADO NA RESOLUÇÃO ESCOLHIDA";
        t["Fps_BestPresetFor60"] = "Sugestão: gráficos no {0} para manter 60 FPS em {1}.";
        t["Fps_BestPresetAtCap"] = "Sugestão: gráficos no {0} — o jogo já roda no limite dele ({1} FPS), como foi feito para rodar.";
        t["Fps_NoPresetFor60"] = "Nem no Baixo este PC chega a 60 FPS nesta resolução — tente uma resolução menor ou upscaling (DLSS/FSR).";
        t["Fps_WarnMinGpu"] = "Sua placa de vídeo está abaixo da exigida no mínimo oficial: o jogo pode não abrir ou apresentar falhas.";
        t["Fps_WarnMinRam"] = "O mínimo pede {0} GB de RAM e você tem {1}: espere travamentos.";
        t["Fps_WarnRecRam"] = "O recomendado pede {0} GB de RAM (você tem {1}): pode haver engasgos em áreas grandes.";
        t["Fps_WarnCap"] = "O jogo é limitado a {0} FPS pelo próprio motor.";
        t["Fps_WarnVram"] = "O mínimo pede {0} GB de memória de vídeo e sua placa tem {1} GB: use texturas no Baixo.";
        t["Fps_WarnApproxGpu"] = "O modelo exato da sua placa não está na base; a estimativa usa o modelo mais parecido. Se souber qual é, escolha-o na lista.";
        t["Fps_WarnCpuBound"] = "Acima de ~150 FPS quem costuma limitar é o processador: o FPS real pode ficar abaixo do estimado.";
        t["Fps_WarnFewThreads"] = "Seu processador tem só {0} threads: muitos jogos atuais pedem pelo menos 4 e podem travar ou nem abrir.";
        t["Fps_ListHint"] = "Cada jogo mostra o melhor preset para manter 60 FPS; clique para ver todos.";
        t["Fps_GpuApprox"] = "Aproximado: modelo exato não está na base";
        t["Fps_GpuVsRequired"] = "Sua GPU: {0}% do desempenho desta";
        t["Fps_GpuWayAbove"] = "Sua GPU está bem acima desta (3× ou mais)";
        t["Fps_MinRequirements"] = "Requisitos mínimos (Steam)";
        t["Fps_RecRequirements"] = "Requisitos recomendados (Steam)";
        t["Fps_OpenDetails"] = "Ver tabela completa";
        t["Fps_CappedAt"] = "{0} FPS (limite)";
        t["Fps_PresetLow"] = "Baixo";
        t["Fps_PresetMedium"] = "Médio";
        t["Fps_PresetHigh"] = "Alto";
        t["Fps_PresetUltra"] = "Ultra";
        t["Fps_VerdictCannotRun"] = "Não roda";
        t["Fps_VerdictStruggles"] = "Roda com dificuldade";
        t["Fps_VerdictPlayable"] = "Jogável";
        t["Fps_VerdictSmooth"] = "Roda bem";
        t["Fps_VerdictVerySmooth"] = "Roda liso";
        t["Fps_Attribution"] = "Estimativas de FPS médio (sem ray tracing, DLSS/FSR ou geração de quadros), não medições — erro típico de ~30%. Dados de requisitos e FPS: {0}, ";
    }

    internal static void AddFpsInGameEnglish()
    {
        var t = En;
        t["Nav_FpsInGame"] = "Fps-In-Game";
        t["Fps_Title"] = "Fps-In-Game";
        t["Fps_Subtitle"] = "Find out whether a game runs on your PC and at what average FPS, across 16,000+ titles.";
        t["Fps_YourGpu"] = "GRAPHICS CARD";
        t["Fps_YourCpu"] = "PROCESSOR";
        t["Fps_YourRam"] = "RAM";
        t["Fps_Resolution"] = "RESOLUTION";
        t["Fps_GpuScore"] = "G3D score: {0}";
        t["Fps_UseDetected"] = "Use detected";
        t["Fps_GpuNotRecognized"] = "We couldn't recognize your graphics card automatically — pick it from the list above. Detected:";
        t["Fps_SearchPlaceholder"] = "Search game (e.g. gta 5, cyberpunk, rdr2, bg3)";
        t["Fps_FilterPopular"] = "Most popular";
        t["Fps_FilterLibrary"] = "In my library";
        t["Fps_FilterAll"] = "All games";
        t["Fps_Loading"] = "Loading the game database...";
        t["Fps_LoadError"] = "Couldn't load the game database: {0}";
        t["Fps_ShowingOf"] = "Showing {0} of {1} games — refine the search to see others";
        t["Fps_Showing"] = "{0} games";
        t["Fps_PopularSummary"] = "At {3}, {0} of the {2} most popular games run at 60+ FPS and {1} are playable.";
        t["Fps_NoResults"] = "No games found. \"In my library\" lists the GameHub games that exist in the database.";
        t["Fps_InLibrary"] = "In your GameHub library";
        t["Fps_EstimateHeader"] = "ESTIMATED AVERAGE FPS AT THE CHOSEN RESOLUTION";
        t["Fps_BestPresetFor60"] = "Suggestion: {0} graphics to hold 60 FPS at {1}.";
        t["Fps_BestPresetAtCap"] = "Suggestion: {0} graphics — the game already runs at its own cap ({1} FPS), as it was made to.";
        t["Fps_NoPresetFor60"] = "This PC doesn't reach 60 FPS at this resolution even on Low — try a lower resolution or upscaling (DLSS/FSR).";
        t["Fps_WarnMinGpu"] = "Your graphics card is below the official minimum: the game may not start or may glitch.";
        t["Fps_WarnMinRam"] = "The minimum asks for {0} GB of RAM and you have {1}: expect stutters.";
        t["Fps_WarnRecRam"] = "The recommended spec asks for {0} GB of RAM (you have {1}): large areas may stutter.";
        t["Fps_WarnCap"] = "The game's engine caps it at {0} FPS.";
        t["Fps_WarnVram"] = "The minimum asks for {0} GB of video memory and your card has {1} GB: keep textures on Low.";
        t["Fps_WarnApproxGpu"] = "Your card's exact model isn't in the database; the estimate uses the closest model. If you know it, pick it from the list.";
        t["Fps_WarnCpuBound"] = "Above ~150 FPS the processor is usually the limit: real FPS may land below the estimate.";
        t["Fps_WarnFewThreads"] = "Your processor has only {0} threads: many current games ask for at least 4 and may stutter or not start.";
        t["Fps_ListHint"] = "Each game shows the best preset to hold 60 FPS; click to see them all.";
        t["Fps_GpuApprox"] = "Approximate: exact model not in the database";
        t["Fps_GpuVsRequired"] = "Your GPU: {0}% of this one's performance";
        t["Fps_GpuWayAbove"] = "Your GPU is well above this one (3× or more)";
        t["Fps_MinRequirements"] = "Minimum requirements (Steam)";
        t["Fps_RecRequirements"] = "Recommended requirements (Steam)";
        t["Fps_OpenDetails"] = "See full table";
        t["Fps_CappedAt"] = "{0} FPS (cap)";
        t["Fps_PresetLow"] = "Low";
        t["Fps_PresetMedium"] = "Medium";
        t["Fps_PresetHigh"] = "High";
        t["Fps_PresetUltra"] = "Ultra";
        t["Fps_VerdictCannotRun"] = "Can't run";
        t["Fps_VerdictStruggles"] = "Struggles";
        t["Fps_VerdictPlayable"] = "Playable";
        t["Fps_VerdictSmooth"] = "Runs well";
        t["Fps_VerdictVerySmooth"] = "Runs smooth";
        t["Fps_Attribution"] = "Average FPS estimates (no ray tracing, DLSS/FSR or frame generation), not measurements — typical error ~30%. Requirements and FPS data: {0}, ";
    }
}
