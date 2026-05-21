using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Backend.Services;

/// <summary>
/// Represents the registry Backup Service.
/// </summary>
public sealed class RegistryBackupService
{
    private readonly string _backupDirectory;
    private readonly FileLogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryBackupService"/> class.
    /// </summary>
    public RegistryBackupService(string backupDirectory, FileLogger? logger = null)
    {
        _backupDirectory = backupDirectory;
        _logger = logger;
        Directory.CreateDirectory(_backupDirectory);
    }

    /// <summary>
    /// Executes export Key Async.
    /// </summary>
    public async Task<string> ExportKeyAsync(string registryPath, CancellationToken cancellationToken)
    {
        var backupPath = Path.Combine(_backupDirectory, $"{GetSafeFileName(registryPath)}.reg");
        await RunRegAsync(
            action: "RegistryBackupExport",
            arguments: $"export \"{registryPath}\" \"{backupPath}\" /y",
            registryPath: registryPath,
            backupPath: backupPath,
            cancellationToken: cancellationToken);
        return backupPath;
    }

    /// <summary>
    /// Executes restore Backup Async.
    /// </summary>
    public async Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        await RunRegAsync(
            action: "RegistryBackupImport",
            arguments: $"import \"{backupFilePath}\"",
            registryPath: null,
            backupPath: backupFilePath,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes backup File.
    /// </summary>
    public void DeleteBackupFile(string? backupFilePath)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath))
        {
            return;
        }

        var existsBefore = File.Exists(backupFilePath);
        if (existsBefore)
        {
            File.Delete(backupFilePath);
        }

        _logger?.LogFireAndForget($"RegistryBackupDelete: BackupFilePath={backupFilePath}, ExistsBefore={existsBefore}, Result={(existsBefore ? "Deleted" : "Skipped")}.");
    }

    private async Task RunRegAsync(
        string action,
        string arguments,
        string? registryPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start reg.exe.");

        await process.WaitForExitAsync(cancellationToken);
        stopwatch.Stop();

        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);

        await (_logger?.LogAsync(
            process.ExitCode == 0 ? RuntimeLogLevel.Information : RuntimeLogLevel.Warning,
            $"{action}: RegistryPath={DiagnosticLogFormatter.FormatRegistryPath(registryPath)}, BackupPath={backupPath}, RegExeArguments={arguments}, ExitCode={process.ExitCode}, ElapsedMs={stopwatch.ElapsedMilliseconds}{(process.ExitCode == 0 ? string.Empty : $", Stdout={DiagnosticLogFormatter.FormatRegistryValueData(standardOutput)}, Stderr={DiagnosticLogFormatter.FormatRegistryValueData(standardError)}")}.",
            cancellationToken) ?? Task.CompletedTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
        }
    }

    private static string GetSafeFileName(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
