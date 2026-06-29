namespace ContextMenuMgr.Contracts;

public enum DocumentIconProvider
{
    MicrosoftOffice = 0,
    WpsOffice = 1
}

public enum ProtectedDocumentGroup
{
    Word,
    Excel,
    PowerPoint,
    Pdf,
    Image,
    Ebook
}

public sealed record ProtectedDocumentAssociation
{
    public string Extension { get; init; } = string.Empty;

    public ProtectedDocumentGroup Group { get; init; }

    public IReadOnlyList<string> MicrosoftProgIds { get; init; } = [];

    public IReadOnlyList<string> WpsProgIdPrefixes { get; init; } = [];

    public bool SupportsIconProviderToggle { get; init; }

    public string? CurrentOwner { get; init; }

    public string? CurrentProgId { get; init; }
}

public sealed record OfficeSuiteCoexistenceStatus
{
    public bool IsWpsInstalled { get; init; }

    public bool IsMicrosoftOfficeInstalled { get; init; }

    public bool IsCoexistenceActive { get; init; }

    public IReadOnlyList<string> WpsIconSourceCandidates { get; init; } = [];

    public IReadOnlyList<string> MicrosoftOfficeIconSourceCandidates { get; init; } = [];

    public IReadOnlyList<ProtectedDocumentAssociation> ProtectedAssociations { get; init; } = [];

    public DocumentIconProvider? CurrentDocumentIconProvider { get; init; }
}
