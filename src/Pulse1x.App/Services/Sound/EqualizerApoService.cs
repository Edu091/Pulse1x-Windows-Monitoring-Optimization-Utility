using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Pulse1x.App.Services.Sound;

/// <summary>
/// Integração com o Equalizer APO, o motor de áudio de código aberto (GPL) que roda dentro do
/// próprio mecanismo de som do Windows. É ele que permite o que o Windows sozinho não faz: volume
/// do microfone acima de 100%, equalizador, graves, som 3D e o modificador de voz valendo em TODOS
/// os apps (Discord, WhatsApp, jogos), sem criar microfone virtual nem trocar o dispositivo neles.
///
/// O Pulse1x não mexe nos arquivos do usuário: escreve só o próprio pulse1x.txt e acrescenta uma
/// linha "Include: pulse1x.txt" no config.txt. O motor recarrega a configuração sozinho quando o
/// arquivo muda, então os ajustes valem na hora.
/// </summary>
public class EqualizerApoService
{
    public const string DownloadPage = "https://sourceforge.net/projects/equalizerapo/files/";
    public const string IncludeFileName = "pulse1x.txt";
    private const string IncludeLine = "Include: " + IncludeFileName;
    private const string RegistryPath = @"SOFTWARE\EqualizerAPO";

    // CLSID do APO do Equalizer APO, gravado nas FxProperties dos dispositivos em que ele foi ativado.
    private const string ApoClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";

    /// <summary>Pasta de instalação, ou null se o Equalizer APO não estiver instalado.</summary>
    public string? InstallPath
    {
        get
        {
            string? path = ReadRegistry("InstallPath");
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
            return File.Exists(Path.Combine(fallback, "EqualizerAPO.dll")) ? fallback : null;
        }
    }

    public bool IsInstalled => InstallPath is not null;

    /// <summary>Pasta onde o motor lê o config.txt.</summary>
    public string? ConfigPath
    {
        get
        {
            string? path = ReadRegistry("ConfigPath");
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
            return InstallPath is string install ? Path.Combine(install, "config") : null;
        }
    }

    /// <summary>
    /// O motor foi ativado neste dispositivo (no Device Selector/Configurator)? Sem isso o arquivo
    /// de configuração existe, mas o Windows não passa o som daquele aparelho pelo motor.
    /// Devolve null quando não dá para saber (sem permissão de leitura, formato desconhecido).
    /// </summary>
    public bool? IsActiveOn(string? endpointId)
    {
        string? guid = EqualizerApoConfig.ExtractGuid(endpointId);
        if (guid is null || endpointId is null) return null;
        string flow = endpointId.StartsWith("{0.0.1.", StringComparison.Ordinal) ? "Capture" : "Render";

        try
        {
            using var fx = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{flow}\{guid}\FxProperties");
            if (fx is not null)
            {
                foreach (string name in fx.GetValueNames())
                {
                    if (fx.GetValue(name) is string s && s.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (fx.GetValue(name) is string[] multi &&
                        multi.Any(m => m.Contains(ApoClsid, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            // As versões do Equalizer APO guardam, por dispositivo, o APO original que substituíram.
            using var child = Registry.LocalMachine.OpenSubKey($@"{RegistryPath}\Child APOs\{guid}");
            if (child is not null) return true;
            using var childUpper = Registry.LocalMachine.OpenSubKey($@"{RegistryPath}\Child APOs\{guid.ToUpperInvariant()}");
            return childUpper is not null ? true : fx is null ? null : false;
        }
        catch { return null; }
    }

    /// <summary>
    /// Grava o pulse1x.txt e garante a linha de Include no config.txt. Se o config.txt ainda é o de
    /// fábrica (o exemplo que abaixa o volume em 6 dB e liga um filtro de demonstração), ele é
    /// guardado como config.txt.pulse1x-backup e substituído — senão o exemplo afetaria todos os
    /// dispositivos, inclusive o microfone.
    /// </summary>
    public void Write(string content)
    {
        string dir = ConfigPath ?? throw new InvalidOperationException("Equalizer APO não instalado.");
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(Path.Combine(dir, IncludeFileName), content);

        string config = Path.Combine(dir, "config.txt");
        string current = File.Exists(config) ? File.ReadAllText(config) : "";
        if (current.Split('\n').Any(l => l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase)))
            return;

        if (IsFactoryConfig(current))
        {
            if (current.Length > 0)
            {
                try { File.Copy(config, config + ".pulse1x-backup", overwrite: true); } catch { }
            }
            AtomicFile.WriteAllText(config, IncludeLine + Environment.NewLine);
        }
        else
        {
            string separator = current.Length == 0 || current.EndsWith('\n') ? "" : Environment.NewLine;
            AtomicFile.WriteAllText(config, current + separator + IncludeLine + Environment.NewLine);
        }
    }

    /// <summary>config.txt vazio ou apenas com o exemplo de fábrica (Preamp -6 dB + example.txt).</summary>
    public static bool IsFactoryConfig(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Equals("Preamp: -6 dB", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Equals("Include: example.txt", StringComparison.OrdinalIgnoreCase)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Abre a ferramenta do motor que ativa o processamento nos dispositivos.</summary>
    public bool OpenDeviceSelector()
    {
        if (InstallPath is not string dir) return false;
        foreach (var exe in new[] { "DeviceSelector.exe", "Configurator.exe" })
        {
            string path = Path.Combine(dir, exe);
            if (!File.Exists(path)) continue;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = dir });
                return true;
            }
            catch { }
        }
        return false;
    }

    public static void OpenDownloadPage()
    {
        try { Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true }); } catch { }
    }

    private static string? ReadRegistry(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            return key?.GetValue(name) as string;
        }
        catch { return null; }
    }
}
