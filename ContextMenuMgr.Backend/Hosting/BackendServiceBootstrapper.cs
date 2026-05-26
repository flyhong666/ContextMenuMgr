using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using ContextMenuMgr.Backend.Services;
using ContextMenuMgr.Contracts;
using Microsoft.Win32;

namespace ContextMenuMgr.Backend.Hosting;

/// <summary>
/// Performs elevated service install/repair operations for the frontend.
/// </summary>
internal static class BackendServiceBootstrapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string FrontendPolicyKeyPath = @"Software\ContextMenuMgr\Frontend";
    private const string FrontendPolicyValueName = "StartWithWindows";
    private static readonly string DataDirectory = RuntimePaths.DataDirectory;
    private static readonly string KeepFrontendOnStopMarkerPath = Path.Combine(
        DataDirectory,
        ServiceMetadata.KeepFrontendOnStopMarkerFileName);
    private static readonly string BootstrapLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextMenuMgr",
        "Logs",
        "bootstrap.log");

    /// <summary>
    /// Tries to execute an elevated backend bootstrap command.
    /// </summary>
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--service-bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var command = args.Length >= 2 ? args[1] : string.Empty;
        var resultFilePath = TryGetArgumentValue(args, "--result-file");
        if (string.IsNullOrWhiteSpace(resultFilePath))
        {
            Environment.ExitCode = 1;
            return true;
        }

        AppendBootstrapLog($"BootstrapStart: Command={command}, ResultFilePresent={!string.IsNullOrWhiteSpace(resultFilePath)}, UserSidPresent={!string.IsNullOrWhiteSpace(TryGetArgumentValue(args, "--user-sid"))}, EnabledArgument={TryGetArgumentValue(args, "--enabled")}, ArgsCount={args.Length}.");
        var result = Execute(command, args);
        AppendBootstrapLog($"BootstrapEnd: Command={command}, Success={result.Success}, Code={result.Code}, Detail={result.Detail}.");
        WriteResult(resultFilePath, result.Success, result.Code, result.Detail);
        Environment.ExitCode = result.Success ? 0 : 1;
        return true;
    }

    private static (bool Success, string Code, string Detail) Execute(string command, IReadOnlyList<string> args)
    {
        var details = new List<string>
        {
            $"Command={command}",
            $"ResultFilePresent={!string.IsNullOrWhiteSpace(TryGetArgumentValue(args, "--result-file"))}",
            $"UserSidPresent={!string.IsNullOrWhiteSpace(TryGetArgumentValue(args, "--user-sid"))}",
            $"EnabledArgument={TryGetArgumentValue(args, "--enabled") ?? "<null>"}",
            $"Identity={WindowsIdentity.GetCurrent().Name}",
            $"IsSystem={WindowsIdentity.GetCurrent().User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true}",
            $"IsAdmin={IsCurrentProcessAdmin()}"
        };
        void AddDetail(string detail)
        {
            details.Add(detail);
            AppendBootstrapLog(detail);
        }

        try
        {
            var userSidArgument = TryGetUserSidArgument(args);
            AddDetail($"UserSidArgument: Present={!string.IsNullOrWhiteSpace(userSidArgument.Sid)}, Valid={userSidArgument.IsValid}, Sid={userSidArgument.Sid ?? "<null>"}, Detail={userSidArgument.Detail ?? "<null>"}.");
            if (!userSidArgument.IsValid)
            {
                return (false, "INVALID_USER_SID", JoinDetails(details, userSidArgument.Detail ?? "Invalid --user-sid."));
            }

            var result = command.ToLowerInvariant() switch
            {
                "install-or-repair" => InstallOrRepairService(userSidArgument.Sid, AddDetail),
                "uninstall" => UninstallService(AddDetail),
                "stop" => StopService(AddDetail),
                "set-startup-mode" => SetServiceStartupMode(TryParseEnabledArgument(args), userSidArgument.Sid, AddDetail),
                "repair-runtime-data-acl" => RepairRuntimeDataAcl(AddDetail),
                _ => (false, "UNKNOWN_BOOTSTRAP_COMMAND", command)
            };
            return (result.Item1, result.Item2, JoinDetails(details, result.Item3));
        }
        catch (Exception ex)
        {
            var status = GetServiceStatusText(ServiceMetadata.ServiceName);
            AddDetail($"BootstrapException: Exception={ex}, Status={status}.");
            return (false, "SERVICE_BOOTSTRAP_ERROR", JoinDetails(details, $"{ex.Message} | Status={status}"));
        }
    }

    private static (bool Success, string Code, string Detail) InstallOrRepairService(string? userSid, Action<string> log)
    {
        var serviceExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(serviceExePath) || !File.Exists(serviceExePath))
        {
            return (false, "BACKEND_EXE_MISSING", string.Empty);
        }

        var binaryPath = $"\"{serviceExePath}\" --service";
        var isAutostartEnabled = IsAutostartEnabledForUser(userSid, log);
        var startupMode = isAutostartEnabled ? "auto" : "demand";
        log($"InstallOrRepairService: ServiceExePath={serviceExePath}, BinaryPath={binaryPath}, StartupMode={startupMode}, UserSid={userSid ?? "<null>"}, IsAutostartEnabledForUser={isAutostartEnabled}, ServiceExists={ServiceExists(ServiceMetadata.ServiceName)}, LegacyServiceExists={ServiceExists(ServiceMetadata.LegacyServiceName)}.");

        var health = TestServiceRegistrationHealthy(ServiceMetadata.ServiceName);
        log($"InstallOrRepairServiceHealthCheck: ServiceName={ServiceMetadata.ServiceName}, Healthy={health}.");
        if (ServiceExists(ServiceMetadata.ServiceName) && !health)
        {
            RemoveServiceRegistration(ServiceMetadata.ServiceName, keepFrontendAlive: true, log);
        }

        if (ServiceExists(ServiceMetadata.LegacyServiceName))
        {
            RemoveServiceRegistration(ServiceMetadata.LegacyServiceName, keepFrontendAlive: true, log);
        }

        if (!ServiceExists(ServiceMetadata.ServiceName))
        {
            RunSc(log,
                "create",
                ServiceMetadata.ServiceName,
                "binPath=",
                binaryPath,
                "start=",
                startupMode,
                "DisplayName=",
                ServiceMetadata.DisplayName);
        }
        else
        {
            using var existingService = new ServiceController(ServiceMetadata.ServiceName);
            if (existingService.Status != ServiceControllerStatus.Stopped)
            {
                EnsureKeepFrontendMarker();
                existingService.Stop();
                existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }

            RunSc(log,
                "config",
                ServiceMetadata.ServiceName,
                "binPath=",
                binaryPath,
                "start=",
                startupMode);
        }

        if (!TestServiceRegistrationHealthy(ServiceMetadata.ServiceName))
        {
            return (false, "SERVICE_REGISTRATION_INCOMPLETE", "Service registration health check failed.");
        }

        RunSc(log, "description", ServiceMetadata.ServiceName, "Context Menu Manager Plus elevated backend service");

        using (var service = new ServiceController(ServiceMetadata.ServiceName))
        {
            var initialStatus = service.Status;
            log($"ServiceStartCheck: ServiceName={ServiceMetadata.ServiceName}, InitialStatus={initialStatus}.");
            if (initialStatus != ServiceControllerStatus.Running)
            {
                log("ServiceStart: Ensuring keep-frontend marker before service start.");
                EnsureKeepFrontendMarker();
                try
                {
                    log("ServiceStart: Calling Start.");
                    service.Start();
                    log("ServiceStart: WaitForStatus Running started, Timeout=15s.");
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    service.Refresh();
                    log($"ServiceStart: WaitForStatus succeeded, Status={service.Status}.");
                }
                catch (System.ServiceProcess.TimeoutException ex)
                {
                    var finalStatus = TryGetServiceControllerStatusText(service);
                    log($"ServiceStart: WaitForStatus timeout/failure, FinalStatus={finalStatus}, Exception={ex}.");
                    if (!string.Equals(finalStatus, nameof(ServiceControllerStatus.Running), StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteKeepFrontendMarker();
                    }

                    return (
                        false,
                        "SERVICE_START_TIMEOUT",
                        $"Service did not report Running within 15 seconds. InitialStatus={initialStatus}, FinalStatus={finalStatus}, ServiceName={ServiceMetadata.ServiceName}, ServiceExePath={serviceExePath}, Exception={ex.Message}. Check %ProgramData%\\ContextMenuMgr\\Logs\\bootstrap.log, backend.log, and service-startup.log.");
                }
                catch (Exception ex)
                {
                    var finalStatus = TryGetServiceControllerStatusText(service);
                    log($"ServiceStart: WaitForStatus timeout/failure, FinalStatus={finalStatus}, Exception={ex}.");
                    if (!string.Equals(finalStatus, nameof(ServiceControllerStatus.Running), StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteKeepFrontendMarker();
                    }

                    throw;
                }
            }
            else
            {
                log($"ServiceStart: Already running, Status={initialStatus}.");
            }
        }

        var status = GetServiceStatusText(ServiceMetadata.ServiceName);
        if (!string.Equals(status, nameof(ServiceControllerStatus.Running), StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteKeepFrontendMarker();
            return (false, "SERVICE_NOT_RUNNING", status);
        }

        // Treat the service as healthy only after the backend pipe answers a
        // real Ping request. This prevents false-positive "install succeeded"
        // results when SCM reports Running but the runtime is still hung during
        // startup and not yet accepting pipe connections.
        if (!WaitForBackendPipeReady(TimeSpan.FromSeconds(20), log))
        {
            var finalStatus = GetServiceStatusText(ServiceMetadata.ServiceName);
            log($"BackendPipeReadyFailure: ServiceName={ServiceMetadata.ServiceName}, FinalStatus={finalStatus}.");
            if (!string.Equals(finalStatus, nameof(ServiceControllerStatus.Running), StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteKeepFrontendMarker();
            }

            return (
                false,
                "BACKEND_PIPE_NOT_READY",
                $"Service is running but backend pipe did not become ready in 20 seconds. Status={finalStatus}. ServiceName={ServiceMetadata.ServiceName}. Check %ProgramData%\\ContextMenuMgr\\Logs\\bootstrap.log, backend.log, and service-startup.log.");
        }

        TryDeleteKeepFrontendMarker();
        return (true, "OK", "Running");
    }

    private static (bool Success, string Code, string Detail) UninstallService(Action<string> log)
    {
        if (!ServiceExists(ServiceMetadata.ServiceName))
        {
            TryDeleteKeepFrontendMarker();
            return (true, "NOT_INSTALLED", "Service was not installed.");
        }

        RemoveServiceRegistration(ServiceMetadata.ServiceName, keepFrontendAlive: true, log);
        TryDeleteKeepFrontendMarker();
        return (true, "UNINSTALLED", "Service removed.");
    }

    private static (bool Success, string Code, string Detail) StopService(Action<string> log)
    {
        if (!ServiceExists(ServiceMetadata.ServiceName))
        {
            return (true, "NOT_INSTALLED", "Service was not installed.");
        }

        using var service = new ServiceController(ServiceMetadata.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return (true, "ALREADY_STOPPED", "Stopped");
        }

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        var status = GetServiceStatusText(ServiceMetadata.ServiceName);
        log($"StopService: ServiceName={ServiceMetadata.ServiceName}, Status={status}.");
        return string.Equals(status, nameof(ServiceControllerStatus.Stopped), StringComparison.OrdinalIgnoreCase)
            ? (true, "STOPPED", "Stopped")
            : (false, "SERVICE_NOT_STOPPED", status);
    }

    private static (bool Success, string Code, string Detail) SetServiceStartupMode(bool enabled, string? userSid, Action<string> log)
    {
        if (!ServiceExists(ServiceMetadata.ServiceName))
        {
            return (true, "NOT_INSTALLED", "Service was not installed.");
        }

        log($"SetServiceStartupMode: Enabled={enabled}, UserSid={userSid ?? "<null>"}.");
        RunSc(log,
            "config",
            ServiceMetadata.ServiceName,
            "start=",
            enabled ? "auto" : "demand");

        SetAutostartPolicyForUser(userSid, enabled, log);

        return (true, enabled ? "STARTUP_AUTO" : "STARTUP_MANUAL", enabled ? "Automatic" : "Manual");
    }

    private static (bool Success, string Code, string Detail) RepairRuntimeDataAcl(Action<string> log)
    {
        log($"RepairRuntimeDataAcl: Root={RuntimePaths.RootDirectory}, Result=Started.");
        var result = RuntimeDataAclRepairService.RepairRuntimeDataDirectory(RuntimePaths.RootDirectory);
        log($"RepairRuntimeDataAcl: Root={RuntimePaths.RootDirectory}, Success={result.Success}, Code={result.Code}, Detail={result.Detail}");
        return (result.Success, result.Code, result.Detail);
    }

    private static void RemoveServiceRegistration(string serviceName, bool keepFrontendAlive, Action<string> log)
    {
        if (ServiceExists(serviceName))
        {
            using var service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (keepFrontendAlive)
                {
                    EnsureKeepFrontendMarker();
                }

                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
        }

        RunSc(log, "delete", serviceName);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && ServiceExists(serviceName))
        {
            Thread.Sleep(300);
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TestServiceRegistrationHealthy(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key is null)
        {
            return false;
        }

        var imagePath = key.GetValue("ImagePath") as string;
        var start = key.GetValue("Start");
        var type = key.GetValue("Type");
        return !string.IsNullOrWhiteSpace(imagePath) && start is not null && type is not null;
    }

    private static bool IsAutostartEnabledForUser(string? userSid, Action<string> log)
    {
        RegistryKey? key = null;
        try
        {
            key = string.IsNullOrWhiteSpace(userSid)
                ? Registry.CurrentUser.OpenSubKey(FrontendPolicyKeyPath, writable: false)
                : Registry.Users.OpenSubKey($@"{userSid}\{FrontendPolicyKeyPath}", writable: false);

            var value = key?.GetValue(FrontendPolicyValueName);
            log($"IsAutostartEnabledForUser: Path={(string.IsNullOrWhiteSpace(userSid) ? $@"HKEY_CURRENT_USER\{FrontendPolicyKeyPath}" : $@"HKEY_USERS\{userSid}\{FrontendPolicyKeyPath}")}, ValueName={FrontendPolicyValueName}, RawValue={value ?? "<null>"}.");
            if (value is int intValue)
            {
                return intValue != 0;
            }

            if (value is string stringValue && int.TryParse(stringValue, out var parsed))
            {
                return parsed != 0;
            }

            return false;
        }
        finally
        {
            key?.Dispose();
        }
    }

    private static void SetAutostartPolicyForUser(string? userSid, bool enabled, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            using var key = Registry.CurrentUser.CreateSubKey(FrontendPolicyKeyPath, writable: true);
            key?.SetValue(FrontendPolicyValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            log($@"SetAutostartPolicyForUser: Path=HKEY_CURRENT_USER\{FrontendPolicyKeyPath}, ValueName={FrontendPolicyValueName}, ValueKind={RegistryValueKind.DWord}, ValueData={(enabled ? 1 : 0)}, Result=Success.");
            return;
        }

        var policyPath = $@"HKEY_USERS\{userSid}\{FrontendPolicyKeyPath}\{FrontendPolicyValueName}";
        try
        {
            using var userRoot = Registry.Users.OpenSubKey(userSid, writable: true)
                ?? throw new InvalidOperationException($"The registry hive for user {userSid} is not loaded.");

            using var key = userRoot.CreateSubKey(FrontendPolicyKeyPath, writable: true)
                ?? throw new InvalidOperationException(
                    $@"Unable to open frontend autostart policy key HKEY_USERS\{userSid}\{FrontendPolicyKeyPath}.");

            key.SetValue(FrontendPolicyValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            log($@"SetAutostartPolicyForUser: Path=HKEY_USERS\{userSid}\{FrontendPolicyKeyPath}, ValueName={FrontendPolicyValueName}, ValueKind={RegistryValueKind.DWord}, ValueData={(enabled ? 1 : 0)}, Result=Success.");
        }
        catch (Exception ex)
        {
            log($"SetAutostartPolicyForUser: Path={policyPath}, ValueData={(enabled ? 1 : 0)}, Result=Failure, Exception={ex}.");
            throw new InvalidOperationException($"Failed to write {policyPath}: {ex.Message}", ex);
        }
    }

    private static string GetServiceStatusText(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "Missing";
        }
    }

    private static string TryGetServiceControllerStatusText(ServiceController service)
    {
        try
        {
            service.Refresh();
            return service.Status.ToString();
        }
        catch (Exception ex)
        {
            return $"Unavailable({ex.GetType().Name}: {ex.Message})";
        }
    }

    private static void EnsureKeepFrontendMarker()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(KeepFrontendOnStopMarkerPath, "1");
    }

    private static void TryDeleteKeepFrontendMarker()
    {
        try
        {
            if (File.Exists(KeepFrontendOnStopMarkerPath))
            {
                File.Delete(KeepFrontendOnStopMarkerPath);
            }
        }
        catch
        {
        }
    }

    private static void RunSc(Action<string> log, params string[] arguments)
    {
        var stopwatch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start sc.exe.");
        }

        process.WaitForExit();
        stopwatch.Stop();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        log($"RunSc: Arguments={string.Join(" ", arguments)}, ExitCode={process.ExitCode}, ElapsedMs={stopwatch.ElapsedMilliseconds}, Stdout={(process.ExitCode == 0 ? "<suppressed-success>" : stdout)}, Stderr={(process.ExitCode == 0 ? "<suppressed-success>" : stderr)}.");
        if (process.ExitCode == 0)
        {
            return;
        }

        var detail = stderr;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = stdout;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"sc.exe exited with code {process.ExitCode}."
            : detail.Trim());
    }

    private static void WriteResult(string resultFilePath, bool success, string code, string detail)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultFilePath) ?? Path.GetTempPath());
        var payload = JsonSerializer.Serialize(new BootstrapResult(success, code, detail), JsonOptions);
        File.WriteAllText(resultFilePath, payload);
    }

    private static bool WaitForBackendPipeReady(TimeSpan timeout, Action<string> log)
    {
        var deadlineUtc = DateTime.UtcNow + timeout;
        var attempt = 0;
        log($"WaitForBackendPipeReadyStart: ServiceName={ServiceMetadata.ServiceName}, TimeoutMs={timeout.TotalMilliseconds}, Status={GetServiceStatusText(ServiceMetadata.ServiceName)}.");
        while (DateTime.UtcNow < deadlineUtc)
        {
            attempt++;
            if (TryPingBackendPipe(TimeSpan.FromSeconds(2), attempt, log))
            {
                log($"WaitForBackendPipeReadyEnd: Result=Success, Attempts={attempt}, Status={GetServiceStatusText(ServiceMetadata.ServiceName)}.");
                return true;
            }

            Thread.Sleep(300);
        }

        log($"WaitForBackendPipeReadyEnd: Result=Timeout, Attempts={attempt}, Status={GetServiceStatusText(ServiceMetadata.ServiceName)}.");
        return false;
    }

    private static bool TryPingBackendPipe(TimeSpan timeout, int attempt, Action<string> log)
    {
        try
        {
            log($"TryPingBackendPipe: Attempt={attempt}, ConnectTimeoutMs={timeout.TotalMilliseconds}, Result=Start.");
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeConstants.PipeName,
                PipeDirection.InOut,
                PipeOptions.None);
            pipe.Connect((int)timeout.TotalMilliseconds);

            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true
            };

            var payload = JsonSerializer.Serialize(new PipeEnvelope
            {
                MessageType = PipeMessageType.Request,
                CorrelationId = Guid.NewGuid(),
                Request = new PipeRequest
                {
                    Command = PipeCommand.Ping
                }
            }, JsonOptions);

            writer.WriteLine(payload);
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                log($"TryPingBackendPipe: Attempt={attempt}, Result=FailureEmptyResponse.");
                return false;
            }

            var envelope = JsonSerializer.Deserialize<PipeEnvelope>(line, JsonOptions);
            var success = envelope?.MessageType == PipeMessageType.Response
                   && envelope.Response?.Success == true;
            log($"TryPingBackendPipe: Attempt={attempt}, Result={(success ? "Success" : "FailureResponse")}.");
            return success;
        }
        catch (Exception ex)
        {
            log($"TryPingBackendPipe: Attempt={attempt}, Result=FailureException, ExceptionType={ex.GetType().FullName}, Message={ex.Message}.");
            return false;
        }
    }

    private static string? TryGetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static UserSidArgument TryGetUserSidArgument(IReadOnlyList<string> args)
    {
        var sid = TryGetArgumentValue(args, "--user-sid");
        if (string.IsNullOrWhiteSpace(sid))
        {
            return new UserSidArgument(true, null, null);
        }

        try
        {
            _ = new SecurityIdentifier(sid);
            return new UserSidArgument(true, sid, null);
        }
        catch (Exception ex)
        {
            return new UserSidArgument(false, null, $"Invalid --user-sid value '{sid}': {ex.Message}");
        }
    }

    private static bool TryParseEnabledArgument(IReadOnlyList<string> args)
    {
        var value = TryGetArgumentValue(args, "--enabled");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinDetails(IEnumerable<string> details, string finalDetail)
        => string.Join(" | ", details.Append(finalDetail).Where(static detail => !string.IsNullOrWhiteSpace(detail)));

    private static bool IsCurrentProcessAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void AppendBootstrapLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapLogPath) ?? Path.GetTempPath());
            File.AppendAllText(BootstrapLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed record BootstrapResult(bool Success, string Code, string Detail);

    private sealed record UserSidArgument(bool IsValid, string? Sid, string? Detail);
}
