using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>
/// Aplica as "Configurações Recomendadas" da Central Pós-Formatação. Cada ação é pontual
/// (aplica uma vez) e explicada na tela antes de rodar. Reaproveita o
/// <see cref="SpecialCommandsService"/> para planos de energia e ponto de restauração, e
/// escreve ajustes leves e reversíveis do Explorador diretamente no registro do usuário (HKCU).
/// </summary>
public class PostFormatTweaksService
{
    private readonly SpecialCommandsService _commands;

    public PostFormatTweaksService(SpecialCommandsService commands) => _commands = commands;

    /// <summary>Executa uma configuração pelo seu Id (mesmos Ids de <c>PostFormatCatalog.Settings</c>).</summary>
    public async Task<CommandResult> ApplyAsync(string id) => id switch
    {
        "restore_point" => await _commands.CreateRestorePointAsync("Pulse1x — Central Pós-Formatação"),
        "enable_system_restore" => await EnableSystemRestoreAsync(),
        "high_performance" => await _commands.SetHighPerformancePlanAsync(),
        "ultimate_performance" => await _commands.SetUltimatePlanAsync(),
        "disable_suggestions" => DisableSuggestions(),
        "reduce_telemetry" => await ReduceTelemetryAsync(),
        "show_extensions" => SetExplorerDword("HideFileExt", 0),
        "show_hidden" => SetExplorerDword("Hidden", 1),
        "explorer_this_pc" => SetExplorerDword("LaunchTo", 1),
        "manage_startup" => OpenStartupManager(),
        _ => new CommandResult(1, Localization.Loc.S("Pf_UnknownSetting")),
    };

    private async Task<CommandResult> EnableSystemRestoreAsync()
    {
        string ps =
            "$ErrorActionPreference='Stop'; " +
            "Enable-ComputerRestore -Drive 'C:\\'; " +
            "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' " +
            "-Name 'SystemRestorePointCreationFrequency' -Value 0 -PropertyType DWord -Force | Out-Null";
        return await _commands.RunProcessAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
    }

    // Reduz a telemetria opcional ao mínimo permitido na edição (Basic/Required = 1).
    private async Task<CommandResult> ReduceTelemetryAsync()
    {
        string ps =
            "$ErrorActionPreference='Stop'; " +
            "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name 'AllowTelemetry' -Value 1 -Type DWord";
        return await _commands.RunProcessAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
    }

    // Desliga as sugestões/dicas/anúncios do menu Iniciar, configurações e tela de bloqueio.
    private static CommandResult DisableSuggestions()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
            foreach (var name in new[]
            {
                "SubscribedContent-338388Enabled", // sugestões no Iniciar
                "SubscribedContent-338389Enabled", // dicas/anúncios
                "SubscribedContent-310093Enabled", // dicas de boas-vindas
                "SystemPaneSuggestionsEnabled",     // sugestões no painel
                "SoftLandingEnabled",
            })
                key?.SetValue(name, 0, RegistryValueKind.DWord);
            return new CommandResult(0, Localization.Loc.S("Pf_SuggestionsDisabled"));
        }
        catch (Exception ex)
        {
            return new CommandResult(1, ex.Message);
        }
    }

    // Ajustes do Explorador (HKCU) — leves e reversíveis pelo próprio Explorador.
    private static CommandResult SetExplorerDword(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            key?.SetValue(name, value, RegistryValueKind.DWord);
            return new CommandResult(0, $"{name} = {value}");
        }
        catch (Exception ex)
        {
            return new CommandResult(1, ex.Message);
        }
    }

    private CommandResult OpenStartupManager()
    {
        try
        {
            try { _commands.OpenTool("taskmgr.exe", "/7"); }
            catch { _commands.OpenTool("ms-settings:startupapps"); }
            return new CommandResult(0, Localization.Loc.S("Pf_StartupManagerOpened"));
        }
        catch (Exception ex)
        {
            return new CommandResult(1, ex.Message);
        }
    }
}
