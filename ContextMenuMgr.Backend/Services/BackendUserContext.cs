using System.IO;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Describes the interactive user that initiated a backend pipe request.
/// </summary>
public sealed record BackendUserContext(
    string Sid,
    string UserName,
    string ProfilePath,
    string LocalAppDataPath,
    string RoamingAppDataPath,
    int? SessionId)
{
    public string GetWinXPath() => Path.Combine(LocalAppDataPath, @"Microsoft\Windows\WinX");

    public string GetSendToPath() => Path.Combine(RoamingAppDataPath, @"Microsoft\Windows\SendTo");
}
