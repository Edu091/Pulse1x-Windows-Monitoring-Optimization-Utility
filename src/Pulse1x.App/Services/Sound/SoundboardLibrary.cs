using System.IO;

namespace Pulse1x.App.Services.Sound;

/// <summary>Um som embutido da mesa: gravação real do Freesound, licença Creative Commons 0.</summary>
public sealed record SoundboardTrack(string Key, string Title, string Author, int FreesoundId)
{
    public string PageUrl => $"https://freesound.org/people/{Author}/sounds/{FreesoundId}/";
}

/// <summary>
/// Biblioteca embutida da mesa de sons: 22 gravações reais do Freesound, todas em domínio público
/// (CC0, licença conferida na página de cada som — ver Resources/Soundboard/CREDITS.md). Os MP3
/// viajam dentro do .exe. Memes de verdade não entram aqui: têm direitos de terceiros; o usuário os
/// baixa e arrasta para a mesa (ver SoundboardService.Import).
///
/// A ordem define os atalhos: os nove primeiros ganham Ctrl+Alt+1 a 9.
/// </summary>
public static class SoundboardLibrary
{
    public static readonly IReadOnlyList<SoundboardTrack> Tracks = new SoundboardTrack[]
    {
        new("fart", "Fart #005", "bassimat", 840755),
        new("scream", "JirkaScream 2", "Kalibrk", 339308),
        new("laugh", "Joker Laugh 2", "BrainClaim", 427573),
        new("airhorn", "Airhorn", "jacksonacademyashmore", 414208),
        new("sadtrombone", "wah wah sad trombone.wav", "kirbydx", 175409),
        new("rimshot", "Rimshot 2.wav", "Sorinious_Genious", 573258),
        new("crickets", "cricket chirping (slow)", "_sinny_", 822935),
        new("applause", "APPLAUSE.WAV", "erkanozan", 51743),
        new("wrong", "Wrong Answer", "-Andreas", 648462),
        new("correct", "Correct Answer / That's Right!", "Beetlemuse", 528957),
        new("explosion", "Explosion Test", "Apenguin73", 335152),
        new("boing", "Funny Boing", "se2001", 543641),
        new("siren", "European Police siren 1", "chungus43A", 819894),
        new("gunshot", "Heathers Gunshot Effect2.wav", "okieactor", 415912),
        new("burp", "The Best Burp Sample On The Internet", "scooterfort", 570266),
        new("kiss", "perfect_kiss.mp3", "gflower", 179303),
        new("boo", "Voice_Crowd_Small_Expression_Boo_Stereo", "Nox_Sound", 752707),
        new("wow", "kids_WOAH.wav", "DesignDean", 397317),
        new("drumroll", "Christmas Drum Roll.wav", "Teaganbear", 672596),
        new("bark", "Animal Dog Bark And Growl 01.wav", "abhisheky948", 625501),
        new("meow", "cat meow", "tuberatanka", 110011),
        new("quack", "Ducks Quacking To Each Other", "OwennewO", 719115),
    };

    public static IEnumerable<string> Keys => Tracks.Select(t => t.Key);

    /// <summary>O MP3 embutido do som (o chamador descarta o stream).</summary>
    public static Stream Open(string key) =>
        typeof(SoundboardLibrary).Assembly.GetManifestResourceStream($"Pulse1x.App.Soundboard.{key}.mp3")
        ?? throw new FileNotFoundException($"Som embutido não encontrado: {key}");
}
