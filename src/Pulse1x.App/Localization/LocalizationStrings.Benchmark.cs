namespace Pulse1x.App.Localization;

/// <summary>Textos da categoria Benchmark.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddBenchmarkPortuguese()
    {
        var t = Pt;
        t["Nav_Benchmark"] = "Benchmark";
        t["Bench_PageTitle"] = "Benchmark";
        t["Bench_PageSubtitle"] = "Meça o desempenho do processador, da memória RAM e da placa de vídeo, e compare antes e depois de otimizar.";
        t["Bench_Overall"] = "RESULTADO GERAL";
        t["Bench_Points"] = "pontos";
        t["Bench_RunAll"] = "Executar benchmark completo";
        t["Bench_Run"] = "Executar";
        t["Bench_Cancel"] = "Cancelar";
        t["Bench_Duration"] = "CPU, RAM e GPU · cerca de 35 segundos";
        t["Bench_DontTouch"] = "Deixe o Pulse1x na frente e evite usar o PC durante o teste: o Windows dá menos desempenho a janelas em segundo plano, e outros programas abertos reduzem o resultado.";
        t["Bench_Running"] = "Testando {0} ({1} de {2})...";
        t["Bench_Done"] = "Benchmark concluído e salvo no histórico.";
        t["Bench_Canceled"] = "Benchmark cancelado.";
        t["Bench_Failed"] = "O benchmark falhou: {0}";
        t["Bench_OverallNeedsAll"] = "O resultado geral aparece depois de um benchmark completo (as quatro áreas na mesma execução).";
        t["Bench_BatteryWarning"] = "O notebook está na bateria: o Windows limita CPU e GPU, então o resultado sai menor. Conecte o carregador para medir o desempenho máximo.";
        t["Bench_NotRunYet"] = "Ainda não executado";
        t["Bench_Unavailable"] = "Não foi possível executar: {0}";
        t["Bench_ThreadsUsed"] = "{0} threads em paralelo";
        t["Bench_DeltaUp"] = "▲ {0}% vs anterior";
        t["Bench_DeltaDown"] = "▼ {0}% vs anterior";
        t["Bench_DeltaSame"] = "≈ igual ({0}%)";
        t["Bench_History"] = "Histórico";
        t["Bench_ClearHistory"] = "Limpar histórico";
        t["Bench_ColDate"] = "Data";
        t["Bench_ColOverall"] = "Geral";
        t["Bench_ColSingle"] = "CPU 1T";
        t["Bench_ColMulti"] = "CPU MT";
        t["Bench_Reference"] = "1000 pontos = máquina de referência (Core i5-13420H, DDR5-5200, GeForce RTX 3050 6GB Laptop, no carregador). 2000 pontos = o dobro do desempenho. O resultado geral é a média geométrica das quatro áreas. Variações de até 3% entre execuções são normais.";

        t["Bench_TitleDisk"] = "Disco (SSD / HD)";
        t["Bench_DescDisk"] = "Leitura e escrita sequenciais e 4K aleatório: boot, carregamento de jogos e instalações.";
        t["Bench_DiskNote"] = "Grava um arquivo temporário de 1 GB (apagado ao final). Discos variam mais entre execuções que CPU e RAM. Não entra no resultado geral.";
        t["Bench_DiskPick"] = "Escolha um disco no cartão Disco antes de testar.";
        t["Bench_DiskNoSpace"] = "Espaço livre insuficiente (precisa de pelo menos 1 GB livre).";
        t["Bench_DiskNoAccess"] = "Não foi possível criar o arquivo de teste neste disco";
        t["Bench_DiskSeqRead"] = "Leitura sequencial";
        t["Bench_DiskSeqWrite"] = "Escrita sequencial";
        t["Bench_DiskRnd32Read"] = "Leitura 4K (fila 32)";
        t["Bench_DiskRnd32Write"] = "Escrita 4K (fila 32)";
        t["Bench_DiskRnd1Read"] = "Leitura 4K (fila 1)";
        t["Bench_ColDisk"] = "Disco";
        t["Bench_Stress"] = "Stress";
        t["Bench_StressStop"] = "Parar";
        t["Bench_StressTip"] = "Carga contínua com a pontuação atualizada a cada segundo (como o stress do CPU-Z): mostra se o desempenho cai quando a peça esquenta. No disco, o stress só lê, para não gastar o SSD.";
        t["Bench_StressRunning"] = "Stress de {0} rodando — clique em Parar para encerrar.";
        t["Bench_StressStopped"] = "Stress de {0} encerrado: {1}";
        t["Bench_StressWarming"] = "Medindo...";
        t["Bench_StressStats"] = "média {0} · mín {1} · máx {2} · {3}";
        t["Bench_Usage"] = "uso";
        t["Bench_Live"] = "AO VIVO";
        t["Bench_TitleCpuSingle"] = "CPU — Single-thread";
        t["Bench_DescCpuSingle"] = "Um núcleo só: o que mais pesa no FPS da maioria dos jogos e na fluidez do Windows.";
        t["Bench_TitleCpuMulti"] = "CPU — Multi-thread";
        t["Bench_DescCpuMulti"] = "Todos os núcleos juntos: jogos modernos, streaming, compilação e renderização.";
        t["Bench_TitleMemory"] = "Memória RAM";
        t["Bench_DescMemory"] = "Banda e latência: carregamentos, mundos abertos e FPS mínimo (1% low).";
        t["Bench_TitleGpu"] = "Placa de vídeo (GPU)";
        t["Bench_DescGpu"] = "Poder de cálculo e banda da memória de vídeo, pelo Direct3D 11.";

        t["Bench_CpuInteger"] = "Inteiros";
        t["Bench_CpuFloat"] = "Ponto flutuante";
        t["Bench_CpuSimd"] = "Vetorial (SIMD)";
        t["Bench_CpuSort"] = "Ordenação";
        t["Bench_CpuCompress"] = "Compressão";
        t["Bench_Threads"] = "Threads";
        t["Bench_MemRead"] = "Leitura";
        t["Bench_MemWrite"] = "Escrita";
        t["Bench_MemCopy"] = "Cópia";
        t["Bench_MemLatency"] = "Latência";
        t["Bench_GpuCompute"] = "Cálculo FP32";
        t["Bench_GpuBandwidth"] = "Banda de memória";
    }

    internal static void AddBenchmarkEnglish()
    {
        var t = En;
        t["Nav_Benchmark"] = "Benchmark";
        t["Bench_PageTitle"] = "Benchmark";
        t["Bench_PageSubtitle"] = "Measure processor, RAM and graphics card performance, and compare before and after optimizing.";
        t["Bench_Overall"] = "OVERALL SCORE";
        t["Bench_Points"] = "points";
        t["Bench_RunAll"] = "Run full benchmark";
        t["Bench_Run"] = "Run";
        t["Bench_Cancel"] = "Cancel";
        t["Bench_Duration"] = "CPU, RAM and GPU · about 35 seconds";
        t["Bench_DontTouch"] = "Keep Pulse1x in front and avoid using the PC during the test: Windows gives less performance to background windows, and other open programs lower the result.";
        t["Bench_Running"] = "Testing {0} ({1} of {2})...";
        t["Bench_Done"] = "Benchmark finished and saved to history.";
        t["Bench_Canceled"] = "Benchmark canceled.";
        t["Bench_Failed"] = "The benchmark failed: {0}";
        t["Bench_OverallNeedsAll"] = "The overall score appears after a full benchmark (all four areas in the same run).";
        t["Bench_BatteryWarning"] = "The laptop is on battery: Windows limits CPU and GPU, so the score comes out lower. Plug in the charger to measure peak performance.";
        t["Bench_NotRunYet"] = "Not run yet";
        t["Bench_Unavailable"] = "Couldn't run: {0}";
        t["Bench_ThreadsUsed"] = "{0} threads in parallel";
        t["Bench_DeltaUp"] = "▲ {0}% vs previous";
        t["Bench_DeltaDown"] = "▼ {0}% vs previous";
        t["Bench_DeltaSame"] = "≈ same ({0}%)";
        t["Bench_History"] = "History";
        t["Bench_ClearHistory"] = "Clear history";
        t["Bench_ColDate"] = "Date";
        t["Bench_ColOverall"] = "Overall";
        t["Bench_ColSingle"] = "CPU 1T";
        t["Bench_ColMulti"] = "CPU MT";
        t["Bench_Reference"] = "1000 points = reference machine (Core i5-13420H, DDR5-5200, GeForce RTX 3050 6GB Laptop, plugged in). 2000 points = twice the performance. The overall score is the geometric mean of the four areas. Up to 3% variation between runs is normal.";

        t["Bench_TitleDisk"] = "Disk (SSD / HDD)";
        t["Bench_DescDisk"] = "Sequential and 4K random read and write: boot, game loading and installs.";
        t["Bench_DiskNote"] = "Writes a 1 GB temporary file (deleted at the end). Disks vary more between runs than CPU and RAM. Not part of the overall score.";
        t["Bench_DiskPick"] = "Pick a disk on the Disk card before testing.";
        t["Bench_DiskNoSpace"] = "Not enough free space (needs at least 1 GB free).";
        t["Bench_DiskNoAccess"] = "Couldn't create the test file on this disk";
        t["Bench_DiskSeqRead"] = "Sequential read";
        t["Bench_DiskSeqWrite"] = "Sequential write";
        t["Bench_DiskRnd32Read"] = "4K read (queue 32)";
        t["Bench_DiskRnd32Write"] = "4K write (queue 32)";
        t["Bench_DiskRnd1Read"] = "4K read (queue 1)";
        t["Bench_ColDisk"] = "Disk";
        t["Bench_Stress"] = "Stress";
        t["Bench_StressStop"] = "Stop";
        t["Bench_StressTip"] = "Continuous load with the score updated every second (like CPU-Z's stress): shows whether performance drops as the part heats up. On disks the stress only reads, so it doesn't wear the SSD.";
        t["Bench_StressRunning"] = "{0} stress running — click Stop to end it.";
        t["Bench_StressStopped"] = "{0} stress ended: {1}";
        t["Bench_StressWarming"] = "Measuring...";
        t["Bench_StressStats"] = "average {0} · min {1} · max {2} · {3}";
        t["Bench_Usage"] = "usage";
        t["Bench_Live"] = "LIVE";
        t["Bench_TitleCpuSingle"] = "CPU — Single-thread";
        t["Bench_DescCpuSingle"] = "One core only: what matters most for FPS in most games and for Windows responsiveness.";
        t["Bench_TitleCpuMulti"] = "CPU — Multi-thread";
        t["Bench_DescCpuMulti"] = "All cores together: modern games, streaming, compiling and rendering.";
        t["Bench_TitleMemory"] = "RAM";
        t["Bench_DescMemory"] = "Bandwidth and latency: loading, open worlds and minimum FPS (1% low).";
        t["Bench_TitleGpu"] = "Graphics card (GPU)";
        t["Bench_DescGpu"] = "Compute power and video memory bandwidth, through Direct3D 11.";

        t["Bench_CpuInteger"] = "Integer";
        t["Bench_CpuFloat"] = "Floating point";
        t["Bench_CpuSimd"] = "Vector (SIMD)";
        t["Bench_CpuSort"] = "Sorting";
        t["Bench_CpuCompress"] = "Compression";
        t["Bench_Threads"] = "Threads";
        t["Bench_MemRead"] = "Read";
        t["Bench_MemWrite"] = "Write";
        t["Bench_MemCopy"] = "Copy";
        t["Bench_MemLatency"] = "Latency";
        t["Bench_GpuCompute"] = "FP32 compute";
        t["Bench_GpuBandwidth"] = "Memory bandwidth";
    }
}
