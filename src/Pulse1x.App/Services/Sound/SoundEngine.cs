using System.Diagnostics;

namespace Pulse1x.App.Services.Sound;

/// <summary>Resultado de uma aplicação dos ajustes de som.</summary>
public sealed record SoundApplyResult(bool ApoWritten, string? ApoError);

/// <summary>
/// Aplica os ajustes da categoria Som: o volume do Windows no microfone, a amplificação de hardware
/// e o arquivo do motor de áudio (equalizador, graves, 3D, ganho acima de 100% e voz). É usado pela
/// página, pela trava de volume e pela inicialização com o Windows (Pulse1x.App.exe --apply-sound).
/// </summary>
public class SoundEngine
{
    /// <summary>Argumento da tarefa de logon que só reaplica o som e sai, sem abrir janela.</summary>
    public const string ApplyArgument = "--apply-sound";

    /// <summary>Nome da tarefa agendada (removida também pelo desinstalador).</summary>
    public const string StartupTaskName = "Pulse1x Som";

    public SoundDeviceService Devices { get; }
    public EqualizerApoService Apo { get; }

    public SoundEngine(SoundDeviceService devices, EqualizerApoService apo)
    {
        Devices = devices;
        Apo = apo;
    }

    /// <summary>Aplica o volume do microfone no Windows (a parte até 100%).</summary>
    public void ApplyMicLevel(SoundSettings s)
    {
        if (s.MicLevelPercent is not int level) return;
        string? mic = Devices.Resolve(s.InputDeviceId, input: true);
        if (mic is null) return;
        Devices.SetVolume(mic, SoundPresets.SplitMicLevel(level).WindowsPercent);
    }

    public void ApplyHardwareBoost(SoundSettings s)
    {
        if (s.MicHardwareBoostDb is not float db) return;
        string? mic = Devices.Resolve(s.InputDeviceId, input: true);
        if (mic is not null) Devices.SetHardwareBoost(mic, db);
    }

    /// <summary>Reescreve o pulse1x.txt do motor de áudio (se ele estiver instalado).</summary>
    public SoundApplyResult ApplyEffects(SoundSettings s)
    {
        if (!Apo.IsInstalled) return new SoundApplyResult(false, null);
        try
        {
            string? output = Devices.Resolve(s.OutputDeviceId, input: false);
            string? input = Devices.Resolve(s.InputDeviceId, input: true);

            // Efeitos com resposta ao impulso: o arquivo é gerado na taxa exata do microfone.
            int rate = 0;
            var voice = SoundPresets.FindVoice(s.VoiceEffect);
            if (s.MicEffectsEnabled && voice.Impulse is string impulse && input is not null)
            {
                rate = Devices.GetSampleRate(input) ?? 0;
                if (rate > 0)
                    Apo.WriteAuxFile(ImpulseResponses.FileName(impulse, rate),
                        ImpulseResponses.ToWav(ImpulseResponses.Generate(impulse, rate), rate));
            }

            // Plugin de voz (tom, robô, eco, ganho com limitador...): instalado na pasta do motor.
            string? plugin = Apo.EnsureVoicePlugin();

            string text = EqualizerApoConfig.Build(s,
                EqualizerApoConfig.ExtractGuid(output), EqualizerApoConfig.ExtractGuid(input), rate, plugin);
            Apo.Write(text);
            return new SoundApplyResult(true, null);
        }
        catch (Exception ex)
        {
            return new SoundApplyResult(false, ex.Message);
        }
    }

    public SoundApplyResult ApplyAll(SoundSettings s)
    {
        ApplyMicLevel(s);
        ApplyHardwareBoost(s);
        return ApplyEffects(s);
    }

    /// <summary>
    /// Trava de volume: se outro app (Discord e Teams fazem isso com o "ajuste automático") mudou o
    /// volume do microfone, devolve o valor escolhido. Devolve true quando precisou corrigir.
    /// </summary>
    public bool EnforceMicLevel(SoundSettings s)
    {
        if (!s.LockMicLevel || s.MicLevelPercent is not int level) return false;
        string? mic = Devices.Resolve(s.InputDeviceId, input: true);
        if (mic is null) return false;
        int wanted = SoundPresets.SplitMicLevel(level).WindowsPercent;
        int? current = Devices.GetVolume(mic);
        if (current is null || Math.Abs(current.Value - wanted) <= 1) return false;
        return Devices.SetVolume(mic, wanted);
    }

    // =====================================================================================
    //  Inicialização com o Windows
    // =====================================================================================

    /// <summary>
    /// Cria (ou remove) a tarefa de logon que reaplica o som. Como o "Iniciar com o Windows" do
    /// app, é uma tarefa agendada com privilégio máximo: a chave Run é ignorada para programas que
    /// exigem administrador, e a escrita no motor de áudio (em Arquivos de Programas) precisa dele.
    /// O atraso de 20 s dá tempo de fones Bluetooth e placas USB aparecerem.
    /// </summary>
    public static void SetStartupTask(bool enabled)
    {
        string args;
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            args = $"/Create /TN \"{StartupTaskName}\" /TR \"\\\"{exe}\\\" {ApplyArgument}\" /SC ONLOGON /DELAY 0000:20 /RU \"{user}\" /RL HIGHEST /IT /F";
        }
        else
        {
            args = $"/Delete /TN \"{StartupTaskName}\" /F";
        }

        using var proc = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        proc.WaitForExit(15000);
        if (enabled && proc.ExitCode != 0)
            throw new InvalidOperationException(proc.StandardError.ReadToEnd().Trim());
    }

    /// <summary>
    /// Execução sem janela, no logon: espera os dispositivos aparecerem (até ~90 s) e reaplica
    /// tudo. Se a trava de volume estiver ligada, fica de olho por mais alguns minutos, que é
    /// quando Discord e companhia costumam abrir e mexer no volume.
    /// </summary>
    public static void RunStartupApply()
    {
        var settings = new SettingsService().Current.Sound;
        if (!settings.ApplyOnStartup) return;

        var engine = new SoundEngine(new SoundDeviceService(), new EqualizerApoService());
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            bool micReady = settings.InputDeviceId is null || engine.Devices.List(true).Any(d => d.Id == settings.InputDeviceId);
            bool outReady = settings.OutputDeviceId is null || engine.Devices.List(false).Any(d => d.Id == settings.OutputDeviceId);
            if (micReady && outReady) break;
            Thread.Sleep(3000);
        }

        engine.ApplyAll(settings);

        if (!settings.LockMicLevel) return;
        var watchUntil = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < watchUntil)
        {
            Thread.Sleep(2000);
            engine.EnforceMicLevel(settings);
        }
    }
}
