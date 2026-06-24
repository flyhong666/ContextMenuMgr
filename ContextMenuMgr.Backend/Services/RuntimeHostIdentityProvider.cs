using System.Security.Cryptography;
using System.Text;
using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Services;

public sealed class RuntimeHostIdentityProvider
{
    private const string HashPurpose = "ContextMenuMgr:HostIdentity:v1";
    private readonly FileLogger _logger;
    private readonly Lock _syncRoot = new();
    private RuntimeHostIdentity? _cachedIdentity;

    public RuntimeHostIdentityProvider(FileLogger logger)
    {
        _logger = logger;
    }

    public RuntimeHostIdentity GetCurrent()
    {
        lock (_syncRoot)
        {
            if (_cachedIdentity is { IsTrusted: true })
            {
                return _cachedIdentity;
            }

            _cachedIdentity = CreateIdentity();
            return _cachedIdentity;
        }
    }

    public bool IsCurrentFingerprint(string? fingerprint)
    {
        var current = GetCurrent();
        return current.IsTrusted
            && !string.IsNullOrWhiteSpace(fingerprint)
            && string.Equals(current.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private RuntimeHostIdentity CreateIdentity()
    {
        try
        {
            var machineGuid = ReadMachineGuid();
            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                return LogUntrusted("MachineGuidUnavailable");
            }

            var userContext = new BackendUserContextResolver(_logger).TryResolveInteractiveUserFallback();
            if (string.IsNullOrWhiteSpace(userContext?.Sid))
            {
                return LogUntrusted("FrontendUserSidUnavailable");
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{HashPurpose}|{machineGuid}|{userContext.Sid}"))).ToLowerInvariant();
            var identity = new RuntimeHostIdentity(
                IsTrusted: true,
                SchemaVersion: RuntimeHostIdentity.CurrentSchemaVersion,
                Kind: RuntimeHostIdentity.CurrentKind,
                Fingerprint: fingerprint,
                FingerprintPrefix: fingerprint[..Math.Min(12, fingerprint.Length)],
                CreatedAtUtc: DateTimeOffset.UtcNow,
                FailureReason: null);

            _logger.LogFireAndForget(
                $"PortableHostIdentityCheck: PackageKind={RuntimePaths.PackageKind}, CurrentFingerprintPrefix={identity.FingerprintPrefix}, Result=Trusted.");
            return identity;
        }
        catch (Exception ex)
        {
            _logger.LogFireAndForget(
                RuntimeLogLevel.Warning,
                $"PortableHostIdentityCheck: PackageKind={RuntimePaths.PackageKind}, Result=Untrusted, Reason={ex.GetType().Name}, Exception={ex.Message}.");
            return RuntimeHostIdentity.Untrusted(ex.GetType().Name);
        }
    }

    private RuntimeHostIdentity LogUntrusted(string reason)
    {
        _logger.LogFireAndForget(
            RuntimeLogLevel.Warning,
            $"PortableHostIdentityCheck: PackageKind={RuntimePaths.PackageKind}, CurrentFingerprintPrefix=untrusted, Result=Untrusted, Reason={reason}.");
        return RuntimeHostIdentity.Untrusted(reason);
    }

    private static string? ReadMachineGuid()
    {
        return ReadMachineGuid(RegistryView.Registry64)
            ?? ReadMachineGuid(RegistryView.Default);
    }

    private static string? ReadMachineGuid(RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
        return key?.GetValue("MachineGuid")?.ToString();
    }
}
