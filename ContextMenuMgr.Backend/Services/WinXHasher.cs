using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using System.Threading;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ContextMenuMgr.Backend.Services;

internal sealed record WinXHashResult(
    bool Success,
    uint? Hash,
    string? TargetPath,
    string? Arguments,
    string? GeneralizedTargetPath,
    string? Error,
    bool? VerificationSucceeded = null,
    uint? ReadBackHash = null,
    string? VerificationWarning = null);

internal static class WinXHasher
{
    private static readonly Dictionary<string, string> GeneralizePathMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["%ProgramFiles%"] = "{905e63b6-c1bf-494e-b29c-65b732d3d21a}",
        [@"%SystemRoot%\System32"] = "{1ac14e77-02e7-4e5d-b744-2eb1ae5198b7}",
        ["%SystemRoot%"] = "{f38bf404-1d43-42f2-9305-67de0b28fc23}"
    };

    public static void HashLnk(string lnkPath)
    {
        SHCreateItemFromParsingName(lnkPath, null, typeof(IShellItem2).GUID, out var item);
        var item2 = (IShellItem2)item;

        PSGetPropertyKeyFromName("System.Link.TargetParsingPath", out var propertyKey);
        string? targetPath;
        try { targetPath = item2.GetString(propertyKey); }
        catch { targetPath = null; }

        PSGetPropertyKeyFromName("System.Link.Arguments", out propertyKey);
        string? arguments;
        try { arguments = item2.GetString(propertyKey); }
        catch { arguments = null; }

        var blob = (GeneralizePath(targetPath) + arguments
            + "do not prehash links.  this should only be done by the user.").ToLowerInvariant();
        var input = Encoding.Unicode.GetBytes(blob);
        var output = new byte[input.Length];
        HashData(input, input.Length, output, output.Length);
        var hash = BitConverter.ToUInt32(output, 0);

        var storeId = typeof(IPropertyStore).GUID;
        var store = item2.GetPropertyStore(GPS.READWRITE, ref storeId);
        PSGetPropertyKeyFromName("System.Winx.Hash", out propertyKey);
        var propVariant = new PropVariant { VarType = VarEnum.VT_UI4, uintVal = hash };
        store.SetValue(ref propertyKey, ref propVariant);
        store.Commit();

        Marshal.ReleaseComObject(store);
        Marshal.ReleaseComObject(item);
    }

    public static WinXHashResult HashLnkWithResult(string lnkPath)
    {
        string? targetPath = null;
        string? arguments = null;
        string? generalizedTargetPath = null;
        uint? hash = null;

        IShellItem? item = null;
        var storeRef = default(IPropertyStore);

        try
        {
            SHCreateItemFromParsingName(lnkPath, null, typeof(IShellItem2).GUID, out item);
            var item2 = (IShellItem2)item;

            PSGetPropertyKeyFromName("System.Link.TargetParsingPath", out var propertyKey);
            try { targetPath = item2.GetString(propertyKey); }
            catch { targetPath = null; }

            PSGetPropertyKeyFromName("System.Link.Arguments", out propertyKey);
            try { arguments = item2.GetString(propertyKey); }
            catch { arguments = null; }

            generalizedTargetPath = GeneralizePath(targetPath);
            var blob = (generalizedTargetPath + arguments
                + "do not prehash links.  this should only be done by the user.").ToLowerInvariant();
            var input = Encoding.Unicode.GetBytes(blob);
            var output = new byte[input.Length];
            HashData(input, input.Length, output, output.Length);
            hash = BitConverter.ToUInt32(output, 0);

            var storeId = typeof(IPropertyStore).GUID;
            storeRef = item2.GetPropertyStore(GPS.READWRITE, ref storeId);
            PSGetPropertyKeyFromName("System.Winx.Hash", out propertyKey);
            var propVariant = new PropVariant { VarType = VarEnum.VT_UI4, uintVal = hash.Value };
            storeRef.SetValue(ref propertyKey, ref propVariant);
            storeRef.Commit();
        }
        catch (Exception ex)
        {
            return new WinXHashResult(false, hash, targetPath, arguments, generalizedTargetPath, ex.Message);
        }
        finally
        {
            if (storeRef is not null) Marshal.ReleaseComObject(storeRef);
            if (item is not null) Marshal.ReleaseComObject(item);
        }

        // Diagnostic readback verification — does not affect Success.
        if (TryReadWinXHashWithRetry(lnkPath, out var readHash, out var readError))
        {
            if (readHash == hash)
            {
                return new WinXHashResult(true, hash, targetPath, arguments, generalizedTargetPath, null,
                    VerificationSucceeded: true, ReadBackHash: readHash);
            }

            return new WinXHashResult(true, hash, targetPath, arguments, generalizedTargetPath, null,
                VerificationSucceeded: false, ReadBackHash: readHash,
                VerificationWarning: $"Hash mismatch: wrote {hash}, read {readHash}.");
        }

        return new WinXHashResult(true, hash, targetPath, arguments, generalizedTargetPath, null,
            VerificationSucceeded: false,
            VerificationWarning: $"Readback verification did not confirm written hash: {readError}");
    }

    public static bool TryReadWinXHash(string lnkPath, out uint hash, out string? error)
    {
        hash = 0;
        error = null;
        try
        {
            SHCreateItemFromParsingName(lnkPath, null, typeof(IShellItem2).GUID, out var item);
            var item2 = (IShellItem2)item;
            var storeId = typeof(IPropertyStore).GUID;
            var store = item2.GetPropertyStore(GPS.DEFAULT, ref storeId);
            PSGetPropertyKeyFromName("System.Winx.Hash", out var propertyKey);
            var pv = new PropVariant();
            try
            {
                store.GetValue(ref propertyKey, out pv);
                if (pv.VarType == VarEnum.VT_UI4)
                {
                    hash = (uint)pv.ulVal;
                }
                else
                {
                    error = $"System.Winx.Hash property found but type is {pv.VarType}, not VT_UI4.";
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
                Marshal.ReleaseComObject(item);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadWinXHashWithRetry(string lnkPath, out uint hash, out string? error)
    {
        hash = 0;
        error = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (TryReadWinXHash(lnkPath, out hash, out error))
            {
                return true;
            }

            if (!IsSharingViolation(error))
            {
                break;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static bool IsSharingViolation(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("0x80070020", StringComparison.OrdinalIgnoreCase)
               || error.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GeneralizePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        foreach (var pair in GeneralizePathMap)
        {
            var directory = Environment.ExpandEnvironmentVariables(pair.Key);
            if (filePath.StartsWith(directory + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Replace(directory, pair.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        return filePath;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int HashData(byte[] pbData, int cbData, byte[] pbHash, int cbHash);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern uint SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IBindCtx? pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem ppv);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PSGetPropertyKeyFromName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszCanonicalName,
        out PropertyKey propkey);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetCount(out uint cProps);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetAt(uint iProp, out PropertyKey pkey);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetValue(ref PropertyKey key, out PropVariant pv);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void SetValue(ref PropertyKey key, ref PropVariant pv);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void Commit();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out nint ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [SuppressUnmanagedCodeSecurity]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    private interface IShellItem2 : IShellItem
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object BindToHandler(IBindCtx pbc, [In] ref Guid bhid, [In] ref Guid riid);

        IShellItem GetParent();

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetDisplayName(uint sigdnName);

        uint GetAttributes(uint sfgaoMask);

        int Compare(IShellItem psi, uint hint);

        [return: MarshalAs(UnmanagedType.Interface)]
        IPropertyStore GetPropertyStore(GPS flags, [In] ref Guid riid);

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetPropertyStoreWithCreateObject(GPS flags, [MarshalAs(UnmanagedType.IUnknown)] object punkCreateObject, [In] ref Guid riid);

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetPropertyStoreForKeys(nint rgKeys, uint cKeys, GPS flags, [In] ref Guid riid);

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetPropertyDescriptionList(nint keyType, [In] ref Guid riid);

        void Update(IBindCtx pbc);

        [SecurityCritical]
        void GetProperty(nint key, [In][Out] PropVariant pv);

        Guid GetCLSID(nint key);

        FILETIME GetFileTime(nint key);

        int GetInt32(nint key);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetString(PropertyKey key);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid GUID;
        public int PID;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    private struct PropVariant
    {
        [FieldOffset(0)] public VarEnum VarType;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public uint uintVal;
        [FieldOffset(8)] public ulong ulVal;
    }

    [Flags]
    private enum GPS
    {
        DEFAULT = 0x00000000,
        READWRITE = 0x00000002
    }
}
