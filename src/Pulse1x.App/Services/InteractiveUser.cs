using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Pulse1x.App.Services;

/// <summary>
/// A pessoa sentada diante do PC — não necessariamente a conta em que o Pulse1x está rodando.
///
/// O app exige administrador. Numa conta comum, o UAC pede a senha de OUTRO usuário (o
/// administrador) e o processo passa a rodar como ele: <see cref="Registry.CurrentUser"/> e o
/// AppData apontam então para o perfil do administrador. Otimizações por usuário (Copilot,
/// sugestões, Game DVR, aceleração de hardware do Discord/Spotify) eram gravadas na conta errada e
/// não mudavam nada para quem usa o computador.
///
/// O usuário interativo é o dono do Explorer da sessão atual. Quando ele é a própria conta do
/// processo (o caso comum), tudo continua exatamente como antes.
/// </summary>
public static class InteractiveUser
{
    private static readonly Lazy<string?> OtherSid = new(ResolveOtherUserSid);
    private static RegistryKey? _hive;

    /// <summary>SID do usuário interativo quando ele difere da conta do processo; null caso contrário.</summary>
    public static string? RedirectedSid => OtherSid.Value;

    /// <summary>HKEY_CURRENT_USER de quem usa o PC (HKEY_USERS\SID quando o app foi elevado por outra conta).</summary>
    public static RegistryKey Registry
    {
        get
        {
            if (RedirectedSid is not { } sid) return Microsoft.Win32.Registry.CurrentUser;
            // A hive do usuário logado está sempre carregada em HKEY_USERS enquanto ele tem sessão.
            return _hive ??= Microsoft.Win32.Registry.Users.OpenSubKey(sid, writable: true)
                             ?? Microsoft.Win32.Registry.CurrentUser;
        }
    }

    /// <summary>%APPDATA% (Roaming) de quem usa o PC.</summary>
    public static string RoamingAppData => ProfileFolder(Environment.SpecialFolder.ApplicationData, @"AppData\Roaming");

    /// <summary>%LOCALAPPDATA% de quem usa o PC.</summary>
    public static string LocalAppData => ProfileFolder(Environment.SpecialFolder.LocalApplicationData, @"AppData\Local");

    private static string ProfileFolder(Environment.SpecialFolder own, string relative)
    {
        if (RedirectedSid is { } sid && ProfilePath(sid) is { } profile)
            return Path.Combine(profile, relative);
        return Environment.GetFolderPath(own);
    }

    private static string? ProfilePath(string sid)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
            return key?.GetValue("ProfileImagePath") is string path && Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static string? ResolveOtherUserSid()
    {
        try
        {
            var me = WindowsIdentity.GetCurrent().User;
            int session = Process.GetCurrentProcess().SessionId;

            foreach (var explorer in Process.GetProcessesByName("explorer"))
            {
                using (explorer)
                {
                    if (explorer.SessionId != session) continue;
                    var owner = OwnerOf(explorer);
                    if (owner is null) continue;
                    return me is not null && owner.Equals(me) ? null : owner.Value;
                }
            }
        }
        catch { /* sem como saber: segue na conta do processo, o comportamento de sempre */ }
        return null;
    }

    private static SecurityIdentifier? OwnerOf(Process process)
    {
        if (!OpenProcessToken(process.Handle, TokenQuery, out var token)) return null;
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User;
        }
        finally { CloseHandle(token); }
    }

    private const uint TokenQuery = 0x0008;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
