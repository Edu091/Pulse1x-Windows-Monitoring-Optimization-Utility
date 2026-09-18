using System.IO;
using System.Windows.Media;

namespace Pulse1x.App.Services.GameHub;

/// <summary>Cada momento da navegação que merece um som.</summary>
public enum HubSound
{
    /// <summary>Mover a seleção de um jogo para outro.</summary>
    Navigate,
    /// <summary>Confirmar/entrar.</summary>
    Confirm,
    /// <summary>Voltar/cancelar.</summary>
    Back,
    /// <summary>Iniciar um jogo — o som mais marcante dos três.</summary>
    Launch,
}

/// <summary>
/// Sons curtos de navegação do GameHub, no espírito de um console.
///
/// A reprodução usa o <see cref="MediaPlayer"/> do WPF, e não o SoundPlayer do .NET. O motivo é
/// prático: o SoundPlayer não tem controle de volume nenhum (obrigava a assar a amplitude dentro do
/// próprio arquivo .wav), toca pelo caminho de "sons do sistema" — que o mixer do Windows silencia
/// junto com os bipes do sistema — e um Play() cancela o anterior. O MediaPlayer tem volume de
/// verdade, toca pelo caminho normal de áudio do aplicativo e permite um player por evento.
///
/// Os tons padrão continuam sendo sintetizados pelo próprio Pulse1x e gravados no cache
/// (%APPDATA%\Pulse1x\gamehub\sounds) — sem arquivos binários no repositório e funcionando offline.
/// Quem preferir aponta um .wav próprio para cada evento nas Configurações.
/// </summary>
public class GameHubSoundService
{
    private readonly string _soundDir;

    /// <summary>Um player por evento, aberto uma vez e reposicionado a cada toque.</summary>
    private readonly Dictionary<HubSound, MediaPlayer> _players = new();

    /// <summary>
    /// Versão do gerador. Os tons ficam gravados em disco, então mudar a síntese não teria efeito
    /// em quem já tem os arquivos antigos; quando esta versão muda, os padrões são recriados.
    /// </summary>
    private const int GeneratorVersion = 4;

    /// <summary>Ligado/desligado (preferência do usuário).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Volume de 0 a 1, aplicado na reprodução — não precisa recriar arquivo nenhum.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            foreach (var player in _players.Values)
            {
                try { player.Volume = _volume; } catch { }
            }
        }
    }
    private double _volume = 0.5;

    /// <summary>Caminhos definidos pelo usuário (evento → arquivo .wav). Vazio = som padrão.</summary>
    public Dictionary<string, string> CustomSounds { get; set; } = new();

    public GameHubSoundService()
    {
        _soundDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse1x", "gamehub", "sounds");
        Directory.CreateDirectory(_soundDir);

        EnsureCurrentGeneration();
    }

    public string SoundDirectory => _soundDir;

    /// <summary>Apaga os tons de uma geração anterior do sintetizador.</summary>
    private void EnsureCurrentGeneration()
    {
        try
        {
            string marker = Path.Combine(_soundDir, "generator.txt");
            string expected = GeneratorVersion.ToString();
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == expected) return;

            foreach (HubSound sound in Enum.GetValues<HubSound>())
            {
                string path = Path.Combine(_soundDir, $"{sound.ToString().ToLowerInvariant()}.wav");
                if (File.Exists(path)) File.Delete(path);
            }

            File.WriteAllText(marker, expected);
        }
        catch { /* sem permissão: os sons existentes continuam valendo */ }
    }

    // =====================================================================================
    //  Reprodução
    // =====================================================================================

    /// <summary>
    /// Toca o som do evento. Precisa ser chamado na thread de interface (o MediaPlayer pertence a
    /// ela); todas as chamadas do GameHub já vêm de lá. Nunca lança: áudio é enfeite.
    /// </summary>
    public void Play(HubSound sound)
    {
        if (!Enabled || Volume <= 0.01) return;

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Play(sound)));
                return;
            }

            var player = GetPlayer(sound);
            if (player is null) return;

            // Rebobinar e tocar: com o arquivo já aberto, o toque é imediato.
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch { }
    }

    private MediaPlayer? GetPlayer(HubSound sound)
    {
        if (_players.TryGetValue(sound, out var cached)) return cached;

        string? path = ResolvePath(sound);
        if (path is null || !File.Exists(path)) return null;

        try
        {
            var player = new MediaPlayer { Volume = Volume };
            player.Open(new Uri(path, UriKind.Absolute));
            _players[sound] = player;
            return player;
        }
        catch { return null; }
    }

    /// <summary>Arquivo do evento: o do usuário, se houver; senão o padrão (gerado sob demanda).</summary>
    private string? ResolvePath(HubSound sound)
    {
        if (CustomSounds.TryGetValue(sound.ToString(), out var custom) &&
            !string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            return custom;

        string path = Path.Combine(_soundDir, $"{sound.ToString().ToLowerInvariant()}.wav");
        if (File.Exists(path)) return path;

        return GenerateDefault(sound, path) ? path : null;
    }

    /// <summary>Descarta os players (ao trocar os arquivos personalizados).</summary>
    public void Reset()
    {
        foreach (var player in _players.Values)
        {
            try { player.Close(); } catch { }
        }
        _players.Clear();
    }

    /// <summary>Apaga os tons padrão para serem recriados.</summary>
    public void RegenerateDefaults()
    {
        Reset();
        foreach (HubSound sound in Enum.GetValues<HubSound>())
        {
            try
            {
                string path = Path.Combine(_soundDir, $"{sound.ToString().ToLowerInvariant()}.wav");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }

    /// <summary>Volta um evento ao som padrão do Pulse1x.</summary>
    public void ClearCustom(HubSound sound)
    {
        CustomSounds.Remove(sound.ToString());
        Reset();
    }

    // =====================================================================================
    //  Síntese
    // =====================================================================================

    /// <summary>
    /// Desenha o tom do evento e o grava como WAV PCM 16 bits / 44,1 kHz mono.
    ///
    /// O desenho é deliberadamente discreto, no espírito da interface do PlayStation: um toque macio
    /// que se percebe sem chamar atenção, em vez de um bipe eletrônico. Três decisões fazem essa
    /// diferença:
    ///
    ///  • <b>Senoide pura</b>, sem o harmônico de oitava que o tom anterior usava — era ele que dava
    ///    o timbre "8 bits" estridente.
    ///  • <b>Frequência fixa e grave</b> em cada som (nada de varredura subindo/descendo, que soa
    ///    como alarme) e na região de 400–600 Hz, confortável para repetir centenas de vezes.
    ///  • <b>Envelope bem suave</b>: ataque de 15% e queda exponencial longa, o que transforma o
    ///    som num "toc" arredondado em vez de um clique seco.
    ///
    /// A amplitude também é modesta (30% do máximo) — o controle de volume das Configurações ainda
    /// governa por cima, mas o ponto de partida já é baixo.
    /// </summary>
    private static bool GenerateDefault(HubSound sound, string path)
    {
        try
        {
            // (frequência em Hz, duração em ms, volume relativo)
            // As notas formam um acorde simples entre si, então os sons convivem bem em sequência.
            (double frequency, int ms, double level) = sound switch
            {
                HubSound.Navigate => (440.0, 55, 0.30),   // lá — curtíssimo, quase um toque
                HubSound.Confirm => (587.33, 90, 0.38),   // ré acima — um pouco mais claro
                HubSound.Back => (349.23, 90, 0.32),      // fá abaixo — mais grave, sensação de voltar
                _ => (523.25, 260, 0.42),                 // dó — o único com alguma presença
            };

            const int sampleRate = 44100;
            int sampleCount = sampleRate * ms / 1000;
            var samples = new short[sampleCount];

            double amplitude = short.MaxValue * level;

            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / sampleCount;

                // Ataque suave (15% do tempo) e queda exponencial: sem estalo no início nem corte
                // abrupto no fim, que é o que fazia o som parecer "quebrado".
                double attack = t < 0.15 ? t / 0.15 : 1.0;
                double decay = Math.Exp(-3.2 * t);
                double envelope = attack * decay;

                // Senoide pura — o timbre mais macio possível.
                double value = Math.Sin(2 * Math.PI * frequency * i / sampleRate);

                samples[i] = (short)Math.Clamp(value * envelope * amplitude, short.MinValue, short.MaxValue);
            }

            WriteWav(path, samples, sampleRate);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Grava um WAV PCM mono de 16 bits (cabeçalho RIFF mínimo).</summary>
    private static void WriteWav(string path, short[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        int dataBytes = samples.Length * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                          // tamanho do bloco fmt
        writer.Write((short)1);                    // PCM
        writer.Write((short)1);                    // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);              // bytes por segundo
        writer.Write((short)2);                    // alinhamento do bloco
        writer.Write((short)16);                   // bits por amostra

        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (short sample in samples) writer.Write(sample);
    }
}
