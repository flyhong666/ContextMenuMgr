using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

public sealed class PortablePackageTrustService
{
    private const string ZoneIdentifierStreamName = ":Zone.Identifier";
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly string[] PriorityFileNames =
    [
        "ContextMenuManagerPlus.exe",
        "ContextMenuManagerPlus.Service.exe",
        "ContextMenuManagerPlus.TrayHost.exe",
        "ContextMenuMgr.ProbeHost.exe",
        "createdump.exe"
    ];

    public Task<PortablePackageTrustScanReport> ScanPortableRuntimeFilesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ScanPortableRuntimeFiles(cancellationToken), cancellationToken);

    public async Task<PortablePackageTrustUnblockReport> UnblockPortableRuntimeFilesAsync(CancellationToken cancellationToken = default)
    {
        var scan = await ScanPortableRuntimeFilesAsync(cancellationToken);
        FrontendDebugLog.Operation(
            "PortablePackageTrustService",
            "PortableMotwUnblockStarted: "
            + $"PackageKind={RuntimePaths.PackageKind}, "
            + $"BaseDirectory={AppContext.BaseDirectory}, "
            + $"BlockedFiles={FormatFileNames(scan.BlockedFiles)}.");

        var unblocked = new List<string>();
        var failed = new List<PortablePackageTrustFailure>();
        foreach (var file in scan.BlockedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPathUnderAppBase(file.FullPath))
            {
                failed.Add(new PortablePackageTrustFailure(file.FullPath, "File is outside the app directory."));
                continue;
            }

            var streamPath = GetZoneIdentifierStreamPath(file.FullPath);
            if (DeleteFileW(streamPath))
            {
                unblocked.Add(file.FullPath);
                continue;
            }

            var error = Marshal.GetLastPInvokeError();
            failed.Add(new PortablePackageTrustFailure(file.FullPath, $"DeleteFileW failed with Win32 error {error}."));
        }

        var report = new PortablePackageTrustUnblockReport(
            scan.ScannedCount,
            scan.BlockedCount,
            unblocked.Count,
            failed);

        if (failed.Count == 0)
        {
            FrontendDebugLog.Operation(
                "PortablePackageTrustService",
                "PortableMotwUnblockSucceeded: "
                + $"PackageKind={RuntimePaths.PackageKind}, "
                + $"BaseDirectory={AppContext.BaseDirectory}, "
                + $"UnblockedFiles={FormatPaths(unblocked)}, "
                + $"ResultCode=OK.");
        }
        else
        {
            FrontendDebugLog.Warning(
                "PortablePackageTrustService",
                "PortableMotwUnblockFailed: "
                + $"PackageKind={RuntimePaths.PackageKind}, "
                + $"BaseDirectory={AppContext.BaseDirectory}, "
                + $"UnblockedFiles={FormatPaths(unblocked)}, "
                + $"FailedFiles={FormatFailures(failed)}, "
                + $"ResultCode=PARTIAL_FAILURE.");
        }

        return report;
    }

    private static PortablePackageTrustScanReport ScanPortableRuntimeFiles(CancellationToken cancellationToken)
    {
        FrontendDebugLog.Operation(
            "PortablePackageTrustService",
            "PortableMotwScanStarted: "
            + $"PackageKind={RuntimePaths.PackageKind}, "
            + $"BaseDirectory={AppContext.BaseDirectory}.");

        if (RuntimePaths.PackageKind != RuntimePackageKind.Portable)
        {
            return new PortablePackageTrustScanReport(0, []);
        }

        var candidates = EnumerateRuntimeFileCandidates(cancellationToken).ToList();
        var blocked = new List<PortablePackageTrustFile>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasZoneIdentifierStream(candidate.FullPath))
            {
                blocked.Add(candidate);
            }
        }

        if (blocked.Count > 0)
        {
            FrontendDebugLog.Warning(
                "PortablePackageTrustService",
                "PortableMotwDetected: "
                + $"PackageKind={RuntimePaths.PackageKind}, "
                + $"BaseDirectory={AppContext.BaseDirectory}, "
                + $"BlockedFiles={FormatFileNames(blocked)}, "
                + $"ResultCode=PORTABLE_RUNTIME_FILES_BLOCKED.");
        }

        return new PortablePackageTrustScanReport(candidates.Count, blocked);
    }

    private static IEnumerable<PortablePackageTrustFile> EnumerateRuntimeFileCandidates(CancellationToken cancellationToken)
    {
        var baseDirectory = GetNormalizedDirectory(AppContext.BaseDirectory);
        var dataDirectory = RuntimePaths.PackageKind == RuntimePackageKind.Portable
            ? GetNormalizedDirectory(RuntimePaths.DataDirectory)
            : string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFilesWithoutReparsePoints(baseDirectory, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(file);
            if (!IsPathUnderDirectory(fullPath, baseDirectory)
                || (!string.IsNullOrWhiteSpace(dataDirectory) && IsPathUnderDirectory(fullPath, dataDirectory))
                || !IsRuntimeCandidate(fullPath)
                || !seen.Add(fullPath))
            {
                continue;
            }

            yield return new PortablePackageTrustFile(fullPath, Path.GetRelativePath(baseDirectory, fullPath));
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string baseDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(baseDirectory))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(baseDirectory);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory).ToList();
            }
            catch
            {
                childDirectories = [];
            }

            foreach (var childDirectory in childDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsReparsePoint(childDirectory))
                {
                    pending.Push(childDirectory);
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory).ToList();
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsReparsePoint(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsRuntimeCandidate(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (PriorityFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasZoneIdentifierStream(string filePath)
    {
        var streamPath = GetZoneIdentifierStreamPath(filePath);
        using var handle = CreateFileW(
            streamPath,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        return !handle.IsInvalid;
    }

    private static bool IsPathUnderAppBase(string filePath)
        => IsPathUnderDirectory(Path.GetFullPath(filePath), GetNormalizedDirectory(AppContext.BaseDirectory));

    private static bool IsPathUnderDirectory(string fullPath, string directory)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var normalizedDirectory = GetNormalizedDirectory(directory);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNormalizedDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string GetZoneIdentifierStreamPath(string filePath)
        => Path.GetFullPath(filePath) + ZoneIdentifierStreamName;

    private static string FormatFileNames(IReadOnlyCollection<PortablePackageTrustFile> files)
        => string.Join(", ", files.Select(static file => file.RelativePath));

    private static string FormatPaths(IReadOnlyCollection<string> paths)
        => string.Join(", ", paths.Select(Path.GetFileName));

    private static string FormatFailures(IReadOnlyCollection<PortablePackageTrustFailure> failures)
        => string.Join(", ", failures.Select(static failure => $"{Path.GetFileName(failure.FilePath)}={failure.Error}"));

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string lpFileName);
}

public sealed record PortablePackageTrustFile(string FullPath, string RelativePath);

public sealed record PortablePackageTrustScanReport(
    int ScannedCount,
    IReadOnlyList<PortablePackageTrustFile> BlockedFiles)
{
    public int BlockedCount => BlockedFiles.Count;
}

public sealed record PortablePackageTrustFailure(string FilePath, string Error);

public sealed record PortablePackageTrustUnblockReport(
    int ScannedCount,
    int BlockedCount,
    int UnblockedCount,
    IReadOnlyList<PortablePackageTrustFailure> FailedFiles);
