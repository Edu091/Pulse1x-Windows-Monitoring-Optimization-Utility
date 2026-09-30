namespace Pulse1x.App.Localization;

/// <summary>
/// Textos que ainda estavam fixos em português no código (auditoria de localização): detalhes de
/// hardware, relatórios gerados (sistema e diagnóstico), rede, histórico de reversão, desinstalador,
/// filtros de arquivo e os botões novos do Detector de Bloatware. Sem estas chaves o modo inglês
/// mostrava telas misturadas.
/// </summary>
internal static partial class LocalizationStrings
{
    internal static void AddAuditPortuguese()
    {
        var t = Pt;

        // ---- Controles do painel ----
        t["Chart_WaitingData"] = "Aguardando dados...";
        t["Metric_ClickForDetails"] = "Clique para ver detalhes";

        // ---- Detalhes de hardware ----
        t["Hw_Details"] = "Detalhes";
        t["Hw_Model"] = "Modelo";
        t["Hw_Manufacturer"] = "Fabricante";
        t["Hw_Description"] = "Descrição";
        t["Hw_Socket"] = "Soquete";
        t["Hw_PhysicalCores"] = "Núcleos físicos";
        t["Hw_Threads"] = "Threads (lógicos)";
        t["Hw_MaxClock"] = "Clock máximo";
        t["Hw_Architecture"] = "Arquitetura";
        t["Hw_Virtualization"] = "Virtualização";
        t["Hw_ProcessorId"] = "ID do processador";
        t["Hw_Specs"] = "Especificações";
        t["Hw_CurrentUsage"] = "Uso atual";
        t["Hw_Temperature"] = "Temperatura";
        t["Hw_Approx"] = " (aprox.)";
        t["Hw_NA"] = "N/D";
        t["Hw_LiveFrequency"] = "Frequência (ao vivo)";
        t["Hw_LiveClock"] = "Clock (ao vivo)";
        t["Hw_RealTime"] = "Em tempo real";
        t["Hw_Cpu"] = "Processador";
        t["Hw_ModuleFull"] = "Módulo {0} — {1} ({2})";
        t["Hw_Module"] = "Módulo {0}";
        t["Hw_Capacity"] = "Capacidade";
        t["Hw_Type"] = "Tipo";
        t["Hw_TypeN"] = "Tipo {0}";
        t["Hw_PartNumber"] = "Modelo (part number)";
        t["Hw_RatedSpeed"] = "Velocidade nominal";
        t["Hw_ConfiguredSpeed"] = "Velocidade configurada";
        t["Hw_FormFactor"] = "Formato";
        t["Hw_FormFactorN"] = "Formato {0}";
        t["Hw_ConfiguredVoltage"] = "Voltagem configurada";
        t["Hw_SerialNumber"] = "Número de série";
        t["Hw_TotalCapacity"] = "Capacidade total";
        t["Hw_ModulesInstalled"] = "Módulos instalados";
        t["Hw_Summary"] = "Resumo";
        t["Hw_Ram"] = "Memória RAM";
        t["Hw_VideoProcessor"] = "Processador de vídeo";
        t["Hw_DedicatedMemory"] = "Memória dedicada";
        t["Hw_CurrentResolution"] = "Resolução atual";
        t["Hw_RefreshRate"] = "Taxa de atualização";
        t["Hw_DriverVersion"] = "Versão do driver";
        t["Hw_DriverDate"] = "Data do driver";
        t["Hw_DateFormat"] = "dd/MM/yyyy";
        t["Hw_Gpu"] = "Placa de vídeo";
        t["Hw_Letter"] = "Letra";
        t["Hw_Label"] = "Rótulo";
        t["Hw_FileSystem"] = "Sistema de arquivos";
        t["Hw_DriveType"] = "Tipo de unidade";
        t["Hw_FreeSpace"] = "Espaço livre";
        t["Hw_UsedSpace"] = "Espaço usado";
        t["Hw_Volume"] = "Volume";
        t["Hw_Interface"] = "Interface";
        t["Hw_Media"] = "Mídia";
        t["Hw_Bus"] = "Barramento";
        t["Hw_Partitions"] = "Partições";
        t["Hw_PhysicalDisk"] = "Disco físico";
        t["Hw_Storage"] = "Armazenamento";
        t["Hw_Speed"] = "Velocidade";
        t["Hw_MacAddress"] = "Endereço MAC";
        t["Hw_Network"] = "Rede";
        t["Hw_Status"] = "Status";
        t["Hw_NoActiveAdapter"] = "Nenhum adaptador ativo";
        t["Hw_ActiveAdapters"] = "Adaptadores ativos";
        t["Hw_Enabled"] = "Ativada";
        t["Hw_Disabled"] = "Desativada";
        t["Hw_DriveFixed"] = "Fixo (interno)";
        t["Hw_DriveRemovable"] = "Removível";
        t["Hw_DriveRam"] = "Disco RAM";
        t["Hw_Ethernet"] = "Ethernet (cabo)";

        // ---- Tipo de disco no painel ----
        t["Disk_ExternalRemovable"] = "Externo (removível)";
        t["Disk_ExternalUsb"] = "Externo (USB)";
        t["Disk_Internal"] = "Interno";
        t["Disk_Other"] = "Outro";

        // ---- Histórico de reversão ----
        t["ChangeLog_Task"] = "Tarefa agendada: {0}";
        t["ChangeLog_PowerPlan"] = "Plano de energia do Windows";
        t["ChangeLog_Absent"] = "(ausente)";
        t["ChangeLog_Removed"] = "(removido)";

        // ---- Comandos especiais / relatório do sistema ----
        t["Cmd_HighPerfFailed"] = "Não foi possível ativar o plano Alto Desempenho.";
        t["Cmd_UltimateFailed"] = "Não foi possível criar o plano Ultimate Performance.";
        t["Cmd_GodModeFolder"] = "Painel de Controle Total";
        t["Cmd_NetAdapter"] = "Adaptador em uso: {0}";
        t["Cmd_NetDescription"] = "Descrição: {0}";
        t["Cmd_NetType"] = "Tipo: {0}";
        t["Cmd_NetIp"] = "Endereço IP: {0}";
        t["Cmd_NetMask"] = "Máscara de sub-rede: {0}";
        t["Cmd_NetSpeed"] = "Velocidade da conexão: {0} Mbps";
        t["Cmd_NetMac"] = "Endereço físico (MAC): {0}";
        t["Cmd_NetNone"] = "Nenhuma conexão de rede ativa foi encontrada.";
        t["Cmd_Unknown"] = "Desconhecido";
        t["Cmd_ReportTitle"] = "Relatório do Sistema — Pulse1x";
        t["Cmd_ReportHeader"] = "=== RELATÓRIO DO SISTEMA — Pulse1x ===";
        t["Cmd_ReportGenerated"] = "Gerado em: {0}";
        t["Cmd_ReportDateFormat"] = "dd/MM/yyyy HH:mm:ss";
        t["Cmd_ReportSystem"] = "[ SISTEMA ]";
        t["Cmd_ReportComputer"] = "Computador: {0}";
        t["Cmd_ReportUser"] = "Usuário: {0}";
        t["Cmd_ReportOs"] = "Sistema operacional: {0}";
        t["Cmd_ReportVersion"] = "Versão: {0}";
        t["Cmd_ReportArch"] = "Arquitetura: {0}";
        t["Cmd_ReportCpu"] = "Processador: {0}";
        t["Cmd_ReportRam"] = "Memória RAM total: {0} GB";
        t["Cmd_ReportUptime"] = "Tempo ligado: {0}";
        t["Cmd_ReportStorage"] = "[ ARMAZENAMENTO ]";
        t["Cmd_ReportDrive"] = "{0}  {1} GB livres de {2} GB  ({3})";
        t["Cmd_ReportNetwork"] = "[ REDE ]";

        // ---- Relatório do diagnóstico (TXT) ----
        t["HealthReport_Header"] = "=== DIAGNÓSTICO INTELIGENTE — Pulse1x ===";
        t["HealthReport_Overall"] = "SAÚDE GERAL DO PC: {0}  ({1})";
        t["HealthReport_Scores"] = "[ NOTAS POR COMPONENTE ]";
        t["HealthReport_State"] = "Estado: {0}";
        t["HealthReport_Problems"] = "[ PROBLEMAS DETECTADOS ]";
        t["HealthReport_NoProblems"] = "Nenhum problema relevante encontrado.";
        t["HealthReport_Recommendations"] = "[ RECOMENDAÇÕES ]";
        t["HealthReport_Risks"] = "[ RISCOS ]";

        // ---- Desinstalação / instalação ----
        t["Uninst_NoUninstaller"] = "Nenhum desinstalador registrado.";
        t["Uninst_StartFailed"] = "Não foi possível iniciar o desinstalador.";
        t["Uninst_ExitCode"] = "O desinstalador retornou o código {0}.";
        t["Install_Timeout"] = "Timeout: a instalação não terminou em 20 minutos.";

        // ---- Filtros dos seletores de arquivo ----
        t["Filter_ExeShortcutsUrl"] = "Executáveis e atalhos (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|Todos os arquivos (*.*)|*.*";
        t["Filter_ExeShortcuts"] = "Executáveis (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os arquivos (*.*)|*.*";
        t["Filter_Exe"] = "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*";

        // ---- Detector de Bloatware ----
        t["Bloat_TaskDisableFailed"] = "Não foi possível desativar a tarefa. Execute o Pulse1x como Administrador.";
        t["Bloat_Unignore"] = "Deixar de ignorar";
    }

    internal static void AddAuditEnglish()
    {
        var t = En;

        // ---- Dashboard controls ----
        t["Chart_WaitingData"] = "Waiting for data...";
        t["Metric_ClickForDetails"] = "Click to see details";

        // ---- Hardware details ----
        t["Hw_Details"] = "Details";
        t["Hw_Model"] = "Model";
        t["Hw_Manufacturer"] = "Manufacturer";
        t["Hw_Description"] = "Description";
        t["Hw_Socket"] = "Socket";
        t["Hw_PhysicalCores"] = "Physical cores";
        t["Hw_Threads"] = "Threads (logical)";
        t["Hw_MaxClock"] = "Max clock";
        t["Hw_Architecture"] = "Architecture";
        t["Hw_Virtualization"] = "Virtualization";
        t["Hw_ProcessorId"] = "Processor ID";
        t["Hw_Specs"] = "Specifications";
        t["Hw_CurrentUsage"] = "Current usage";
        t["Hw_Temperature"] = "Temperature";
        t["Hw_Approx"] = " (approx.)";
        t["Hw_NA"] = "N/A";
        t["Hw_LiveFrequency"] = "Frequency (live)";
        t["Hw_LiveClock"] = "Clock (live)";
        t["Hw_RealTime"] = "Real time";
        t["Hw_Cpu"] = "Processor";
        t["Hw_ModuleFull"] = "Module {0} — {1} ({2})";
        t["Hw_Module"] = "Module {0}";
        t["Hw_Capacity"] = "Capacity";
        t["Hw_Type"] = "Type";
        t["Hw_TypeN"] = "Type {0}";
        t["Hw_PartNumber"] = "Model (part number)";
        t["Hw_RatedSpeed"] = "Rated speed";
        t["Hw_ConfiguredSpeed"] = "Configured speed";
        t["Hw_FormFactor"] = "Form factor";
        t["Hw_FormFactorN"] = "Form factor {0}";
        t["Hw_ConfiguredVoltage"] = "Configured voltage";
        t["Hw_SerialNumber"] = "Serial number";
        t["Hw_TotalCapacity"] = "Total capacity";
        t["Hw_ModulesInstalled"] = "Installed modules";
        t["Hw_Summary"] = "Summary";
        t["Hw_Ram"] = "RAM";
        t["Hw_VideoProcessor"] = "Video processor";
        t["Hw_DedicatedMemory"] = "Dedicated memory";
        t["Hw_CurrentResolution"] = "Current resolution";
        t["Hw_RefreshRate"] = "Refresh rate";
        t["Hw_DriverVersion"] = "Driver version";
        t["Hw_DriverDate"] = "Driver date";
        t["Hw_DateFormat"] = "MM/dd/yyyy";
        t["Hw_Gpu"] = "Graphics card";
        t["Hw_Letter"] = "Letter";
        t["Hw_Label"] = "Label";
        t["Hw_FileSystem"] = "File system";
        t["Hw_DriveType"] = "Drive type";
        t["Hw_FreeSpace"] = "Free space";
        t["Hw_UsedSpace"] = "Used space";
        t["Hw_Volume"] = "Volume";
        t["Hw_Interface"] = "Interface";
        t["Hw_Media"] = "Media";
        t["Hw_Bus"] = "Bus";
        t["Hw_Partitions"] = "Partitions";
        t["Hw_PhysicalDisk"] = "Physical disk";
        t["Hw_Storage"] = "Storage";
        t["Hw_Speed"] = "Speed";
        t["Hw_MacAddress"] = "MAC address";
        t["Hw_Network"] = "Network";
        t["Hw_Status"] = "Status";
        t["Hw_NoActiveAdapter"] = "No active adapter";
        t["Hw_ActiveAdapters"] = "Active adapters";
        t["Hw_Enabled"] = "Enabled";
        t["Hw_Disabled"] = "Disabled";
        t["Hw_DriveFixed"] = "Fixed (internal)";
        t["Hw_DriveRemovable"] = "Removable";
        t["Hw_DriveRam"] = "RAM disk";
        t["Hw_Ethernet"] = "Ethernet (wired)";

        // ---- Dashboard disk type ----
        t["Disk_ExternalRemovable"] = "External (removable)";
        t["Disk_ExternalUsb"] = "External (USB)";
        t["Disk_Internal"] = "Internal";
        t["Disk_Other"] = "Other";

        // ---- Revert history ----
        t["ChangeLog_Task"] = "Scheduled task: {0}";
        t["ChangeLog_PowerPlan"] = "Windows power plan";
        t["ChangeLog_Absent"] = "(absent)";
        t["ChangeLog_Removed"] = "(removed)";

        // ---- Special commands / system report ----
        t["Cmd_HighPerfFailed"] = "Could not activate the High Performance plan.";
        t["Cmd_UltimateFailed"] = "Could not create the Ultimate Performance plan.";
        t["Cmd_GodModeFolder"] = "God Mode";
        t["Cmd_NetAdapter"] = "Adapter in use: {0}";
        t["Cmd_NetDescription"] = "Description: {0}";
        t["Cmd_NetType"] = "Type: {0}";
        t["Cmd_NetIp"] = "IP address: {0}";
        t["Cmd_NetMask"] = "Subnet mask: {0}";
        t["Cmd_NetSpeed"] = "Connection speed: {0} Mbps";
        t["Cmd_NetMac"] = "Physical address (MAC): {0}";
        t["Cmd_NetNone"] = "No active network connection was found.";
        t["Cmd_Unknown"] = "Unknown";
        t["Cmd_ReportTitle"] = "System Report — Pulse1x";
        t["Cmd_ReportHeader"] = "=== SYSTEM REPORT — Pulse1x ===";
        t["Cmd_ReportGenerated"] = "Generated on: {0}";
        t["Cmd_ReportDateFormat"] = "MM/dd/yyyy HH:mm:ss";
        t["Cmd_ReportSystem"] = "[ SYSTEM ]";
        t["Cmd_ReportComputer"] = "Computer: {0}";
        t["Cmd_ReportUser"] = "User: {0}";
        t["Cmd_ReportOs"] = "Operating system: {0}";
        t["Cmd_ReportVersion"] = "Version: {0}";
        t["Cmd_ReportArch"] = "Architecture: {0}";
        t["Cmd_ReportCpu"] = "Processor: {0}";
        t["Cmd_ReportRam"] = "Total RAM: {0} GB";
        t["Cmd_ReportUptime"] = "Uptime: {0}";
        t["Cmd_ReportStorage"] = "[ STORAGE ]";
        t["Cmd_ReportDrive"] = "{0}  {1} GB free of {2} GB  ({3})";
        t["Cmd_ReportNetwork"] = "[ NETWORK ]";

        // ---- Diagnostics report (TXT) ----
        t["HealthReport_Header"] = "=== SMART DIAGNOSTICS — Pulse1x ===";
        t["HealthReport_Overall"] = "OVERALL PC HEALTH: {0}  ({1})";
        t["HealthReport_Scores"] = "[ SCORES BY COMPONENT ]";
        t["HealthReport_State"] = "State: {0}";
        t["HealthReport_Problems"] = "[ DETECTED PROBLEMS ]";
        t["HealthReport_NoProblems"] = "No relevant problems found.";
        t["HealthReport_Recommendations"] = "[ RECOMMENDATIONS ]";
        t["HealthReport_Risks"] = "[ RISKS ]";

        // ---- Uninstall / install ----
        t["Uninst_NoUninstaller"] = "No uninstaller registered.";
        t["Uninst_StartFailed"] = "Could not start the uninstaller.";
        t["Uninst_ExitCode"] = "The uninstaller returned code {0}.";
        t["Install_Timeout"] = "Timeout: the installation did not finish within 20 minutes.";

        // ---- File picker filters ----
        t["Filter_ExeShortcutsUrl"] = "Executables and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*";
        t["Filter_ExeShortcuts"] = "Executables (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*";
        t["Filter_Exe"] = "Executables (*.exe)|*.exe|All files (*.*)|*.*";

        // ---- Bloatware Detector ----
        t["Bloat_TaskDisableFailed"] = "Could not disable the task. Run Pulse1x as Administrator.";
        t["Bloat_Unignore"] = "Stop ignoring";
    }
}
