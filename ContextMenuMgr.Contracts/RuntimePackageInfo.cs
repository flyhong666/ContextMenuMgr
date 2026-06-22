namespace ContextMenuMgr.Contracts;

public sealed record RuntimePackageInfo(RuntimePackageKind PackageKind)
{
    public static RuntimePackageInfo Installer { get; } = new(RuntimePackageKind.Installer);
}
