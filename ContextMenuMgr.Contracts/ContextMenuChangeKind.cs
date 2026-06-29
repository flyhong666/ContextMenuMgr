namespace ContextMenuMgr.Contracts;

/// <summary>
/// Defines the available context Menu Change Kind values.
/// </summary>
public enum ContextMenuChangeKind
{
    None = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
    Reappeared = 4,
    WpsOfficeAssociationHijack = 100,
    WpsOfficeIconHijack = 101,
    WpsOfficeShellNewInjection = 102
}
