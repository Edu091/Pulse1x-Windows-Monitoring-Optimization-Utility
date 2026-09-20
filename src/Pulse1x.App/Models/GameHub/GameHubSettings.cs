using System.Text.Json.Serialization;

namespace Pulse1x.App.Models.GameHub;

/// <summary>Tamanho dos cartões na biblioteca.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardSize { Small, Medium, Large }

/// <summary>
/// Preferências do GameHub. Ficam separadas das configurações gerais do Pulse1x porque o GameHub
/// tem identidade e comportamento próprios — inclusive a opção de o app abrir direto nele.
/// </summary>
public class GameHubSettings
{
    /// <summary>Abrir o Pulse1x já no GameHub, em tela cheia, em vez do Dashboard.</summary>
    public bool StartInGameHub { get; set; }

    /// <summary>O GameHub ocupa a janela inteira (sem a navegação lateral do Pulse1x).</summary>
    public bool Immersive { get; set; } = true;

    /// <summary>Maximizar a janela ao entrar no GameHub.</summary>
    public bool MaximizeOnOpen { get; set; } = true;

    // ---- Som ----
    public bool SoundEnabled { get; set; } = true;
    public double SoundVolume { get; set; } = 0.35;
    /// <summary>Arquivos .wav escolhidos pelo usuário por evento (vazio = som padrão do Pulse1x).</summary>
    public Dictionary<string, string> CustomSounds { get; set; } = new();

    // ---- Biblioteca ----
    public CardSize CardSize { get; set; } = CardSize.Medium;

    public string ControllerGlyphs { get; set; } = "Auto";

    /// <summary>Procurar capas na internet pelo nome quando não houver arte local.</summary>
    public bool OnlineArtEnabled { get; set; } = true;

    /// <summary>Varrer as fontes automaticamente ao abrir o GameHub.</summary>
    public bool ScanOnOpen { get; set; }

    /// <summary>Mostrar o nome sobre a capa nos cartões.</summary>
    public bool ShowTitlesOnCards { get; set; } = true;

    /// <summary>Registrar horas, FPS e demais estatísticas de uso.</summary>
    public bool MetricsEnabled { get; set; } = true;

    // Each data group can be disabled independently. Results remain in the local GameHub history.
    public bool MetricsPlaytimeEnabled { get; set; } = true;
    public bool MetricsFpsEnabled { get; set; } = true;
    public bool MetricsTemperaturesEnabled { get; set; } = true;
    public bool MetricsHardwareUsageEnabled { get; set; } = true;
    public bool MetricsMemoryEnabled { get; set; } = true;

    /// <summary>
    /// Launchers abertos junto com o GameHub (nomes de LauncherKind). Serve para o cliente já estar
    /// de pé quando o usuário apertar Jogar, em vez de esperar a loja subir na hora.
    /// </summary>
    public List<string> AutoStartLaunchers { get; set; } = new();
}
