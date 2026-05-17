using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ContextMenuMgr.Contracts;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Resolves the user that owns a named-pipe request without relying on the
/// service process HKCU/profile.
/// </summary>
public sealed class BackendUserContextResolver
{
    private readonly FileLogger _logger;

    public BackendUserContextResolver(FileLogger logger)
    {
        _logger = logger;
    }

    public BackendUserContext? TryResolveFromPipeClient(NamedPipeServerStream stream)
    {
        try
        {
            string? sid = null;
            string? userName = null;
            stream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                sid = identity.User?.Value;
                userName = identity.Name;
            });

            return string.IsNullOrWhiteSpace(sid)
                ? null
                : CreateContext(sid, userName ?? sid, sessionId: null);
        }
        catch (Exception ex)
        {
            _ = _logger.LogAsync(RuntimeLogLevel.Warning, $"Unable to resolve pipe client user context: {ex.Message}", CancellationToken.None);
            return null;
        }
    }

    public BackendUserContext? TryResolveInteractiveUserFallback()
    {
        if (TryResolveSession(unchecked((int)NativeMethods.WTSGetActiveConsoleSessionId()), out var context))
        {
            return context;
        }

        if (!NativeMethods.WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out var sessionInfoPtr, out var count))
        {
            return null;
        }

        try
        {
            var dataSize = Marshal.SizeOf<NativeMethods.WTS_SESSION_INFO>();
            BackendUserContext? connected = null;
            for (var index = 0; index < count; index++)
            {
                var current = IntPtr.Add(sessionInfoPtr, index * dataSize);
                var sessionInfo = Marshal.PtrToStructure<NativeMethods.WTS_SESSION_INFO>(current);
                if (!TryResolveSession(sessionInfo.SessionID, out var userContext))
                {
                    continue;
                }

                if (sessionInfo.State == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSActive)
                {
                    return userContext;
                }

                if (connected is null && sessionInfo.State == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSConnected)
                {
                    connected = userContext;
                }
            }

            return connected;
        }
        finally
        {
            NativeMethods.WTSFreeMemory(sessionInfoPtr);
        }
    }

    private bool TryResolveSession(int sessionId, out BackendUserContext? context)
    {
        context = null;
        if (sessionId < 0 || !NativeMethods.WTSQueryUserToken(sessionId, out var tokenHandle))
        {
            return false;
        }

        using var token = new SafeAccessTokenHandle(tokenHandle);
        try
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            var sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                return false;
            }

            context = CreateContext(sid, identity.Name, sessionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static BackendUserContext CreateContext(string sid, string userName, int? sessionId)
    {
        var profilePath = GetProfilePath(sid);
        if (string.IsNullOrWhiteSpace(profilePath) || !Directory.Exists(profilePath))
        {
            throw new InvalidOperationException($"Unable to resolve profile path for {sid}.");
        }

        return new BackendUserContext(
            sid,
            userName,
            profilePath,
            Path.Combine(profilePath, @"AppData\Local"),
            Path.Combine(profilePath, @"AppData\Roaming"),
            sessionId);
    }

    private static string GetProfilePath(string sid)
    {
        using var profileKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}", writable: false);
        return Environment.ExpandEnvironmentVariables(profileKey?.GetValue("ProfileImagePath")?.ToString() ?? string.Empty);
    }

    private sealed class SafeAccessTokenHandle : SafeHandle
    {
        public SafeAccessTokenHandle(IntPtr handle)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WTSEnumerateSessionsW(
            IntPtr hServer,
            int reserved,
            int version,
            out IntPtr ppSessionInfo,
            out int pCount);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WTS_SESSION_INFO
        {
            public int SessionID;
            public string pWinStationName;
            public WTS_CONNECTSTATE_CLASS State;
        }
    }
}
