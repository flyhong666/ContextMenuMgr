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
    MoveSpecialMenuItem,
    RestoreSpecialMenuDefaults,
    SetShellNewOrderLock,
    AnalyzeFileTypeContext,
    CreateSceneMenuItem,
    GetEnhanceMenuDefinitions,
    GetEnhanceMenuState,
    RequestShutdown
}
