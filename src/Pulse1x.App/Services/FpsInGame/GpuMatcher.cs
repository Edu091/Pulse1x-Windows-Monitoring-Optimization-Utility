namespace Pulse1x.App.Services.FpsInGame;

/// <summary>Resultado do reconhecimento: a GPU do catálogo e se é o modelo exato ou uma aproximação.</summary>
public sealed record GpuMatch(GpuSpec Gpu, bool IsApproximate);

/// <summary>
/// Encontra no catálogo a GPU que o Windows informa. Os nomes do WMI trazem marcas e sufixos
/// ("NVIDIA GeForce RTX 3060 Laptop GPU", "Intel(R) Iris(R) Xe Graphics") que o catálogo não usa,
/// então a comparação é feita por palavras normalizadas: os números do modelo precisam bater, o
/// fabricante também, e cada palavra a mais ou a menos (Ti, XT, Super, Laptop...) pesa contra.
/// </summary>
public static class GpuMatcher
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "r", "tm", "nvidia", "amd", "ati", "gpu", "graphics", "with", "corporation", "series", "the",
    };

    /// <summary>Adaptadores que não renderizam jogos (telas virtuais, acesso remoto, docks USB).</summary>
    private static readonly string[] VirtualAdapterHints =
    {
        "virtual", "remote", "basic display", "basic render", "mirage", "usb", "displaylink", "parsec",
        "citrix", "vmware", "hyper-v", "virtualbox", "spacedesk", "idd", "sharing", "meta ",
    };

    private enum Vendor { Unknown, Nvidia, Amd, Intel }

    /// <summary>Verdadeiro para adaptadores de vídeo virtuais, que nunca são a GPU de um jogo.</summary>
    public static bool IsVirtualAdapter(string name)
    {
        var lower = name.ToLowerInvariant();
        return VirtualAdapterHints.Any(lower.Contains);
    }

    /// <summary>Entradas do catálogo que são GPUs de verdade (tira "SMI USB Display", "Sharing Monitor"...).</summary>
    public static bool IsCatalogGpu(GpuSpec gpu) =>
        VendorOf(Tokens(gpu.Name)) != Vendor.Unknown && !IsVirtualAdapter(gpu.Name)
        && gpu.Name is not ("GeForce GPU" or "IncrediblE HD")
        // Submissões com lixo no nome ("Intel UHD 770#0x1c005d...#", "...manual-gen9_2015-134121").
        && !gpu.Name.Contains('#') && !gpu.Name.Contains("manual", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Escolhe, entre todos os adaptadores do PC, a GPU reconhecida mais forte — é nela que o jogo
    /// roda em notebooks híbridos e desktops com integrada ativa.
    /// </summary>
    public static GpuMatch? MatchBest(IReadOnlyList<GpuSpec> catalog, IEnumerable<string> adapterNames, string? cpuName, bool isLaptop)
    {
        return adapterNames
            .Where(n => !string.IsNullOrWhiteSpace(n) && !IsVirtualAdapter(n))
            .Select(n => Match(catalog, n, cpuName, isLaptop))
            .Where(m => m is not null)
            .OrderBy(m => m!.IsApproximate) // um modelo exato vale mais que um palpite mais alto
            .ThenByDescending(m => m!.Gpu.G3d)
            .FirstOrDefault();
    }

    public static GpuMatch? Match(IReadOnlyList<GpuSpec> catalog, string? gpuName, string? cpuName = null, bool isLaptop = false)
    {
        if (string.IsNullOrWhiteSpace(gpuName) || IsVirtualAdapter(gpuName)) return null;
        var query = Tokens(gpuName);
        var vendor = VendorOf(query);

        // Gráficos integrados da AMD aparecem só como "AMD Radeon(TM) Graphics"; o catálogo os
        // identifica pelo processador ("Ryzen 7 6800HS with Radeon Graphics").
        bool genericRadeon = vendor == Vendor.Amd && !query.Any(HasDigit);
        if (genericRadeon && !string.IsNullOrWhiteSpace(cpuName))
        {
            var byCpu = MatchAmdApu(catalog, cpuName);
            if (byCpu is not null) return byCpu;
        }

        // Notebooks com drivers antigos informam "GeForce GTX 1060" para a versão mobile, que rende
        // bem menos. Com bateria e sem indicação no nome, a variante de notebook vem primeiro.
        if (isLaptop && !query.Overlaps(new[] { "laptop", "mobile", "max", "q" }))
        {
            foreach (var suffix in new[] { "laptop", "mobile" })
            {
                var laptopQuery = new HashSet<string>(query) { suffix };
                var hit = MatchTokens(catalog, laptopQuery, vendor, out int penalty);
                if (hit is not null && penalty <= 1 && Tokens(hit.Name).Contains(suffix))
                    return new GpuMatch(hit, false);
            }
        }

        var exact = MatchTokens(catalog, query, vendor, out _);
        if (exact is not null) return new GpuMatch(exact, false);

        return ApproximateFamily(catalog, query, vendor);
    }

    /// <summary>Melhor candidato do mesmo fabricante que tenha todos os números do modelo.</summary>
    private static GpuSpec? MatchTokens(IReadOnlyList<GpuSpec> catalog, HashSet<string> query, Vendor vendor, out int bestPenalty)
    {
        bestPenalty = int.MaxValue;
        // O tamanho da memória ("6gb") só desempata: "GTX 1060 6GB" existe no catálogo apenas como
        // "GTX 1060" e "GTX 1060 3GB".
        var modelTokens = query.Where(t => HasDigit(t) && !IsMemorySize(t)).ToList();

        // Sem número de modelo só vale o nome idêntico ("Intel Iris Xe"); um genérico qualquer não
        // diz o desempenho.
        if (modelTokens.Count == 0)
        {
            var same = catalog.FirstOrDefault(g => IsCatalogGpu(g) && Tokens(g.Name).SetEquals(query));
            if (same is not null) bestPenalty = 0;
            return same;
        }

        GpuSpec? best = null;
        foreach (var gpu in catalog)
        {
            var candidate = Tokens(gpu.Name);
            if (!modelTokens.All(candidate.Contains)) continue;
            if (vendor != Vendor.Unknown && VendorOf(candidate) != vendor) continue;
            // Mesmo número em linhas diferentes é outro chip: HD 540 ≠ Iris 540, RX 580X ≠ Pro 580X.
            if (!FamilyOf(candidate).SetEquals(FamilyOf(query))) continue;
            if (!IsCatalogGpu(gpu)) continue;

            int penalty = 0;
            foreach (var t in candidate)
                if (!query.Contains(t)) penalty += HasDigit(t) ? 2 : 3;
            foreach (var t in query)
                if (!candidate.Contains(t)) penalty += 1;

            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = gpu;
            }
        }
        return best;
    }

    /// <summary>
    /// iGPU da AMD pelo processador. Sem o modelo exato, usa a mediana dos irmãos da mesma geração
    /// e linha (ex.: outros Ryzen 5000 "H"), que têm a mesma integrada com clocks parecidos.
    /// </summary>
    private static GpuMatch? MatchAmdApu(IReadOnlyList<GpuSpec> catalog, string cpuName)
    {
        var apus = catalog.Where(g => g.Name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
                                      && g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)).ToList();
        var cpu = Tokens(cpuName);
        var exact = MatchTokens(apus, cpu, Vendor.Amd, out _);
        if (exact is not null) return new GpuMatch(exact, false);

        // "5800h" -> geração '5', sufixo "h".
        var model = cpu.FirstOrDefault(t => t.Length >= 4 && char.IsDigit(t[0]) && t.Count(char.IsDigit) >= 4);
        if (model is null) return null;
        string suffix = new string(model.SkipWhile(char.IsDigit).ToArray());
        var siblings = apus.Where(g => Tokens(g.Name).Any(t =>
                t.Length >= 4 && t[0] == model[0] && t.Count(char.IsDigit) >= 4
                && new string(t.SkipWhile(char.IsDigit).ToArray()).StartsWith(suffix.Length > 0 ? suffix[..1] : "", StringComparison.Ordinal)))
            .OrderBy(g => g.G3d)
            .ToList();
        return siblings.Count == 0 ? null : new GpuMatch(siblings[siblings.Count / 2], true);
    }

    /// <summary>
    /// Integradas antigas da Intel que a base não lista (HD 4000, HD 520...) recebem o valor da
    /// família, marcado como aproximado — melhor que deixar o usuário sem resposta.
    /// </summary>
    private static GpuMatch? ApproximateFamily(IReadOnlyList<GpuSpec> catalog, HashSet<string> query, Vendor vendor)
    {
        if (vendor != Vendor.Intel) return null;
        string? familyToken = new[] { "uhd", "hd", "iris", "arc" }.FirstOrDefault(query.Contains);
        if (familyToken is null) return null;

        // Mesmo nome de família com o número de modelo mais próximo (HD 540 -> HD 530).
        var number = query.Select(t => int.TryParse(t, out int n) ? n : (int?)null).FirstOrDefault(n => n is not null);
        if (number is not null)
        {
            var nearest = catalog
                .Where(g => IsCatalogGpu(g) && Tokens(g.Name) is var t && t.Contains(familyToken) && t.Contains("intel")
                            && (familyToken != "hd" || !t.Contains("uhd")))
                .Select(g => (Gpu: g, N: Tokens(g.Name).Select(t => int.TryParse(t, out int n) ? n : (int?)null).FirstOrDefault(n => n is not null)))
                .Where(x => x.N is not null && x.N.ToString()!.Length == number.ToString()!.Length)
                .OrderBy(x => Math.Abs(x.N!.Value - number.Value))
                .ThenByDescending(x => x.Gpu.G3d)
                .Select(x => x.Gpu)
                .FirstOrDefault();
            if (nearest is not null) return new GpuMatch(nearest, true);
        }

        string family = familyToken switch
        {
            "uhd" => "Intel UHD Graphics",
            "hd" => "Intel HD Family",
            "iris" => "Intel Iris Plus",
            _ => "Intel Arc",
        };
        var gpu = catalog.FirstOrDefault(g => g.Name == family);
        return gpu is null ? null : new GpuMatch(gpu, true);
    }

    private static readonly HashSet<string> FamilyTokens = new(StringComparer.Ordinal)
    {
        "hd", "uhd", "iris", "arc", "xe", "rtx", "gtx", "gt", "gts", "mx", "rx", "r5", "r7", "r9", "pro", "quadro", "titan", "firepro",
    };

    private static HashSet<string> FamilyOf(HashSet<string> tokens) =>
        tokens.Where(FamilyTokens.Contains).ToHashSet(StringComparer.Ordinal);

    private static Vendor VendorOf(HashSet<string> tokens)
    {
        if (tokens.Overlaps(new[] { "geforce", "quadro", "rtx", "gtx", "titan", "tesla" })) return Vendor.Nvidia;
        if (tokens.Overlaps(new[] { "radeon", "ryzen", "vega", "firepro", "instinct" })) return Vendor.Amd;
        if (tokens.Overlaps(new[] { "intel", "arc", "iris", "uhd" })) return Vendor.Intel;
        return Vendor.Unknown;
    }

    internal static HashSet<string> Tokens(string name)
    {
        // "RadeonT" é como o símbolo ™ ficou gravado em parte do catálogo.
        var normalized = TextNormalizer.Normalize(name.Replace("RadeonT ", "Radeon ", StringComparison.Ordinal));
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Noise.Contains(t))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasDigit(string token) => token.Any(char.IsDigit);

    private static bool IsMemorySize(string token) =>
        token.Length > 2 && token.EndsWith("gb", StringComparison.Ordinal) && token[..^2].All(char.IsDigit);
}
