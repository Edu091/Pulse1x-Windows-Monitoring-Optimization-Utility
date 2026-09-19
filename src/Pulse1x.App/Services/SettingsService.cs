using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

public class AppSettings
{
    public bool DarkTheme { get; set; } = true;
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    // Animações fluidas de interface (transições de página, abertura da janela, entradas).
    // Pode ser desligado nas Configurações para um visual instantâneo / PCs mais fracos.
    public bool AnimationsEnabled { get; set; } = true;

    // Idioma da interface ("pt" ou "en"); padrão Português.
    public string Language { get; set; } = "pt";

    // Ordem (lista de DashboardSectionKind, como string) e visibilidade das seções do
    // dashboard, definidas pelo usuário ao personalizar o layout.
    public List<string> DashboardSectionOrder { get; set; } = new();
    public Dictionary<string, bool> DashboardSectionVisibility { get; set; } = new();

    // Personalização visual de todo o app (cores, transparência, blur, animações, plano de fundo).
    // Fica aqui para viajar junto com o resto das preferências no mesmo settings.json.
    public AppearanceSettings Appearance { get; set; } = new();

    // Preferências próprias do GameHub (início direto, sons, tamanho das capas, capas online).
    public Models.GameHub.GameHubSettings GameHub { get; set; } = new();

    // Personalização do Windows (barra de tarefas, Menu Iniciar, Explorer, Configurações).
    // Só as preferências do subsistema ficam aqui; os temas propriamente ditos moram em
    // arquivos próprios (AppData\Pulse1x\win-themes), um por tema.
    public Models.WinCustom.WinCustomSettings WinCustom { get; set; } = new();

    // Comandos do software do fabricante por modo, para máquinas sem integração nativa
    // (MSI Center, Alienware Command Center e afins). Ver OemCommandAdapter.
    public Profiles.OemCustomCommands OemCommands { get; set; } = new();
}

public class SettingsService
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegistryValueName = "Pulse1x";

    private readonly string _settingsFilePath;

    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(appDataDir);
        _settingsFilePath = Path.Combine(appDataDir, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Arquivo corrompido ou inacessível: usar padrões.
        }

        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }

    public void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            key.SetValue(RunRegistryValueName, $"\"{exePath}\"");
        }
        else
        {
            if (key.GetValue(RunRegistryValueName) != null)
                key.DeleteValue(RunRegistryValueName);
        }

        Current.StartWithWindows = enabled;
        Save();
    }
}
