using System.Runtime.InteropServices;
using System.Text;

namespace ContextMenuMgr.TrayHost;

internal static class AppUserModelIdShortcutRegistrar
{
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    public static bool TryEnsureShortcut(string baseDirectory, out string shortcutPath, out string? errorMessage)
    {
        shortcutPath = ResolveShortcutPath();
        errorMessage = null;

        try
        {
            var targetPath = ResolveTargetPath(baseDirectory);
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                errorMessage = $"Target executable was not found: {targetPath ?? "<null>"}";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            CreateOrUpdateShortcut(shortcutPath, targetPath, baseDirectory);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string ResolveShortcutPath()
    {
        var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return Path.Combine(startMenuPath, "Programs", AppIdentity.ShortcutFileName);
    }

    private static string? ResolveTargetPath(string baseDirectory)
    {
        var frontendPath = Path.Combine(baseDirectory, AppIdentity.FrontendExecutableName);
        if (File.Exists(frontendPath))
        {
            return frontendPath;
        }

        return Environment.ProcessPath;
    }

    private static void CreateOrUpdateShortcut(string shortcutPath, string targetPath, string baseDirectory)
    {
        object? shellLinkObject = Activator.CreateInstance(
            Type.GetTypeFromCLSID(ShellLinkClsid, throwOnError: true)!);

        if (shellLinkObject is null)
        {
            throw new InvalidOperationException("Failed to create the ShellLink COM object.");
        }

        try
        {
            var shellLink = (IShellLinkW)shellLinkObject;
            Marshal.ThrowExceptionForHR(shellLink.SetPath(targetPath));
            Marshal.ThrowExceptionForHR(shellLink.SetWorkingDirectory(baseDirectory));
            Marshal.ThrowExceptionForHR(shellLink.SetDescription(AppIdentity.AppDisplayName));

            var iconPath = Path.Combine(baseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                Marshal.ThrowExceptionForHR(shellLink.SetIconLocation(iconPath, 0));
            }

            var propertyStore = (IPropertyStore)shellLinkObject;
            var appUserModelIdKey = AppUserModelIdKey;
            var appUserModelId = new PropVariant(AppIdentity.AppUserModelId);
            try
            {
                Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref appUserModelIdKey, ref appUserModelId));
                Marshal.ThrowExceptionForHR(propertyStore.Commit());
            }
            finally
            {
                appUserModelId.Dispose();
            }

            var persistFile = (IPersistFile)shellLinkObject;
            Marshal.ThrowExceptionForHR(persistFile.Save(shortcutPath, true));
        }
        finally
        {
            if (Marshal.IsComObject(shellLinkObject))
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);

        [PreserveSig]
        int GetIDList(out IntPtr ppidl);

        [PreserveSig]
        int SetIDList(IntPtr pidl);

        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        [PreserveSig]
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        [PreserveSig]
        int GetHotkey(out short pwHotkey);

        [PreserveSig]
        int SetHotkey(short wHotkey);

        [PreserveSig]
        int GetShowCmd(out int piShowCmd);

        [PreserveSig]
        int SetShowCmd(int iShowCmd);

        [PreserveSig]
        int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        [PreserveSig]
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        [PreserveSig]
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        [PreserveSig]
        int Resolve(IntPtr hwnd, uint fFlags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        [PreserveSig]
        int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        [PreserveSig]
        int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        [PreserveSig]
        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        [PreserveSig]
        int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000138-0000-0000-C000-000000000046")]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PropertyKey pkey);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant pv);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant pv);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant : IDisposable
    {
        private const ushort VtLpwstr = 31;

        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _pointerValue;

        public PropVariant(string value)
        {
            _valueType = VtLpwstr;
            _reserved1 = 0;
            _reserved2 = 0;
            _reserved3 = 0;
            _pointerValue = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            if (_pointerValue == IntPtr.Zero)
            {
                return;
            }

            PropVariantClear(ref this);
            _pointerValue = IntPtr.Zero;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}
