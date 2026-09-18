using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.GameHub;

/// <summary>
/// Fotografia do estado do sistema tirada ANTES de qualquer alteração de um perfil. É gravada em
/// disco (gamehub-snapshot.json) assim que é criada, para que a restauração funcione mesmo se o
/// Pulse1x ou o jogo for encerrado inesperadamente: na próxima abertura o app encontra o arquivo,
/// percebe que ficou uma sessão pendente e devolve o computador ao estado anterior.
///
/// Só é preenchido o que o perfil realmente vai mudar — o que não foi tocado fica null e a
/// restauração ignora.
/// </summary>
public class SystemSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>Jogo/app que originou a sessão (para a mensagem de recuperação).</summary>
    public string? GameId { get; set; }
    public string? GameName { get; set; }
    public string? ProfileId { get; set; }

    // ---- Energia ----
    public string? PowerPlanGuid { get; set; }
    public string? PowerPlanName { get; set; }
    /// <summary>Valores originais das configurações avançadas alteradas, no plano que estava ativo.</summary>
    public List<PowerSettingValue> PowerSettings { get; set; } = new();

    // ---- Fabricante (OEM) ----
    public string? OemVendorId { get; set; }
    public string? OemModeId { get; set; }

    // ---- Áudio ----
    public int? Volume { get; set; }
    public bool? Muted { get; set; }
    public string? DefaultOutputDeviceId { get; set; }
    public string? DefaultInputDeviceId { get; set; }

    // ---- Tela ----
    public int? Brightness { get; set; }
    public int? RefreshRate { get; set; }
    public bool? Hdr { get; set; }
    public string? DisplayDevice { get; set; }

    // ---- Latência ----
    /// <summary>Resolução do timer (ms) antes da alteração; a restauração apenas libera o pedido.</summary>
    public double? TimerResolutionMs { get; set; }

    // ---- Processos ----
    /// <summary>Apps que o perfil fechou, com o caminho para reabrir.</summary>
    public List<ClosedProcessInfo> ClosedProcesses { get; set; } = new();
    /// <summary>Apps que o perfil abriu junto ao jogo e devem ser fechados no fim.</summary>
    public List<int> StartedProcessIds { get; set; } = new();

    /// <summary>Marcado quando a restauração termina; o arquivo é então removido.</summary>
    public bool Restored { get; set; }

    /// <summary>Etapas que falharam ao aplicar (só para diagnóstico na interface).</summary>
    public List<string> Warnings { get; set; } = new();

    [JsonIgnore]
    public bool HasAnything =>
        PowerPlanGuid is not null || PowerSettings.Count > 0 || OemModeId is not null ||
        Volume is not null || Muted is not null || DefaultOutputDeviceId is not null ||
        DefaultInputDeviceId is not null || Brightness is not null || RefreshRate is not null ||
        Hdr is not null || TimerResolutionMs is not null ||
        ClosedProcesses.Count > 0 || StartedProcessIds.Count > 0;
}

/// <summary>Processo fechado por um perfil, com o necessário para reabri-lo depois.</summary>
public class ClosedProcessInfo
{
    public string ProcessName { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public int Count { get; set; } = 1;
}
