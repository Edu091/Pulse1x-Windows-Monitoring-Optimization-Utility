using System.IO;
using Pulse1x.App.Services;

namespace Pulse1x.Hub.Tests;

/// <summary>Categoria Latência: leitura do netsh (PT/EN), canais, bufferbloat, DNS, driver da placa e textos.</summary>
internal static class NetworkTests
{
    // Saídas reais do netsh num Windows em português (MediaTek MT7921), 2026-11-08.
    private const string InterfacesPt = """
        Há 1 interface no sistema:

            Nome                   : Wi-Fi
            Descrição            : MediaTek Wi-Fi 6 MT7921 Wireless LAN Card
            GUID                   : a69f9ab9-d734-444f-9c27-6329548a16eb
            Endereço físico       : 24:b2:b9:0f:70:d5
            Estado                  : Conectado
            SSID                   : X-PC_DUDU_5G
            AP BSSID               : e8:81:75:14:4b:15
            Banda                   : 5 GHz
            Canal                : 64
            Tipo de rede           : Infraestrutura
            Tipo de rádio             : 802.11ac
            Taxa de recepção (Mbps)    : 866.7
            Taxa de transmissão (Mbps)   : 866.7
            Sinal                 : 80%
            Rssi                   : -52
            Perfil                : X-PC_DUDU_5G
        """;

    private const string NetworksPt = """
        Nome da interface: Wi-Fi
        Há 3 redes visíveis no momento.

        SSID 1 : X-PC_DUDU_5G
            Tipo de rede            : Infraestrutura
            Autenticação            : WPA2-Personal
            BSSID 1                 : e8:81:75:14:4b:15
                 Sinal             : 87%
                 Tipo de Rádio         : 802.11ac
                 Banda               : 5 GHz
                 Canal            : 64
                 Carga Bss:
                     Estações Conectadas:         4
                     Utilização do canal:        12 (5 %)
                 Taxas básicas (Mbps): 6 12 24
            BSSID 2                 : e8:81:75:14:4b:14
                 Sinal             : 90%
                 Banda               : 2,4 GHz
                 Canal            : 6

        SSID 2 : Vizinho
            Tipo de rede            : Infraestrutura
            BSSID 1                 : 11:22:33:44:55:66
                 Sinal             : 70%
                 Banda               : 2,4 GHz
                 Canal            : 4

        SSID 3 :
            BSSID 1                 : aa:bb:cc:dd:ee:ff
                 Sinal             : 20%
                 Canal            : 11
        """;

    public static void Register(TestSuite suite)
    {
        suite.Run("netsh interfaces (PT) yields SSID, BSSID, channel, signal and link rate", () =>
        {
            var w = NetworkLatencyService.ParseWifiInterfaces(InterfacesPt);
            TestSuite.Equal(true, w.Connected);
            TestSuite.Equal("X-PC_DUDU_5G", w.Ssid);
            TestSuite.Equal("e8:81:75:14:4b:15", w.Bssid);
            TestSuite.Equal("Wi-Fi", w.InterfaceName);
            TestSuite.Equal("a69f9ab9-d734-444f-9c27-6329548a16eb", w.InterfaceGuid);
            TestSuite.Equal(64, w.Channel);
            TestSuite.Equal(80, w.SignalPercent);
            TestSuite.Equal(-52, w.SignalDbm);
            TestSuite.Equal(866.7, w.LinkMbps);
            TestSuite.Equal("802.11ac", w.RadioType);
            TestSuite.Equal(false, w.LocationBlocked);
        });

        suite.Run("netsh location-permission refusal is detected instead of showing blanks", () =>
        {
            const string en = "Network shell commands need location permission to access WLAN information. Turn on Location services on the Location page in Privacy & security settings.\r\n\r\nHere is the URI for the Location page in the Settings app:\r\nms-settings:privacy-location";
            var w = NetworkLatencyService.ParseWifiInterfaces(en);
            TestSuite.Equal(true, w.LocationBlocked);
            TestSuite.Equal(false, w.Connected);
        });

        suite.Run("netsh networks (PT) parses one entry per BSSID without confusing channel usage", () =>
        {
            var list = NetworkLatencyService.ParseWifiNetworks(NetworksPt);
            TestSuite.Equal(4, list.Count);
            var own = list[0];
            TestSuite.Equal("X-PC_DUDU_5G", own.Ssid);
            TestSuite.Equal(64, own.Channel);
            TestSuite.Equal(87, own.SignalPercent);
            TestSuite.Equal("5 GHz", own.Band);
            TestSuite.Equal("802.11ac", own.Radio);
            TestSuite.Equal(4, own.Stations ?? -1);
            TestSuite.Equal(5, own.UtilizationPercent ?? -1);
            TestSuite.Equal(6, list[1].Channel);
            TestSuite.Equal("", list[3].Ssid);
            TestSuite.Equal("2,4 GHz", list[3].Band); // inferida pelo canal 11
        });

        suite.Run("Channel advice ignores the user's own router and counts 2.4 GHz overlap", () =>
        {
            var list = NetworkLatencyService.ParseWifiNetworks(NetworksPt);
            // No canal 6 (2,4 GHz): o vizinho no canal 4 sobrepõe; o próprio roteador (mesmo prefixo de MAC) não conta.
            var a = NetworkLatencyService.BuildChannelAdvice(list, 6, "e8:81:75:14:4b:14")!;
            TestSuite.Equal(1, a.NeighborsOnCurrent);
            TestSuite.Equal(false, a.Congested);
            // O vizinho forte do canal 4 sobrepõe 1 e 6; no 11 só há uma rede oculta fraca.
            TestSuite.Equal(11, a.SuggestedChannel24);
            TestSuite.Equal(36, a.SuggestedChannel5);
            TestSuite.Equal(true, NetworkLatencyService.Interferes(1, 4));
            TestSuite.Equal(false, NetworkLatencyService.Interferes(1, 6));
            TestSuite.Equal(false, NetworkLatencyService.Interferes(36, 40));
            TestSuite.Equal(false, NetworkLatencyService.Interferes(11, 36));
        });

        suite.Run("Hidden/guest networks of the same router are not counted as neighbours", () =>
        {
            // Visto ao vivo: a rede oculta de mesh do roteador usa o MAC base com o bit "local" ligado.
            TestSuite.Equal(true, NetworkLatencyService.SameAccessPoint("e8:81:75:14:4b:15", "ea:81:75:44:4b:15"));
            TestSuite.Equal(true, NetworkLatencyService.SameAccessPoint("e8:81:75:14:4b:15", "e8:81:75:14:4b:14"));
            TestSuite.Equal(false, NetworkLatencyService.SameAccessPoint("e8:81:75:14:4b:15", "e8:45:8b:dd:d0:a0"));
            TestSuite.Equal(false, NetworkLatencyService.SameAccessPoint("", "e8:81:75:14:4b:15"));
        });

        suite.Run("Bufferbloat grade follows the latency increase under load", () =>
        {
            TestSuite.Equal("A+", NetworkLatencyService.BufferbloatGrade(2).Grade);
            TestSuite.Equal("A", NetworkLatencyService.BufferbloatGrade(29).Grade);
            TestSuite.Equal("B", NetworkLatencyService.BufferbloatGrade(30).Grade);
            TestSuite.Equal("C", NetworkLatencyService.BufferbloatGrade(150).Grade);
            TestSuite.Equal("D", NetworkLatencyService.BufferbloatGrade(399).Grade);
            TestSuite.Equal("F", NetworkLatencyService.BufferbloatGrade(1000).Grade);
        });

        suite.Run("DNS query packet is a valid RFC 1035 A query and responses match by id", () =>
        {
            var q = NetworkLatencyService.BuildDnsQuery(0xBEEF, "google.com");
            TestSuite.SequenceEqual(new byte[] { 0xBE, 0xEF, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0,
                6, (byte)'g', (byte)'o', (byte)'o', (byte)'g', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0,
                0x00, 0x01, 0x00, 0x01 }, q);
            var resp = (byte[])q.Clone();
            resp[2] |= 0x80; // QR = resposta
            TestSuite.Equal(true, NetworkLatencyService.IsDnsResponseFor(resp, 0xBEEF));
            TestSuite.Equal(false, NetworkLatencyService.IsDnsResponseFor(q, 0xBEEF));      // é a própria consulta
            TestSuite.Equal(false, NetworkLatencyService.IsDnsResponseFor(resp, 0xBEEE));   // outro id
        });

        suite.Run("Driver latency uses the PC's own baseline, not the 1 ms ideal", () =>
        {
            // Regressão: Sleep(1) sem timeBeginPeriod dura ~15,6 ms e todo PC saía "latência elevada".
            var steady = Enumerable.Repeat(1.95, 3000).ToList();
            var r = NetworkLatencyService.SummarizeDriverSamples(steady);
            TestSuite.Equal(QualityLevel.Excellent, r.Level);
            // Um soluço isolado não decide o resultado; picos repetidos sim.
            var oneSpike = steady.Append(25.0).ToList();
            TestSuite.Equal(QualityLevel.Excellent, NetworkLatencyService.SummarizeDriverSamples(oneSpike).Level);
            var spiky = steady.Concat(Enumerable.Repeat(12.0, 10)).ToList();
            TestSuite.Equal(QualityLevel.Poor, NetworkLatencyService.SummarizeDriverSamples(spiky).Level);
        });

        suite.Run("Driver latency probe on this PC no longer reports the 15.6 ms timer tick", () =>
        {
            var r = new NetworkLatencyService().CheckDriverLatencyAsync(1500).GetAwaiter().GetResult();
            Console.WriteLine($"     driver latency peak {r.MaxDeviationMs} ms, avg {r.AvgDeviationMs} ms ({r.Level})");
            if (r.MaxDeviationMs >= 12) throw new Exception($"Peak {r.MaxDeviationMs} ms looks like the default timer tick.");
        });

        suite.Run("Adapter tweaks pick only values the driver offers (MediaTek Wi-Fi + Realtek LAN)", () =>
        {
            var props = new List<AdapterProperty>
            {
                P("Agressividade de roaming", "RoamIndicateTh", "4",
                  new[] { "1. Desabilitado", "2. Mais baixo", "3. Média a baixa", "4. Média", "5. Média a alta", "6. Mais alto" },
                  new[] { "0", "1", "2", "3", "4", "5" }),
                P("Banda preferencial", "PreferredBand", "0",
                  new[] { "1. Sem preferência", "2. Preferir banda de 2,4 GHz", "3. Preferir banda de 5 GHz" }, new[] { "0", "1", "2" }),
                P("Miracast prefer band", "WfdGOOperatingChannel", "0", new[] { "Auto", "2.4G", "5G" }, new[] { "0", "6", "36" }),
                P("Power Saving", "LowPowerEnable", "1", new[] { "Desabilitado", "Auto" }, new[] { "0", "1" }),
                P("U-APSD Support", "UAPSDSupport", "0", new[] { "Habilitado", "Desabilitado" }, new[] { "1", "0" }),
                P("Pacote Mágico do Wake on", "DisableWakeOnMagic", "0", new[] { "Desabilitado", "Habilitado" }, new[] { "1", "0" }),
                P("Energia Eficiente Ethernet", "*EEE", "1", new[] { "Desactivado", "Activado" }, new[] { "0", "1" }),
                P("Ethernet Verde", "EnableGreenEthernet", "1", new[] { "Desactivado", "Activado" }, new[] { "0", "1" }),
                P("Gigabit Lite", "GigaLite", "1", new[] { "Desactivado", "Activado" }, new[] { "0", "1" }),
                P("Moderação de Interrupção", "*InterruptModeration", "1", new[] { "Disabled", "Enabled" }, new[] { "0", "1" }),
                P("Endereço de Rede", "NetworkAddress", "", Array.Empty<string>(), Array.Empty<string>()),
            };

            var roam = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.WifiRoaming, props);
            TestSuite.Equal(1, roam.Count);
            TestSuite.Equal("1", roam[0].Target); // "Mais baixo", não "Desabilitado"

            var band = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.WifiPrefer5GHz, props);
            TestSuite.Equal(1, band.Count);
            TestSuite.Equal("2", band[0].Target);

            var power = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.PowerSaving, props)
                .ToDictionary(p => p.Prop.RegistryKeyword, p => p.Target);
            TestSuite.SequenceEqual(new[] { "*EEE", "EnableGreenEthernet", "LowPowerEnable", "UAPSDSupport" }, power.Keys.OrderBy(k => k, StringComparer.Ordinal));
            TestSuite.Equal("0", power["LowPowerEnable"]);
            TestSuite.Equal("0", power["UAPSDSupport"]); // já desligado: o plano mantém, a aplicação pula

            var im = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.InterruptModeration, props);
            TestSuite.Equal("0", im.Single().Target);

            var offload = new List<AdapterProperty>
            {
                P("Large Send Offload V2 (IPv4)", "*LsoV2IPv4", "1", new[] { "Desabilitado", "Habilitado" }, new[] { "0", "1" }),
                P("Large Send Offload V2 (IPv6)", "*LsoV2IPv6", "1", new[] { "Disabled", "Enabled" }, new[] { "0", "1" }),
                P("Controle de Fluxo", "*FlowControl", "3",
                  new[] { "Desabilitado", "Tx Habilitado", "Rx Habilitado", "Rx e Tx Habilitados" }, new[] { "0", "1", "2", "3" }),
                P("TCP Checksum Offload (IPv4)", "*TCPChecksumOffloadIPv4", "3", new[] { "Disabled", "Rx & Tx Enabled" }, new[] { "0", "3" }),
            };
            var lso = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.LargeSendOffload, offload);
            TestSuite.SequenceEqual(new[] { "*LsoV2IPv4", "*LsoV2IPv6" }, lso.Select(p => p.Prop.RegistryKeyword).OrderBy(k => k, StringComparer.Ordinal));
            TestSuite.Equal(true, lso.All(p => p.Target == "0"));
            var flow = NetworkOptimizationService.PlanAdapterTweak(AdapterTweak.FlowControl, offload);
            TestSuite.Equal("0", flow.Single().Target);
        });

        suite.Run("Every Latency tool has a safety badge and pros/cons in both languages", () =>
        {
            var missing = new List<string>();
            foreach (var key in Pulse1x.App.ViewModels.LatencyViewModel.AllToolKeys)
                foreach (var suffix in new[] { "_Pros", "_Cons" })
                    if (!Pulse1x.App.Localization.LocalizationStrings.Pt.ContainsKey(key + suffix)
                        || !Pulse1x.App.Localization.LocalizationStrings.En.ContainsKey(key + suffix))
                        missing.Add(key + suffix);
            if (missing.Count > 0) throw new Exception("Missing: " + string.Join(", ", missing));
            TestSuite.Equal(Pulse1x.App.ViewModels.ToolSafety.Advanced, Pulse1x.App.ViewModels.LatencyViewModel.SafetyOf("Lat_ToolResetTcp"));
            TestSuite.Equal(Pulse1x.App.ViewModels.ToolSafety.Safe, Pulse1x.App.ViewModels.LatencyViewModel.SafetyOf("Lat_ToolFlushDns"));
        });

        suite.Run("Power plan index and Auto-Tuning are read in any Windows language", () =>
        {
            // powercfg em espanhol: rótulos que o código não conhece; as duas últimas linhas são CA e CC.
            const string es = "  GUID de configuración: 12bbebe6-58d6-4636-95bb-3217ef867c1a  (Modo de ahorro de energía)\r\n" +
                              "    Índice de configuración de alimentación posible: 000\r\n" +
                              "    Índice de configuración de corriente alterna actual: 0x00000000\r\n" +
                              "    Índice de configuración de corriente continua actual: 0x00000002\r\n";
            TestSuite.Equal((int?)0, NetworkOptimizationService.ParsePowerIndex(es, dc: false));
            TestSuite.Equal((int?)2, NetworkOptimizationService.ParsePowerIndex(es, dc: true));
            const string en = "    Current AC Power Setting Index: 0x00000003\r\n    Current DC Power Setting Index: 0x00000001\r\n";
            TestSuite.Equal((int?)3, NetworkOptimizationService.ParsePowerIndex(en, dc: false));
            TestSuite.Equal((int?)1, NetworkOptimizationService.ParsePowerIndex(en, dc: true));

            TestSuite.Equal("highlyrestricted", NetworkOptimizationService.ParseAutoTuning("HighlyRestricted"));
            TestSuite.Equal("normal", NetworkOptimizationService.ParseAutoTuning("Normal"));
            TestSuite.Equal("—", NetworkOptimizationService.ParseAutoTuning(""));
        });

        suite.Run("Update check compares release tags with the 4-part assembly version", () =>
        {
            TestSuite.Equal(new Version(1, 20, 0), GitHubUpdateService.ParseTag("v1.20.0"));
            TestSuite.Equal(new Version(1, 20, 0), GitHubUpdateService.ParseTag("1.20"));
            TestSuite.Equal(new Version(2, 0, 1), GitHubUpdateService.ParseTag("v2.0.1-beta"));
            TestSuite.Equal((Version?)null, GitHubUpdateService.ParseTag("latest"));
            // "1.19.0" (tag) vs "1.19.0.0" (assembly): mesma versão, não é atualização.
            TestSuite.Equal(false, GitHubUpdateService.IsNewer(GitHubUpdateService.ParseTag("1.19.0")!, new Version(1, 19, 0, 0)));
            TestSuite.Equal(true, GitHubUpdateService.IsNewer(GitHubUpdateService.ParseTag("1.20.0")!, new Version(1, 19, 0, 0)));
            TestSuite.Equal(false, GitHubUpdateService.IsNewer(GitHubUpdateService.ParseTag("1.13.3")!, new Version(1, 19, 0, 0)));
        });

        suite.Run("Reverting a value changed twice ends at the original value", () =>
        {
            string file = "network-order-test.json";
            var log = new OptimizationChangeLog(file);
            var opt = new NetworkOptimizationService(log);
            var record = typeof(NetworkOptimizationService).GetMethod("Record",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            try
            {
                // Duas trocas de MTU pelo mesmo card: 1500 → 1492 → 1480. Desfazer tem de terminar em 1500,
                // ou seja, a troca mais antiga é desfeita por último.
                record.Invoke(opt, new object?[] { "netsh-mtu", "X", "MTU", "1500", "1492", "Lat_MtuTitle", null, "" });
                System.Threading.Thread.Sleep(20);
                record.Invoke(opt, new object?[] { "netsh-mtu", "X", "MTU", "1492", "1480", "Lat_MtuTitle", null, "" });
                var order = opt.ChangesFor("Lat_MtuTitle").Select(c => c.OldValue).ToList();
                TestSuite.SequenceEqual(new[] { "1492", "1500" }, order);
            }
            finally
            {
                foreach (var c in log.GetAllActive().ToList()) log.MarkReverted(c);
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", file));
            }
        });

        suite.Run("Wi-Fi auto-config and MTU are read from Portuguese netsh output", () =>
        {
            const string settings = "    Período de bloqueio: Não Configurado.\r\n\r\n    A lógica de configuração automática está habilitada na interface \"Wi-Fi\"\r\n    MAC aleatório desabilitado na interface Wi-Fi\r\n";
            TestSuite.Equal(true, NetworkOptimizationService.ParseAutoConfig(settings, "Wi-Fi"));
            TestSuite.Equal(false, NetworkOptimizationService.ParseAutoConfig(settings.Replace("está habilitada", "está desabilitada"), "Wi-Fi"));
            TestSuite.Equal(false, NetworkOptimizationService.ParseAutoConfig("    Auto configuration logic is disabled on interface \"Wi-Fi\"", "Wi-Fi"));
            TestSuite.Equal((bool?)null, NetworkOptimizationService.ParseAutoConfig(settings, "Wi-Fi 2"));

            const string mtu = """
                       MTU  MediaSenseState      Bytes Entrada     Bytes Saída Interface
                ----------  ---------------  ------------  ------------  -------------
                4294967295                1             0         15139  Loopback Pseudo-Interface 1
                      1500                1    3093934865     109872730  Wi-Fi
                      1492                5             0             0  Conexão Local* 9
                """;
            TestSuite.Equal(1500, NetworkOptimizationService.ParseMtu(mtu, "Wi-Fi"));
            TestSuite.Equal(1492, NetworkOptimizationService.ParseMtu(mtu, "Conexão Local* 9"));
            TestSuite.Equal(0, NetworkOptimizationService.ParseMtu(mtu, "Ethernet"));
        });

        suite.Run("Each tool's changes are tagged so its Revert button undoes only them", () =>
        {
            string file = "network-scope-test.json";
            var log = new OptimizationChangeLog(file);
            var opt = new NetworkOptimizationService(log);
            var record = typeof(NetworkOptimizationService).GetMethod("Record",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            void Rec(string titleKey) => record.Invoke(opt, new object?[] { "netsh-mtu", "X", "MTU", "1500", "1492", titleKey, null, "" });
            try
            {
                // Um perfil grava ajustes com títulos de outros cards, mas o "Reverter" do perfil leva todos.
                opt.RunScopedAsync("Lat_ToolCompetitive", () => { Rec("Lat_ToolRss"); Rec("Lat_ToolAutoTuning"); return Task.FromResult(new OpResult(true, "x")); })
                   .GetAwaiter().GetResult();
                Rec("Lat_MtuTitle"); // fora de escopo: fica com o próprio card
                TestSuite.Equal(2, opt.ChangesFor("Lat_ToolCompetitive").Count);
                TestSuite.Equal(0, opt.ChangesFor("Lat_ToolRss").Count);
                TestSuite.Equal(1, opt.ChangesFor("Lat_MtuTitle").Count);
                TestSuite.Equal(true, opt.HasChanges());
            }
            finally
            {
                foreach (var c in log.GetAllActive().ToList()) log.MarkReverted(c);
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", file));
            }
        });

        suite.Run("Every Latency text used by the app exists in Portuguese and English", () =>
        {
            var keys = new HashSet<string>();
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "Pulse1x.sln"))) root = Path.GetDirectoryName(root)!;
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Pulse1x.App"), "*.*", SearchOption.AllDirectories)
                         .Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml")) && !p.Contains("LocalizationStrings")
                                     && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                             File.ReadAllText(file), @"[""\[](Lat_[A-Za-z0-9_]+)[""\]](?!\s*\+)"))
                    if (!m.Groups[1].Value.EndsWith("_")) keys.Add(m.Groups[1].Value);

            var missing = keys.Where(k => !Pulse1x.App.Localization.LocalizationStrings.Pt.ContainsKey(k)
                                          || !Pulse1x.App.Localization.LocalizationStrings.En.ContainsKey(k)).OrderBy(k => k).ToList();
            if (missing.Count > 0) throw new Exception("Missing: " + string.Join(", ", missing));
            TestSuite.Equal(true, keys.Count > 150);
        });
    }

    private static AdapterProperty P(string display, string keyword, string value, string[] displays, string[] registry) => new()
    {
        DisplayName = display,
        RegistryKeyword = keyword,
        RegistryValue = value,
        ValidDisplayValues = displays,
        ValidRegistryValues = registry,
    };
}
