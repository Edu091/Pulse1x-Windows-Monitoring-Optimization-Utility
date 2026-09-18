using System.IO;
using System.Text.Json;
using Pulse1x.App.Models.GameHub;

namespace Pulse1x.App.Services.GameHub;

/// <summary>
/// Guarda em disco o estado do sistema capturado antes de um perfil ser aplicado.
///
/// O arquivo é gravado ANTES da primeira alteração e só é apagado quando a restauração termina.
/// Essa ordem é o que garante a promessa do GameHub: se o jogo travar, se o Pulse1x for encerrado
/// ou se a máquina desligar no meio, na próxima abertura o app encontra o arquivo, entende que
/// ficou uma sessão pendente e devolve tudo ao que era antes.
/// </summary>
public class SnapshotService
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SnapshotService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse1x");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gamehub-snapshot.json");
    }

    /// <summary>Grava (ou regrava) o snapshot da sessão em andamento.</summary>
    public void Persist(SystemSnapshot snapshot)
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOptions)); }
        catch { /* sem disco/permissão: a restauração em memória ainda funciona nesta sessão */ }
    }

    /// <summary>Lê o snapshot pendente deixado por uma sessão que não terminou normalmente.</summary>
    public SystemSnapshot? LoadPending()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            var snapshot = JsonSerializer.Deserialize<SystemSnapshot>(File.ReadAllText(_filePath));
            return snapshot is { Restored: false } ? snapshot : null;
        }
        catch
        {
            // Snapshot ilegível não pode virar um bloqueio permanente na abertura do app.
            Clear();
            return null;
        }
    }

    /// <summary>Apaga o snapshot — chamado só depois que a restauração termina.</summary>
    public void Clear()
    {
        try { if (File.Exists(_filePath)) File.Delete(_filePath); }
        catch { }
    }

    public bool HasPending => LoadPending() is not null;
}
