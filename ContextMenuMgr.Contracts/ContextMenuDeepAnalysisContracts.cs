namespace ContextMenuMgr.Contracts;

public enum ContextMenuDeepAnalysisProbeMode
{
    SpecificHandler = 0,
    WholeContextMenu = 1
}

public sealed record ContextMenuDeepAnalysisRequest
{
    public Guid OperationId { get; init; }

    public string ItemId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public ContextMenuCategory Category { get; init; }

    public ContextMenuEntryKind EntryKind { get; init; }

    public string? HandlerClsid { get; init; }

    public string? HandlerFilePath { get; init; }

    public string? RegistryPath { get; init; }

    public string? BackendRegistryPath { get; init; }

    public string? CommandText { get; init; }

    public bool IncludeExtendedVerbs { get; init; }

    public string? SamplePath { get; init; }

    public ContextMenuDeepAnalysisProbeMode ProbeMode { get; init; } = ContextMenuDeepAnalysisProbeMode.SpecificHandler;
}

public sealed record ContextMenuDeepAnalysisResult
{
    public Guid OperationId { get; init; }

    public bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public string? DisplayName { get; init; }

    public string? HandlerClsid { get; init; }

    public string? HandlerFilePath { get; init; }

    public string? SamplePath { get; init; }

    public string? ProbeHostProcessArchitecture { get; init; }

    public string? OSArchitecture { get; init; }

    public bool Is64BitProcess { get; init; }

    public bool HandlerFileExists { get; init; }

    public string? HandlerFileMachineType { get; init; }

    public string? HandlerMachineType { get; init; }

    public string? HandlerMachineRawValue { get; init; }

    public string? SelectedProbeHostArchitecture { get; init; }

    public string? SelectedProbeHostPath { get; init; }

    public string? ActualProbeHostMachineType { get; init; }

    public string? ArchitectureSelectionReason { get; init; }

    public string? FrontendProcessArchitecture { get; init; }

    public string? ArchitectureCompatibility { get; init; }

    public string? DiagnosticDetails { get; init; }

    public ContextMenuDeepAnalysisProbeMode ProbeMode { get; init; }

    public bool IsSpecificHandlerResult { get; init; }

    public bool IsWholeContextMenuResult { get; init; }

    public bool SpecificHandlerFailedButWholeContextAvailable { get; init; }

    public string? SpecificHandlerFailureCode { get; init; }

    public string? SpecificHandlerFailureMessage { get; init; }

    public IReadOnlyList<ContextMenuDeepAnalysisMenuItem> Items { get; init; } = [];
}

public sealed record ContextMenuDeepAnalysisMenuItem
{
    public string? RawText { get; init; }

    public string? Text { get; init; }

    public string? CanonicalVerb { get; init; }

    public string? HelpText { get; init; }

    public int CommandOffset { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsSubmenu { get; init; }

    public IReadOnlyList<ContextMenuDeepAnalysisMenuItem> Children { get; init; } = [];
}
