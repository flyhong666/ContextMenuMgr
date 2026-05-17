namespace ContextMenuMgr.Contracts;

/// <summary>
/// Defines the available pipe Command values.
/// </summary>
public enum PipeCommand
{
    Ping,
    EnsureTrayHost,
    SubscribeNotifications,
    SubscribeTrayHost,
    GetSnapshot,
    GetSceneSnapshot,
    SetEnhanceMenuItemEnabled,
    SetDetailedEditRuleValue,
    AcknowledgeItemState,
    SetEnabled,
    SetShellAttribute,
    SetDisplayText,
    GetRegistryProtectionSetting,
    SetRegistryProtectionSetting,
    ApplyDecision,
    DeleteItem,
    UndoDelete,
    PurgeDeletedItem,
    GetSpecialMenuSnapshot,
    SetSpecialMenuItemEnabled,
    CreateSpecialMenuItem,
    UpdateSpecialMenuItem,
    DeleteSpecialMenuItem,
    UndoSpecialMenuItem,
    PurgeSpecialMenuItem,
    MoveSpecialMenuItem,
    RestoreSpecialMenuDefaults,
    SetShellNewOrderLock,
    SetWin11BlockedItem,
    RemoveWin11BlockedItem,
    GetWin11BlockedItems,
    GetWin11ContextMenuSnapshot,
    SetAutoStartEnabled,
    GetAutoStartEnabled,
    AnalyzeFileTypeContext,
    CreateSceneMenuItem,
    GetEnhanceMenuDefinitions,
    GetEnhanceMenuState,
    RequestShutdown,
    RestartExplorer
}
