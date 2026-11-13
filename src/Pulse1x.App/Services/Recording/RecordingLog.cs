using System.IO;

namespace Pulse1x.App.Services.Recording;

/// <summary>
/// Diário da Gravação em %LOCALAPPDATA%\Pulse1x\recording.log: ffmpeg encontrado, resultado do teste
/// de cada encoder, comandos usados e o fim da saída do ffmpeg quando algo falha. É o que permite
/// descobrir por que uma gravação não começou em outro PC. Nunca lança.
/// </summary>
public static class RecordingLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse1x", "recording.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                {
                    // Mantém só a metade mais recente.
                    var lines = File.ReadAllLines(FilePath);
                    File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
