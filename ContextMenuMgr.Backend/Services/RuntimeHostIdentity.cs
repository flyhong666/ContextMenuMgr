namespace ContextMenuMgr.Backend.Services;

public sealed record RuntimeHostIdentity(
    bool IsTrusted,
    int SchemaVersion,
    string Kind,
    string? Fingerprint,
    string FingerprintPrefix,
    DateTimeOffset CreatedAtUtc,
    string? FailureReason)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentKind = "WindowsMachineGuidAndUserSidHash";

    public static RuntimeHostIdentity Untrusted(string reason) => new(
        IsTrusted: false,
        SchemaVersion: CurrentSchemaVersion,
        Kind: CurrentKind,
        Fingerprint: null,
        FingerprintPrefix: "untrusted",
        CreatedAtUtc: DateTimeOffset.UtcNow,
        FailureReason: reason);
}
