using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pulse1x.App.Services;

/// <summary>
/// Aceleração de hardware de aplicativos que guardam a opção num arquivo próprio, e não numa
/// política do Registro: o Discord (settings.json) e o Spotify (prefs). Os navegadores usam as
/// políticas oficiais e ficam no <see cref="AdvancedOptimizationService"/>.
///
/// Cada alteração grava no <see cref="OptimizationChangeLog"/> o valor que existia antes (ou a
/// ausência dele), como <see cref="ChangeKind.AppSetting"/> — desfazer devolve o arquivo ao estado
/// exato anterior, apenas naquela chave.
/// </summary>
public class AppHardwareAccelerationService
{
    private const string DiscordKey = "enableHardwareAcceleration";
    private const string SpotifyKey = "ui.hardware_acceleration";

    private readonly OptimizationChangeLog _log;

    public AppHardwareAccelerationService(OptimizationChangeLog log) => _log = log;

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Estável, PTB e Canary têm pastas separadas; todas seguem o mesmo interruptor.
    private static IEnumerable<string> DiscordSettingsFiles() =>
        new[] { "discord", "discordptb", "discordcanary" }
            .Select(d => Path.Combine(AppData, d))
            .Where(Directory.Exists)
            .Select(d => Path.Combine(d, "settings.json"));

    // Instalador clássico e versão da Microsoft Store.
    private static IEnumerable<string> SpotifyPrefsFiles() =>
        new[]
        {
            Path.Combine(AppData, "Spotify"),
            Path.Combine(LocalAppData, "Packages", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "LocalState", "Spotify"),
        }
        .Where(Directory.Exists)
        .Select(d => Path.Combine(d, "prefs"));

    // ===================== Discord =====================

    public bool IsDiscordInstalled() => DiscordSettingsFiles().Any();

    public bool IsDiscordAccelerationDisabled()
    {
        var files = DiscordSettingsFiles().ToList();
        return files.Count > 0 && files.All(f => ReadJsonValue(f, DiscordKey) == "false");
    }

    public void DisableDiscordAcceleration(string optId, string title)
    {
        foreach (var file in DiscordSettingsFiles())
            SetJsonValue(optId, title, file, DiscordKey, "false");
    }

    public void RestoreDiscordDefault()
    {
        foreach (var file in DiscordSettingsFiles())
            WriteJsonValue(file, DiscordKey, null);
    }

    // ===================== Spotify =====================

    public bool IsSpotifyInstalled() => SpotifyPrefsFiles().Any();

    public bool IsSpotifyAccelerationDisabled()
    {
        var files = SpotifyPrefsFiles().ToList();
        return files.Count > 0 && files.All(f => ReadPrefsValue(f, SpotifyKey) == "false");
    }

    public void DisableSpotifyAcceleration(string optId, string title)
    {
        foreach (var file in SpotifyPrefsFiles())
            SetPrefsValue(optId, title, file, SpotifyKey, "false");
    }

    public void RestoreSpotifyDefault()
    {
        foreach (var file in SpotifyPrefsFiles())
            WritePrefsValue(file, SpotifyKey, null);
    }

    /// <summary>
    /// Os dois apps regravam o arquivo inteiro ao fechar, a partir do que têm na memória. Se
    /// estiverem abertos durante a mudança, ela pode ser desfeita por eles — a tela avisa.
    /// </summary>
    public static bool IsRunning(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            var procs = Process.GetProcessesByName(name);
            bool any = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    // ===================== Reversão =====================

    /// <summary>Desfaz uma alteração <see cref="ChangeKind.AppSetting"/> registrada.</summary>
    public static void Revert(OptimizationChange change)
    {
        if (!File.Exists(change.KeyPath) && change.OldValue is null) return;
        if (change.ValueKind == "Prefs")
            WritePrefsValue(change.KeyPath, change.ValueName, change.OldValue);
        else
            WriteJsonValue(change.KeyPath, change.ValueName, change.OldValue);
    }

    // ===================== JSON (Discord) =====================

    private void SetJsonValue(string optId, string title, string file, string key, string jsonLiteral)
    {
        string? old = ReadJsonValue(file, key);
        if (old == jsonLiteral) return;

        WriteJsonValue(file, key, jsonLiteral);
        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = title,
            Kind = ChangeKind.AppSetting,
            KeyPath = file,
            ValueName = key,
            ValueKind = "Json",
            OldValue = old,
            NewValue = jsonLiteral,
        });
    }

    /// <summary>Valor da chave como literal JSON ("true", "false", "\"x\"") ou null se ausente.</summary>
    public static string? ReadJsonValue(string file, string key)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            return root is not null && root.TryGetPropertyValue(key, out var node)
                ? node?.ToJsonString() ?? "null"
                : null;
        }
        catch { return null; }
    }

    /// <summary>Define a chave com um literal JSON, ou a remove quando <paramref name="jsonLiteral"/> é null.
    /// As demais chaves do arquivo são preservadas.</summary>
    public static void WriteJsonValue(string file, string key, string? jsonLiteral)
    {
        JsonObject root;
        if (File.Exists(file))
        {
            // Arquivo ilegível: não sobrescrevemos o que não entendemos.
            root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject
                   ?? throw new InvalidDataException(file);
        }
        else
        {
            if (jsonLiteral is null) return;
            root = new JsonObject();
        }

        if (jsonLiteral is null) root.Remove(key);
        else root[key] = JsonNode.Parse(jsonLiteral);

        AtomicFile.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ===================== prefs (Spotify) =====================

    private void SetPrefsValue(string optId, string title, string file, string key, string value)
    {
        string? old = ReadPrefsValue(file, key);
        if (old == value) return;

        WritePrefsValue(file, key, value);
        _log.Record(new OptimizationChange
        {
            OptimizationId = optId,
            OptimizationTitle = title,
            Kind = ChangeKind.AppSetting,
            KeyPath = file,
            ValueName = key,
            ValueKind = "Prefs",
            OldValue = old,
            NewValue = value,
        });
    }

    public static string? ReadPrefsValue(string file, string key)
    {
        try
        {
            if (!File.Exists(file)) return null;
            foreach (var line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim() == key)
                    return line[(eq + 1)..].Trim();
            }
        }
        catch { }
        return null;
    }

    /// <summary>Define "chave=valor" no arquivo prefs, ou remove a linha quando <paramref name="value"/> é null.</summary>
    public static void WritePrefsValue(string file, string key, string? value)
    {
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
        if (lines.Count == 0 && value is null) return;

        int index = lines.FindIndex(l =>
        {
            int eq = l.IndexOf('=');
            return eq > 0 && l[..eq].Trim() == key;
        });

        if (value is null)
        {
            if (index >= 0) lines.RemoveAt(index);
        }
        else if (index >= 0) lines[index] = $"{key}={value}";
        else lines.Add($"{key}={value}");

        AtomicFile.WriteAllText(file, string.Join("\n", lines) + "\n");
    }
}
