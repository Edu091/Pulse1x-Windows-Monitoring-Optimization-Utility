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

    public SettingsService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x", "settings.json"))
    {
    }

    public SettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
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
                if (settings != null) return Normalize(settings);
            }
        }
        catch
        {
            // Arquivo corrompido ou inacessível: usar padrões.
        }

        return new AppSettings();
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.UpdateIntervalMs = Math.Clamp(settings.UpdateIntervalMs, 250, 10_000);
        settings.Language = string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "pt";
        settings.DashboardSectionOrder ??= new List<string>();
        settings.DashboardSectionVisibility ??= new Dictionary<string, bool>();
        settings.Appearance ??= new AppearanceSettings();
        settings.GameHub ??= new Models.GameHub.GameHubSettings();
        settings.OemCommands ??= new Profiles.OemCustomCommands();

        settings.GameHub.SoundVolume = Math.Clamp(settings.GameHub.SoundVolume, 0, 1);
        settings.GameHub.CustomSounds ??= new Dictionary<string, string>();
        settings.GameHub.AutoStartLaunchers ??= new List<string>();
        settings.GameHub.ControllerGlyphs = settings.GameHub.ControllerGlyphs is "Xbox" or "PlayStation"
            ? settings.GameHub.ControllerGlyphs
            : "Auto";
        if (!Enum.IsDefined(settings.GameHub.CardSize))
            settings.GameHub.CardSize = Models.GameHub.CardSize.Medium;

        var appearance = settings.Appearance;
        var defaults = new AppearanceSettings();
        appearance.PrimaryColor = string.IsNullOrWhiteSpace(appearance.PrimaryColor) ? defaults.PrimaryColor : appearance.PrimaryColor;
        appearance.SecondaryColor = string.IsNullOrWhiteSpace(appearance.SecondaryColor) ? defaults.SecondaryColor : appearance.SecondaryColor;
        appearance.AccentColor = string.IsNullOrWhiteSpace(appearance.AccentColor) ? defaults.AccentColor : appearance.AccentColor;
        appearance.BackgroundColor = string.IsNullOrWhiteSpace(appearance.BackgroundColor) ? defaults.BackgroundColor : appearance.BackgroundColor;
        appearance.GradientStart = string.IsNullOrWhiteSpace(appearance.GradientStart) ? defaults.GradientStart : appearance.GradientStart;
        appearance.GradientEnd = string.IsNullOrWhiteSpace(appearance.GradientEnd) ? defaults.GradientEnd : appearance.GradientEnd;
        appearance.Transparency = ClampFinite(appearance.Transparency, 0, 1, defaults.Transparency);
        appearance.BlurIntensity = ClampFinite(appearance.BlurIntensity, 0, 1, defaults.BlurIntensity);
        appearance.AnimationIntensity = ClampFinite(appearance.AnimationIntensity, 0, 1, defaults.AnimationIntensity);
        appearance.BackgroundOpacity = ClampFinite(appearance.BackgroundOpacity, 0, 1, defaults.BackgroundOpacity);
        appearance.BackgroundBlur = ClampFinite(appearance.BackgroundBlur, 0, 80, defaults.BackgroundBlur);
        appearance.BackgroundDarken = ClampFinite(appearance.BackgroundDarken, 0, 1, defaults.BackgroundDarken);
        appearance.BackgroundSaturation = ClampFinite(appearance.BackgroundSaturation, 0, 2, defaults.BackgroundSaturation);
        if (!Enum.IsDefined(appearance.Background)) appearance.Background = defaults.Background;
        if (!Enum.IsDefined(appearance.BackgroundFit)) appearance.BackgroundFit = defaults.BackgroundFit;

        settings.OemCommands.Commands ??= new Dictionary<string, string>();
        return settings;
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_settingsFilePath, json);
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
