namespace Pulse1x.App.Localization;

/// <summary>Textos da seção "BIOS e desempenho" do Dashboard.</summary>
internal static partial class LocalizationStrings
{
    internal static void AddFirmwarePortuguese()
    {
        var t = Pt;
        t["Dashboard_SectionFirmware"] = "BIOS e desempenho";
        t["Fw_GroupBios"] = "BIOS / UEFI e hardware";
        t["Fw_GroupWindows"] = "Windows";
        t["Fw_Refresh"] = "Atualizar";
        t["Fw_Expand"] = "Mostrar detalhes";
        t["Fw_Collapse"] = "Ocultar";
        t["Fw_RestartToBios"] = "Reiniciar na BIOS";
        t["Fw_RestartConfirm"] = "O PC vai reiniciar direto na tela de configuração da BIOS/UEFI em 5 segundos.\n\nSalve o que estiver aberto antes. Continuar?";
        t["Fw_RestartFailed"] = "Não foi possível reiniciar na BIOS por aqui. Use Configurações > Sistema > Recuperação > Inicialização avançada > Configurações de firmware UEFI, ou aperte Del/F2 ao ligar o PC.";
        t["Fw_Summary"] = "{0} em ordem · {1} para revisar";
        t["Fw_Footer"] = "Somente leitura: o Pulse1x não altera a BIOS. Itens de BIOS mudam no setup do PC (Del, F2 ou F10 ao ligar) e valem após reiniciar.";
        t["Fw_StateGood"] = "OK";
        t["Fw_StateAttention"] = "Revisar";
        t["Fw_StateInfo"] = "Info";
        t["Fw_StateNotSupported"] = "Sem suporte";
        t["Fw_StateUnknown"] = "Desconhecido";
        t["Fw_Enabled"] = "Ativado";
        t["Fw_Disabled"] = "Desativado";
        t["Fw_DefaultOff"] = "Desativado (padrão)";
        t["Fw_Unknown"] = "Não foi possível verificar";
        t["Fw_NotAvailable"] = "Indisponível";
        t["Fw_NeedsUefi"] = "Exige boot UEFI (este PC está em modo Legacy/CSM).";
        t["Fw_CompatUefiYes"] = "Compatível: o PC inicia em UEFI.";
        t["Fw_WindowsCompat"] = "Configuração do Windows — vale para qualquer PC.";

        t["Fw_ReBar"] = "Resizable BAR / Smart Access Memory";
        t["Fw_NoDedicatedGpu"] = "Sem placa de vídeo dedicada";
        t["Fw_ReBarCompatNoGpu"] = "Só existe em placas de vídeo dedicadas (RTX 30+, RX 6000+, Intel Arc).";
        t["Fw_ReBarCompatYes"] = "Compatível: {0} suporta e o PC está em UEFI.";
        t["Fw_ReBarCompatNeedsUefi"] = "A placa suporta, mas exige boot UEFI (desligue o CSM).";
        t["Fw_ReBarCompatNo"] = "Não compatível: {0} não suporta Resizable BAR.";
        t["Fw_ReBarCompatMaybe"] = "Verifique se {0} e a placa-mãe suportam (RTX 30+, RX 6000+, Intel Arc; Intel 10ª ger.+/Ryzen 3000+).";
        t["Fw_ReBarTipOn"] = "Ótimo: a CPU enxerga toda a VRAM de uma vez — até ~10% de FPS em jogos compatíveis.";
        t["Fw_ReBarTipOff"] = "Ative na BIOS: \"Above 4G Decoding\" + \"Re-Size BAR Support\" (ou \"Smart Access Memory\"). Na Intel Arc é essencial.";
        t["Fw_ReBarTipUnknown"] = "O driver desta placa não informa o estado; confira no painel da AMD/Intel ou na BIOS.";

        t["Fw_Xmp"] = "Perfil de memória (XMP / EXPO)";
        t["Fw_XmpOn"] = "Ativado — {0}";
        t["Fw_XmpJedec"] = "Padrão JEDEC — {0}";
        t["Fw_XmpCompatYes"] = "A memória está acima da velocidade padrão, como prevê o perfil de fábrica.";
        t["Fw_XmpCompatLaptop"] = "Notebooks quase nunca têm XMP: a memória roda na velocidade definida pelo fabricante.";
        t["Fw_XmpCompatMaybe"] = "Depende dos pentes: confira a frequência na etiqueta (ex.: DDR4-3600, DDR5-6000).";
        t["Fw_XmpTipOn"] = "Ótimo: a RAM roda na velocidade da etiqueta, o que ajuda principalmente o FPS mínimo.";
        t["Fw_XmpTipOff"] = "Se os pentes forem de frequência maior, ative XMP (Intel) ou EXPO/DOCP (AMD) na BIOS: +5 a 15% de FPS mínimo.";
        t["Fw_XmpLimited"] = "Os módulos suportam {0:N0} MT/s, mas o processador/plataforma limita a velocidade atual.";

        t["Fw_SecureBoot"] = "Secure Boot";
        t["Fw_SecureBootTipOn"] = "Exigido pelos anti-cheats de Valorant, Battlefield 6 e Call of Duty, e pelo Windows 11.";
        t["Fw_SecureBootTipOff"] = "Jogos como Valorant, Battlefield 6 e Call of Duty exigem Secure Boot: ative na BIOS (aba Boot/Security).";

        t["Fw_Tpm"] = "TPM (fTPM / PTT)";
        t["Fw_TpmNotFound"] = "Não encontrado ou desativado";
        t["Fw_TpmCompat"] = "Processadores desde Intel 8ª ger. e Ryzen 2000 têm TPM 2.0 embutido (PTT/fTPM).";
        t["Fw_TpmTipOn"] = "Exigido pelo Windows 11 e por anti-cheats (Vanguard, Javelin).";
        t["Fw_TpmTipOff"] = "Ative \"Intel PTT\" ou \"AMD fTPM\" na BIOS: o Windows 11 e vários anti-cheats exigem TPM 2.0.";
        t["Fw_TpmTipOld"] = "TPM 1.2 não atende o Windows 11 nem os anti-cheats atuais; veja se a BIOS oferece o 2.0.";

        t["Fw_Vt"] = "Virtualização (VT-x / AMD-V)";
        t["Fw_VtCompatYes"] = "Compatível: o processador suporta virtualização.";
        t["Fw_VtCompatNo"] = "O processador não suporta virtualização.";
        t["Fw_VtTipOn"] = "Necessária para WSL, emuladores Android (BlueStacks) e máquinas virtuais.";
        t["Fw_VtTipOff"] = "Não pesa em jogos. Ative (\"Intel VT-x\" / \"SVM Mode\") só se usar WSL, emuladores Android ou VMs.";

        t["Fw_Uefi"] = "Modo de boot";
        t["Fw_UefiCompat"] = "UEFI é o padrão de PCs desde ~2012.";
        t["Fw_UefiTipOn"] = "Permite Secure Boot e Resizable BAR.";
        t["Fw_UefiTipOff"] = "Em Legacy/CSM não há Secure Boot nem Resizable BAR. Migrar exige converter o disco para GPT (mbr2gpt).";

        t["Fw_Smt"] = "Hyper-Threading / SMT";
        t["Fw_SmtValue"] = "{0} núcleos, {1} threads";
        t["Fw_SmtCompat"] = "Alguns processadores não têm (ex.: Core Ultra 200S, vários Core i5 antigos).";
        t["Fw_SmtTipOn"] = "Mais threads ajudam jogos modernos, streaming e multitarefa.";
        t["Fw_SmtTipOff"] = "Sem threads extras: o processador não tem SMT ou ele está desligado na BIOS.";

        t["Fw_Channels"] = "Canais de memória";
        t["Fw_ChannelsValue"] = "{0} módulo(s), {1} GB";
        t["Fw_DualChannel"] = "Dual-channel (provável)";
        t["Fw_SingleChannel"] = "Single-channel";
        t["Fw_ChannelsCompat"] = "Dual-channel precisa de 2 (ou 4) pentes iguais nos slots indicados pelo manual.";
        t["Fw_ChannelsTipOn"] = "Dobra a banda de memória em relação a um pente só.";
        t["Fw_ChannelsTipOff"] = "Com um pente só a banda cai pela metade: adicionar outro igual dá até +30% de FPS em placas integradas.";

        t["Fw_Fans"] = "Ventoinhas";
        t["Fw_FansNone"] = "Não expostas pelo hardware";
        t["Fw_FansCompatLaptop"] = "Em notebooks o controle fica com o fabricante (MSI Center, Armoury Crate, Vantage...).";
        t["Fw_FansCompatDesktop"] = "A placa-mãe não expõe os sensores (ou a integridade de memória bloqueia o driver de leitura).";
        t["Fw_FansCompatYes"] = "Lido do chip de monitoramento da placa-mãe e da GPU.";
        t["Fw_FansTip"] = "Curvas de ventoinha ficam na BIOS (Q-Fan, Smart Fan) ou no app do fabricante.";

        t["Fw_Hvci"] = "Integridade de memória (HVCI)";
        t["Fw_HvciCompat"] = "Recurso de segurança do Windows (Isolamento de núcleo).";
        t["Fw_HvciTipOn"] = "Protege o kernel, mas custa ~5-10% de FPS em vários jogos. Desligar fica em Segurança do Windows > Isolamento de núcleo (reduz a proteção).";
        t["Fw_HvciTipOff"] = "Sem o custo de desempenho da virtualização de segurança.";

        t["Fw_Hags"] = "Agendamento de GPU acelerado (HAGS)";
        t["Fw_HagsCompat"] = "Exige GPU GTX 10/RX 5000/Arc ou mais nova e Windows 10 2004+.";
        t["Fw_HagsTipOn"] = "Reduz latência e é exigido pela geração de quadros do DLSS 3/4.";
        t["Fw_HagsTipOff"] = "Ative em Configurações > Sistema > Tela > Elementos gráficos > Alterar configurações gráficas padrão.";

        t["Fw_GameMode"] = "Modo de Jogo do Windows";
        t["Fw_GameModeTipOn"] = "Prioriza o jogo e segura atualizações do Windows durante a partida.";
        t["Fw_GameModeTipOff"] = "Ative em Configurações > Jogos > Modo de Jogo.";

        t["Fw_GameDvr"] = "Gravação em segundo plano (Game Bar)";
        t["Fw_GameDvrTipOn"] = "Grava o jogo o tempo todo para \"gravar o que aconteceu\": custa FPS. Desligue em Configurações > Jogos > Capturas.";
        t["Fw_GameDvrTipOff"] = "Sem gravação contínua consumindo GPU e disco.";

        t["Fw_PowerPlan"] = "Plano de energia";
        t["Fw_PowerTipOn"] = "Clocks altos o tempo todo.";
        t["Fw_PowerTipOff"] = "Para jogar, um plano de alto desempenho evita quedas de clock (o GameHub troca sozinho por perfil).";
    }

    internal static void AddFirmwareEnglish()
    {
        var t = En;
        t["Dashboard_SectionFirmware"] = "BIOS and performance";
        t["Fw_GroupBios"] = "BIOS / UEFI and hardware";
        t["Fw_GroupWindows"] = "Windows";
        t["Fw_Refresh"] = "Refresh";
        t["Fw_Expand"] = "Show details";
        t["Fw_Collapse"] = "Hide";
        t["Fw_RestartToBios"] = "Restart into BIOS";
        t["Fw_RestartConfirm"] = "The PC will restart straight into the BIOS/UEFI setup screen in 5 seconds.\n\nSave your open work first. Continue?";
        t["Fw_RestartFailed"] = "Couldn't restart into the BIOS from here. Use Settings > System > Recovery > Advanced startup > UEFI Firmware Settings, or press Del/F2 at power-on.";
        t["Fw_Summary"] = "{0} fine · {1} to review";
        t["Fw_Footer"] = "Read-only: Pulse1x never changes the BIOS. BIOS items are changed in the PC setup (Del, F2 or F10 at power-on) and apply after a restart.";
        t["Fw_StateGood"] = "OK";
        t["Fw_StateAttention"] = "Review";
        t["Fw_StateInfo"] = "Info";
        t["Fw_StateNotSupported"] = "Not supported";
        t["Fw_StateUnknown"] = "Unknown";
        t["Fw_Enabled"] = "Enabled";
        t["Fw_Disabled"] = "Disabled";
        t["Fw_DefaultOff"] = "Disabled (default)";
        t["Fw_Unknown"] = "Couldn't check";
        t["Fw_NotAvailable"] = "Unavailable";
        t["Fw_NeedsUefi"] = "Requires UEFI boot (this PC boots in Legacy/CSM mode).";
        t["Fw_CompatUefiYes"] = "Compatible: the PC boots in UEFI.";
        t["Fw_WindowsCompat"] = "Windows setting — applies to any PC.";

        t["Fw_ReBar"] = "Resizable BAR / Smart Access Memory";
        t["Fw_NoDedicatedGpu"] = "No dedicated graphics card";
        t["Fw_ReBarCompatNoGpu"] = "Only exists on dedicated graphics cards (RTX 30+, RX 6000+, Intel Arc).";
        t["Fw_ReBarCompatYes"] = "Compatible: {0} supports it and the PC boots in UEFI.";
        t["Fw_ReBarCompatNeedsUefi"] = "The card supports it, but it requires UEFI boot (turn CSM off).";
        t["Fw_ReBarCompatNo"] = "Not compatible: {0} doesn't support Resizable BAR.";
        t["Fw_ReBarCompatMaybe"] = "Check whether {0} and the motherboard support it (RTX 30+, RX 6000+, Intel Arc; Intel 10th gen+/Ryzen 3000+).";
        t["Fw_ReBarTipOn"] = "Great: the CPU sees all the VRAM at once — up to ~10% FPS in supported games.";
        t["Fw_ReBarTipOff"] = "Enable in the BIOS: \"Above 4G Decoding\" + \"Re-Size BAR Support\" (or \"Smart Access Memory\"). Essential on Intel Arc.";
        t["Fw_ReBarTipUnknown"] = "This card's driver doesn't report it; check the AMD/Intel control panel or the BIOS.";

        t["Fw_Xmp"] = "Memory profile (XMP / EXPO)";
        t["Fw_XmpOn"] = "Enabled — {0}";
        t["Fw_XmpJedec"] = "JEDEC default — {0}";
        t["Fw_XmpCompatYes"] = "Memory runs above the default speed, as the factory profile intends.";
        t["Fw_XmpCompatLaptop"] = "Laptops almost never have XMP: memory runs at the speed set by the manufacturer.";
        t["Fw_XmpCompatMaybe"] = "Depends on the sticks: check the speed on the label (e.g. DDR4-3600, DDR5-6000).";
        t["Fw_XmpTipOn"] = "Great: RAM runs at its rated speed, which mostly helps minimum FPS.";
        t["Fw_XmpTipOff"] = "If your sticks are rated higher, enable XMP (Intel) or EXPO/DOCP (AMD) in the BIOS: +5 to 15% minimum FPS.";
        t["Fw_XmpLimited"] = "The modules support {0:N0} MT/s, but the processor/platform limits the current speed.";

        t["Fw_SecureBoot"] = "Secure Boot";
        t["Fw_SecureBootTipOn"] = "Required by the anti-cheats of Valorant, Battlefield 6 and Call of Duty, and by Windows 11.";
        t["Fw_SecureBootTipOff"] = "Games like Valorant, Battlefield 6 and Call of Duty require Secure Boot: enable it in the BIOS (Boot/Security tab).";

        t["Fw_Tpm"] = "TPM (fTPM / PTT)";
        t["Fw_TpmNotFound"] = "Not found or disabled";
        t["Fw_TpmCompat"] = "Processors since Intel 8th gen and Ryzen 2000 have a built-in TPM 2.0 (PTT/fTPM).";
        t["Fw_TpmTipOn"] = "Required by Windows 11 and anti-cheats (Vanguard, Javelin).";
        t["Fw_TpmTipOff"] = "Enable \"Intel PTT\" or \"AMD fTPM\" in the BIOS: Windows 11 and several anti-cheats require TPM 2.0.";
        t["Fw_TpmTipOld"] = "TPM 1.2 doesn't meet Windows 11 or current anti-cheats; check whether the BIOS offers 2.0.";

        t["Fw_Vt"] = "Virtualization (VT-x / AMD-V)";
        t["Fw_VtCompatYes"] = "Compatible: the processor supports virtualization.";
        t["Fw_VtCompatNo"] = "The processor doesn't support virtualization.";
        t["Fw_VtTipOn"] = "Needed for WSL, Android emulators (BlueStacks) and virtual machines.";
        t["Fw_VtTipOff"] = "No effect on games. Enable it (\"Intel VT-x\" / \"SVM Mode\") only if you use WSL, Android emulators or VMs.";

        t["Fw_Uefi"] = "Boot mode";
        t["Fw_UefiCompat"] = "UEFI has been the PC standard since ~2012.";
        t["Fw_UefiTipOn"] = "Allows Secure Boot and Resizable BAR.";
        t["Fw_UefiTipOff"] = "Legacy/CSM has no Secure Boot or Resizable BAR. Switching requires converting the disk to GPT (mbr2gpt).";

        t["Fw_Smt"] = "Hyper-Threading / SMT";
        t["Fw_SmtValue"] = "{0} cores, {1} threads";
        t["Fw_SmtCompat"] = "Some processors don't have it (e.g. Core Ultra 200S, many older Core i5).";
        t["Fw_SmtTipOn"] = "More threads help modern games, streaming and multitasking.";
        t["Fw_SmtTipOff"] = "No extra threads: the processor has no SMT or it's disabled in the BIOS.";

        t["Fw_Channels"] = "Memory channels";
        t["Fw_ChannelsValue"] = "{0} module(s), {1} GB";
        t["Fw_DualChannel"] = "Dual-channel (likely)";
        t["Fw_SingleChannel"] = "Single-channel";
        t["Fw_ChannelsCompat"] = "Dual-channel needs 2 (or 4) matching sticks in the slots the manual indicates.";
        t["Fw_ChannelsTipOn"] = "Doubles memory bandwidth compared to a single stick.";
        t["Fw_ChannelsTipOff"] = "With one stick bandwidth is halved: adding a matching one gives up to +30% FPS on integrated graphics.";

        t["Fw_Fans"] = "Fans";
        t["Fw_FansNone"] = "Not exposed by the hardware";
        t["Fw_FansCompatLaptop"] = "On laptops the manufacturer controls them (MSI Center, Armoury Crate, Vantage...).";
        t["Fw_FansCompatDesktop"] = "The motherboard doesn't expose the sensors (or memory integrity blocks the reading driver).";
        t["Fw_FansCompatYes"] = "Read from the motherboard monitoring chip and the GPU.";
        t["Fw_FansTip"] = "Fan curves live in the BIOS (Q-Fan, Smart Fan) or in the manufacturer's app.";

        t["Fw_Hvci"] = "Memory integrity (HVCI)";
        t["Fw_HvciCompat"] = "Windows security feature (Core isolation).";
        t["Fw_HvciTipOn"] = "Protects the kernel but costs ~5-10% FPS in many games. Turning it off is in Windows Security > Core isolation (reduces protection).";
        t["Fw_HvciTipOff"] = "No performance cost from security virtualization.";

        t["Fw_Hags"] = "Hardware-accelerated GPU scheduling (HAGS)";
        t["Fw_HagsCompat"] = "Requires a GTX 10/RX 5000/Arc GPU or newer and Windows 10 2004+.";
        t["Fw_HagsTipOn"] = "Lowers latency and is required by DLSS 3/4 frame generation.";
        t["Fw_HagsTipOff"] = "Enable in Settings > System > Display > Graphics > Change default graphics settings.";

        t["Fw_GameMode"] = "Windows Game Mode";
        t["Fw_GameModeTipOn"] = "Prioritizes the game and holds Windows updates during play.";
        t["Fw_GameModeTipOff"] = "Enable in Settings > Gaming > Game Mode.";

        t["Fw_GameDvr"] = "Background recording (Game Bar)";
        t["Fw_GameDvrTipOn"] = "Records gameplay all the time to \"record what happened\": costs FPS. Turn it off in Settings > Gaming > Captures.";
        t["Fw_GameDvrTipOff"] = "No continuous recording using GPU and disk.";

        t["Fw_PowerPlan"] = "Power plan";
        t["Fw_PowerTipOn"] = "High clocks all the time.";
        t["Fw_PowerTipOff"] = "For gaming, a high-performance plan avoids clock drops (GameHub switches it per profile).";
    }
}
