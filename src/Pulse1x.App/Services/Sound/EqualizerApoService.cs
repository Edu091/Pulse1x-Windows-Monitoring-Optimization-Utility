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
        bool hasInclude = current.Split('\n').Any(l => l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase));

        // Mesmo com a linha já presente, o resto pode ser o exemplo de fábrica (a 1.17.0 não
        // reconhecia o GraphicEQ zerado da 1.4.2 e o deixava ativo: -6 dB em todos os aparelhos).
        if (IsFactoryConfig(current))
        {
            if (hasInclude && current.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase)) return;
            if (current.Length > 0 && !File.Exists(config + ".pulse1x-backup"))
            {
                try { File.Copy(config, config + ".pulse1x-backup"); } catch { }
            }
            AtomicFile.WriteAllText(config, IncludeLine + Environment.NewLine);
            return;
        }
        if (hasInclude) return;

        string separator = current.Length == 0 || current.EndsWith('\n') ? "" : Environment.NewLine;
        AtomicFile.WriteAllText(config, current + separator + IncludeLine + Environment.NewLine);
    }

    /// <summary>
    /// Instala o plugin de voz do Pulse1x (VST2, embutido no .exe) na pasta VSTPlugins do motor e
    /// devolve o caminho completo da DLL. O nome leva
    /// o hash do conteúdo: o motor mantém a DLL aberta dentro do serviço de áudio, então uma versão
    /// nova nunca precisa sobrescrever a antiga em uso. Devolve null se não der para instalar.
    /// </summary>
    public string? EnsureVoicePlugin()
    {
        if (InstallPath is not string install) return null;
        try
        {
            using var stream = typeof(EqualizerApoService).Assembly
                .GetManifestResourceStream("Pulse1x.App.Native.Pulse1xVoice.dll");
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..10].ToLowerInvariant();
            string name = $"Pulse1xVoice-{hash}.dll";

            string dir = Path.Combine(install, "VSTPlugins", "Pulse1x");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                File.WriteAllBytes(path, bytes);

            // Versões antigas: apaga as que não estiverem mais carregadas pelo serviço de áudio.
            foreach (var old in Directory.GetFiles(dir, "Pulse1xVoice-*.dll"))
            {
                if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(old); } catch { }
            }
            // Caminho absoluto: verificado no log do motor ("Loaded library ...") e no áudio real.
            return path;
        }
        catch { return null; }
    }

    // =====================================================================================
    //  Apps de chamada (modo Comunicações)
    // =====================================================================================

    // Modos de processamento de áudio do Windows (AUDIO_SIGNALPROCESSINGMODE_*).
    private const string ModeDefault = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
    private const string ModeCommunications = "{98951333-B9CD-48B1-A0A3-FF40682D73F7}";
    private const string ModeSpeech = "{FC1CFC9B-B9D6-4CFA-B5E0-4BB2166878B2}";

    // FxProperties: CLSID do efeito de stream (SFX) e a lista de modos em que ele roda.
    private const string SfxClsidValue = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5";
    private const string SfxModesValue = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5";
    private const string BackupKey = @"SOFTWARE\Pulse1x\SoundFxBackup";

    /// <summary>
    /// O efeito chega aos apps de chamada? Discord, WhatsApp, Teams e Meet abrem o microfone no modo
    /// "Comunicações"; o Equalizer APO, por padrão, só se registra no modo padrão — então o teste do
    /// Pulse1x ouvia o efeito e a call não. true = já vale nas calls; false = só no modo padrão;
    /// null = não se aplica (motor instalado de outra forma, ou sem permissão de leitura).
    /// </summary>
    public bool? CallModesEnabled(string? endpointId)
    {
        try
        {
            using var fx = OpenFx(endpointId, writable: false);
            if (fx?.GetValue(SfxClsidValue) is not string clsid ||
                !clsid.Equals(ApoClsid, StringComparison.OrdinalIgnoreCase)) return null;
            var modes = fx.GetValue(SfxModesValue) as string[] ?? Array.Empty<string>();
            return modes.Contains(ModeCommunications, StringComparer.OrdinalIgnoreCase);
        }
        catch { return null; }
    }

    /// <summary>
    /// Registra o efeito também nos modos Comunicações e Speech do microfone (o modo Raw continua
    /// sem processamento, como deve ser) e reinicia o serviço de áudio para valer na hora. A lista
    /// original fica guardada para <see cref="RestoreCallModes"/>.
    /// </summary>
    public void EnableCallModes(string endpointId)
    {
        using (var fx = OpenFx(endpointId, writable: true) ?? throw new InvalidOperationException("Dispositivo não encontrado."))
        {
            var current = fx.GetValue(SfxModesValue) as string[] ?? new[] { ModeDefault };
            using (var backup = Registry.LocalMachine.CreateSubKey(BackupKey))
            {
                string name = EqualizerApoConfig.ExtractGuid(endpointId)!;
                if (backup.GetValue(name) is null)
                    backup.SetValue(name, current, RegistryValueKind.MultiString);
            }
            var wanted = current.Concat(new[] { ModeDefault, ModeCommunications, ModeSpeech })
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            fx.SetValue(SfxModesValue, wanted, RegistryValueKind.MultiString);
        }
        RestartAudioService();
    }

    /// <summary>Devolve a lista de modos original (se o Pulse1x a tiver mudado) e reinicia o áudio.</summary>
    public bool RestoreCallModes(string? endpointId)
    {
        string? guid = EqualizerApoConfig.ExtractGuid(endpointId);
        if (guid is null || endpointId is null) return false;
        using var backup = Registry.LocalMachine.OpenSubKey(BackupKey, writable: true);
        if (backup?.GetValue(guid) is not string[] original) return false;
        using (var fx = OpenFx(endpointId, writable: true))
        {
            if (fx is null) return false;
            fx.SetValue(SfxModesValue, original, RegistryValueKind.MultiString);
        }
        backup.DeleteValue(guid, throwOnMissingValue: false);
        RestartAudioService();
        return true;
    }

    // =====================================================================================
    //  Modo universal (efeito de endpoint)
    // =====================================================================================

    // Efeito de endpoint (EFX): CLSID único, lista composta (Windows 11) e modos.
    private const string EfxClsidValue = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";
    private const string EfxCompositeValue = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},15";
    private const string EfxModesValue = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";
    private const string AbsentMarker = "__absent__";

    private static readonly string[] UniversalTouched =
        { SfxClsidValue, SfxModesValue, EfxClsidValue, EfxCompositeValue, EfxModesValue };

    /// <summary>
    /// O motor roda como efeito de endpoint? Segundo a Microsoft, "um efeito de endpoint é sempre
    /// aplicado, mesmo a streams raw" — então ele vale para qualquer app, inclusive o subsistema de
    /// áudio experimental do Discord, que captura em modo Raw e pula os efeitos de stream (SFX).
    /// null = o motor não está neste dispositivo.
    /// </summary>
    public bool? UniversalEnabled(string? endpointId)
    {
        try
        {
            using var fx = OpenFx(endpointId, writable: false);
            if (fx is null) return null;
            bool inComposite = (fx.GetValue(EfxCompositeValue) as string[] ?? Array.Empty<string>())
                .Contains(ApoClsid, StringComparer.OrdinalIgnoreCase);
            bool inSingle = string.Equals(fx.GetValue(EfxClsidValue) as string, ApoClsid, StringComparison.OrdinalIgnoreCase);
            if (inComposite || inSingle) return true;
            return string.Equals(fx.GetValue(SfxClsidValue) as string, ApoClsid, StringComparison.OrdinalIgnoreCase) ? false : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Move o motor de efeito de stream (SFX, pulado pelo modo Raw) para efeito de endpoint (EFX,
    /// sempre aplicado). Ele sai do SFX para não processar a voz duas vezes. Se o driver já tem uma
    /// lista composta de EFX (Windows 11), o motor entra no fim dela, depois do efeito do driver.
    /// Todos os valores tocados ficam guardados para <see cref="DisableUniversal"/>.
    /// </summary>
    public void EnableUniversal(string endpointId)
    {
        string guid = EqualizerApoConfig.ExtractGuid(endpointId) ?? throw new ArgumentException("Id inválido.");
        using (var fx = OpenFx(endpointId, writable: true) ?? throw new InvalidOperationException("Dispositivo não encontrado."))
        {
            using (var backup = Registry.LocalMachine.CreateSubKey($@"{BackupKey}\{guid}"))
            {
                if (backup.GetValue("Saved") is null)
                {
                    var absent = new List<string>();
                    foreach (var name in UniversalTouched)
                    {
                        object? value = fx.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (value is null) { absent.Add(name); continue; }
                        backup.SetValue(name, value, fx.GetValueKind(name));
                    }
                    backup.SetValue(AbsentMarker, absent.ToArray(), RegistryValueKind.MultiString);
                    backup.SetValue("Saved", 1, RegistryValueKind.DWord);
                }
            }

            if (fx.GetValue(EfxCompositeValue) is string[] composite)
            {
                if (!composite.Contains(ApoClsid, StringComparer.OrdinalIgnoreCase))
                    fx.SetValue(EfxCompositeValue, composite.Append(ApoClsid).ToArray(), RegistryValueKind.MultiString);
            }
            else
            {
                fx.SetValue(EfxClsidValue, ApoClsid, RegistryValueKind.String);
            }
            if (fx.GetValue(EfxModesValue) is null)
                fx.SetValue(EfxModesValue, new[] { ModeDefault }, RegistryValueKind.MultiString);

            if (string.Equals(fx.GetValue(SfxClsidValue) as string, ApoClsid, StringComparison.OrdinalIgnoreCase))
                fx.DeleteValue(SfxClsidValue, throwOnMissingValue: false);
        }
        RestartAudioService();
    }

    /// <summary>Devolve exatamente o registro de antes do modo universal e reinicia o áudio.</summary>
    public bool DisableUniversal(string? endpointId)
    {
        string? guid = EqualizerApoConfig.ExtractGuid(endpointId);
        if (guid is null || endpointId is null) return false;
        using var backup = Registry.LocalMachine.OpenSubKey($@"{BackupKey}\{guid}");
        if (backup?.GetValue("Saved") is null) return false;
        using (var fx = OpenFx(endpointId, writable: true))
        {
            if (fx is null) return false;
            var absent = backup.GetValue(AbsentMarker) as string[] ?? Array.Empty<string>();
            foreach (var name in UniversalTouched)
            {
                if (absent.Contains(name)) { fx.DeleteValue(name, throwOnMissingValue: false); continue; }
                object? value = backup.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is not null) fx.SetValue(name, value, backup.GetValueKind(name));
            }
        }
        backup.Close();
        Registry.LocalMachine.DeleteSubKeyTree($@"{BackupKey}\{guid}", throwOnMissingSubKey: false);
        RestartAudioService();
        return true;
    }

    internal static RegistryKey? OpenFx(string? endpointId, bool writable)
    {
        string? guid = EqualizerApoConfig.ExtractGuid(endpointId);
        if (guid is null || endpointId is null) return null;
        string flow = endpointId.StartsWith("{0.0.1.", StringComparison.Ordinal) ? "Capture" : "Render";
        string path = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{flow}\{guid}\FxProperties";
        if (!writable) return Registry.LocalMachine.OpenSubKey(path);

        // Esta chave só dá a Administradores "ler e gravar valores". OpenSubKey(path, true) pede
        // também criar subchaves e o Windows nega o acesso inteiro (SecurityException): aqui se
        // pede só o que a chave permite.
        return Registry.LocalMachine.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree,
            System.Security.AccessControl.RegistryRights.QueryValues | System.Security.AccessControl.RegistryRights.SetValue);
    }

    /// <summary>
    /// Reinicia o serviço de áudio do Windows para ele reler os efeitos do dispositivo. O som some
    /// por 1 ou 2 segundos; Discord e afins reconectam o microfone sozinhos.
    /// </summary>
    private static void RestartAudioService()
    {
        using var proc = Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Restart-Service -Name audiosrv -Force\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        proc?.WaitForExit(30000);
    }

    /// <summary>Grava um arquivo auxiliar (resposta ao impulso dos efeitos) na pasta do motor.</summary>
    public void WriteAuxFile(string name, byte[] content)
    {
        string dir = ConfigPath ?? throw new InvalidOperationException("Equalizer APO não instalado.");
        string path = Path.Combine(dir, name);
        if (File.Exists(path) && new FileInfo(path).Length == content.Length) return;
        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// config.txt vazio ou apenas com o exemplo de fábrica: "Preamp: -6 dB", "Include: example.txt"
    /// e o GraphicEQ zerado que o instalador 1.4 acrescenta. A nossa própria linha de Include não
    /// conta — o que importa é se sobra alguma configuração feita pelo usuário.
    /// </summary>
    public static bool IsFactoryConfig(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Equals("Preamp: -6 dB", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Equals("Include: example.txt", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Equals(IncludeLine, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsFlatGraphicEq(line)) continue;
            return false;
        }
        return true;
    }

    private static bool IsFlatGraphicEq(string line)
    {
        if (!line.StartsWith("GraphicEQ:", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var band in line["GraphicEQ:".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = band.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double gain) || Math.Abs(gain) > 0.001)
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
