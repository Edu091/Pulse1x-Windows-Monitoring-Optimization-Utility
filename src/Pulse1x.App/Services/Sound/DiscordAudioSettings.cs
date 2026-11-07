using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// O subsistema de áudio do Discord. No "experimental", ele captura o microfone em modo Raw — o
/// caminho que o Windows entrega sem efeitos de stream —, então volume extra, filtros e modificador
/// de voz não chegam à call (medido no microfone real: o modo Raw não recebe o efeito). No
/// "standard" ele usa o caminho de comunicações, que o Pulse1x já cobre.
///
/// A opção fica em %APPDATA%\discord\settings.json e é lida na abertura do Discord; com ele aberto,
/// a mudança seria sobrescrita, por isso só é feita com o Discord fechado.
/// </summary>
public static class DiscordAudioSettings
{
    private const string Key = "audioSubsystem";

    public static string SettingsPath(string? appData = null) =>
        Path.Combine(appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");

    /// <summary>"experimental", "standard", "legacy" ou null (Discord não instalado / sem a opção).</summary>
    public static string? GetSubsystem(string? path = null)
    {
        try
        {
            path ??= SettingsPath();
            if (!File.Exists(path)) return null;
            return JsonNode.Parse(File.ReadAllText(path))?[Key]?.GetValue<string>();
        }
        catch { return null; }
    }

    public static bool IsDiscordRunning() =>
        Process.GetProcessesByName("Discord").Length > 0;

    /// <summary>Troca para "standard" preservando o resto do arquivo. Faz uma cópia antes.</summary>
    public static void SetStandard(string? path = null)
    {
        path ??= SettingsPath();
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidDataException("settings.json do Discord inválido.");
        try { File.Copy(path, path + ".pulse1x-backup", overwrite: true); } catch { }
        root[Key] = "standard";
        AtomicFile.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
