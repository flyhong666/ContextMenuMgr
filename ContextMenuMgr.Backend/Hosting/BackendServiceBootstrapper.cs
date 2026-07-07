using System.Diagnostics;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceMarkedForDelete = 1072;
    private const int ErrorServiceExists = 1073;
    private const int ErrorAccessDenied = 5;
    private const int ScManagerConnect = 0x0001;
    private const int ServiceQueryStatus = 0x0004;
    private const int ServiceDelete = 0x00010000;
    private const string FrontendPolicyKeyPath = @"Software\ContextMenuMgr\Frontend";
    private const string FrontendPolicyValueName = "StartWithWindows";
    private static readonly string DataDirectory = RuntimePaths.DataDirectory;
    private static readonly string KeepFrontendOnStopMarkerPath = Path.Combine(
        DataDirectory,
        ServiceMetadata.KeepFrontendOnStopMarkerFileName);
    private static readonly string BootstrapLogPath = Path.Combine(RuntimePaths.LogsDirectory, "bootstrap.log");

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
                "force-remove-service" => ForceRemoveService(AddDetail),
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
        log($"InstallOrRepairService: ServiceExePath={serviceExePath}, BinaryPath={binaryPath}, StartupMode={startupMode}, UserSid={userSid ?? "<null>"}, IsAutostartEnabledForUser={isAutostartEnabled}, ServiceExistsScm={ServiceExistsInScm(ServiceMetadata.ServiceName)}, LegacyServiceExistsScm={ServiceExistsInScm(ServiceMetadata.LegacyServiceName)}.");

        var health = TestServiceRegistrationHealthy(ServiceMetadata.ServiceName);
        log($"InstallOrRepairServiceHealthCheck: ServiceName={ServiceMetadata.ServiceName}, Healthy={health.Healthy}, Reason={health.Reason}.");
        if (ServiceExistsInScm(ServiceMetadata.ServiceName) && !health.Healthy)
        {
            log($"InstallOrRepairService: Existing service unhealthy, Reason={health.Reason}. Removing before create.");
            var removal = RemoveServiceRegistrationTolerant(
                ServiceMetadata.ServiceName,
                keepFrontendAlive: true,
                log,
                health.Reason);
            if (!removal.Success)
            {
                return (false, removal.Code, removal.Detail);
            }
        }

        if (ServiceExistsInScm(ServiceMetadata.LegacyServiceName))
        {
            var legacyRemoval = RemoveServiceRegistrationTolerant(
                ServiceMetadata.LegacyServiceName,
                keepFrontendAlive: true,
                log,
                "LEGACY_SERVICE_CLEANUP");
            if (!legacyRemoval.Success)
            {
                return (false, legacyRemoval.Code, legacyRemoval.Detail);
            }
        }

        if (!ServiceExistsInScm(ServiceMetadata.ServiceName))
        {
            var createResult = TryRunSc(log,
                "create",
                ServiceMetadata.ServiceName,
                "binPath=",
                binaryPath,
                "start=",
                startupMode,
                "DisplayName=",
                ServiceMetadata.DisplayName);
            if (!createResult.Success)
            {
                if (createResult.ExitCode == ErrorServiceExists)
                {
                    var recovery = RecoverCreateServiceAlreadyExists(log);
                    if (!recovery.Success)
                    {
                        return (false, recovery.Code, recovery.Detail);
                    }

                    if (!ServiceExistsInScm(ServiceMetadata.ServiceName))
                    {
                        createResult = TryRunSc(log,
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
                        RunSc(log,
                            "config",
                            ServiceMetadata.ServiceName,
                            "binPath=",
                            binaryPath,
                            "start=",
                            startupMode);
                        createResult = new ScResult(true, 0, string.Empty, string.Empty, "Existing healthy service will be configured.");
                    }
                }
                else if (createResult.ExitCode == ErrorServiceMarkedForDelete)
                {
                    return (
                        false,
                        "SERVICE_PENDING_DELETE",
                        "Service is marked for deletion. Close Services MMC, Task Manager service tab, or any process holding the service handle, then retry; reboot if it remains pending.");
                }

                if (!createResult.Success)
                {
                    return (false, "SERVICE_CREATE_FAILED", createResult.Detail);
                }
            }
        }
        else
        {
            RunSc(log,
                "config",
                ServiceMetadata.ServiceName,
                "binPath=",
                binaryPath,
                "start=",
                startupMode);
        }

        health = TestServiceRegistrationHealthy(ServiceMetadata.ServiceName);
        if (!health.Healthy)
        {
            return (false, "SERVICE_REGISTRATION_INCOMPLETE", $"Service registration health check failed. Reason={health.Reason}.");
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
                        $"Service did not report Running within 15 seconds. InitialStatus={initialStatus}, FinalStatus={finalStatus}, ServiceName={ServiceMetadata.ServiceName}, ServiceExePath={serviceExePath}, Exception={ex.Message}. Check {RuntimePaths.LogsDirectory}\\bootstrap.log, backend.log, and service-startup.log.");
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
                $"Service is running but backend pipe did not become ready in 20 seconds. Status={finalStatus}. ServiceName={ServiceMetadata.ServiceName}. Check {RuntimePaths.LogsDirectory}\\bootstrap.log, backend.log, and service-startup.log.");
        }

        TryDeleteKeepFrontendMarker();
        return (true, "OK", "Running");
    }

    private static (bool Success, string Code, string Detail) UninstallService(Action<string> log)
    {
        var primary = RemoveServiceRegistrationTolerant(
            ServiceMetadata.ServiceName,
            keepFrontendAlive: true,
            log,
            "UNINSTALL_PRIMARY");
        var legacy = RemoveServiceRegistrationTolerant(
            ServiceMetadata.LegacyServiceName,
            keepFrontendAlive: true,
            log,
            "UNINSTALL_LEGACY");
        TryDeleteKeepFrontendMarker();

        if (primary.IsPendingDelete || legacy.IsPendingDelete)
        {
            var detail = JoinDetails(
                new[] { primary.Detail, legacy.Detail },
                "Close Services MMC, Task Manager service tab, or any process holding the service handle, then retry; reboot if it remains pending.");
            log($"UninstallServiceFinal: Code=SERVICE_PENDING_DELETE, Detail={detail}.");
            return (false, "SERVICE_PENDING_DELETE", detail);
        }

        if (!primary.Success)
        {
            log($"UninstallServiceFinal: Code={primary.Code}, Detail={primary.Detail}.");
            return (false, primary.Code, primary.Detail);
        }

        if (!legacy.Success)
        {
            log($"UninstallServiceFinal: Code={legacy.Code}, Detail={legacy.Detail}.");
            return (false, legacy.Code, legacy.Detail);
        }

        var code = primary.Code == "NOT_INSTALLED" && legacy.Code == "NOT_INSTALLED"
            ? "NOT_INSTALLED"
            : "UNINSTALLED";
        log($"UninstallServiceFinal: Code={code}, Primary={primary.Code}, Legacy={legacy.Code}.");
        return (true, code, code == "NOT_INSTALLED" ? "Service was not installed." : "Service removed.");
    }

    private static (bool Success, string Code, string Detail) ForceRemoveService(Action<string> log)
    {
        var primary = RemoveServiceRegistrationTolerant(
            ServiceMetadata.ServiceName,
            keepFrontendAlive: true,
            log,
            "FORCE_REMOVE_PRIMARY");
        var legacy = RemoveServiceRegistrationTolerant(
            ServiceMetadata.LegacyServiceName,
            keepFrontendAlive: true,
            log,
            "FORCE_REMOVE_LEGACY");
        TryDeleteKeepFrontendMarker();

        if (primary.IsPendingDelete || legacy.IsPendingDelete)
        {
            var detail = JoinDetails(
                new[] { primary.Detail, legacy.Detail },
                "Close Services MMC, Task Manager service tab, or any process holding the service handle, then retry; reboot if it remains pending.");
            log($"ForceRemoveServiceFinal: Code=SERVICE_PENDING_DELETE, Detail={detail}.");
            return (false, "SERVICE_PENDING_DELETE", detail);
        }

        if (!primary.Success)
        {
            log($"ForceRemoveServiceFinal: Code={primary.Code}, Detail={primary.Detail}.");
            return (false, primary.Code, primary.Detail);
        }

        if (!legacy.Success)
        {
            log($"ForceRemoveServiceFinal: Code={legacy.Code}, Detail={legacy.Detail}.");
            return (false, legacy.Code, legacy.Detail);
        }

        log($"ForceRemoveServiceFinal: Code=FORCE_REMOVED, Primary={primary.Code}, Legacy={legacy.Code}.");
        return (true, "FORCE_REMOVED", "Service registrations removed.");
    }

    private static (bool Success, string Code, string Detail) StopService(Action<string> log)
    {
        if (!ServiceExistsInScm(ServiceMetadata.ServiceName))
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
        if (!ServiceExistsInScm(ServiceMetadata.ServiceName))
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

    private static ServiceRemovalResult RemoveServiceRegistrationTolerant(
        string serviceName,
        bool keepFrontendAlive,
        Action<string> log,
        string reason)
    {
        if (!IsManagedServiceName(serviceName))
        {
            return new ServiceRemovalResult(false, "UNSUPPORTED_SERVICE_NAME", $"Refusing to remove unsupported service name '{serviceName}'.", false);
        }

        log($"RemoveServiceRegistrationTolerantStart: ServiceName={serviceName}, Reason={reason}, KeepFrontendAlive={keepFrontendAlive}.");
        var existsInScm = ServiceExistsInScm(serviceName);
        var existsInRegistry = ServiceExistsInRegistry(serviceName);
        log($"ServiceExistsScm: ServiceName={serviceName}, Exists={existsInScm}.");
        log($"ServiceExistsRegistry: ServiceName={serviceName}, Exists={existsInRegistry}.");

        var statusText = GetServiceStatusText(serviceName);
        log($"ServiceStatusBeforeStop: ServiceName={serviceName}, Status={statusText}.");
        if (existsInScm && !string.Equals(statusText, nameof(ServiceControllerStatus.Stopped), StringComparison.OrdinalIgnoreCase))
        {
            if (keepFrontendAlive)
            {
                TryEnsureKeepFrontendMarker(log);
            }

            try
            {
                using var service = new ServiceController(serviceName);
                service.Stop();
                log($"StopAttemptResult: ServiceName={serviceName}, Result=StopCalled.");
            }
            catch (Exception ex)
            {
                log($"StopAttemptResult: ServiceName={serviceName}, Result=Failure, StopFailureIgnoredForDelete=true, Exception={ex}.");
            }

            try
            {
                using var service = new ServiceController(serviceName);
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                log($"StopAttemptResult: ServiceName={serviceName}, Result=WaitStopped, FinalStatus={TryGetServiceControllerStatusText(service)}.");
            }
            catch (Exception ex)
            {
                log($"StopAttemptResult: ServiceName={serviceName}, Result=WaitFailure, StopFailureIgnoredForDelete=true, Exception={ex}.");
            }
        }

        var deleteResult = TryDeleteServiceRegistration(serviceName);
        log($"DeleteAttemptResult: ServiceName={serviceName}, Success={deleteResult.Success}, PendingDelete={deleteResult.PendingDelete}, NotInstalled={deleteResult.NotInstalled}, Fatal={deleteResult.Fatal}, ErrorCode={deleteResult.ErrorCode}, Detail={deleteResult.Detail}.");
        if (deleteResult.Fatal)
        {
            return new ServiceRemovalResult(false, deleteResult.Code, deleteResult.Detail, deleteResult.PendingDelete);
        }

        var wait = WaitForScmRemoval(serviceName, TimeSpan.FromSeconds(10), log);
        log($"WaitForScmRemoval result: ServiceName={serviceName}, Code={wait.Code}, Detail={wait.Detail}.");
        log($"RemoveServiceRegistrationTolerantFinal: ServiceName={serviceName}, Code={wait.Code}, Success={wait.Success}, PendingDelete={wait.IsPendingDelete}.");
        return wait;
    }

    private static ServiceRemovalResult RecoverCreateServiceAlreadyExists(Action<string> log)
    {
        log("CreateServiceAlreadyExistsRecovery: Result=Start.");
        var health = TestServiceRegistrationHealthy(ServiceMetadata.ServiceName);
        log($"CreateServiceAlreadyExistsRecovery: ServiceExistsScm={ServiceExistsInScm(ServiceMetadata.ServiceName)}, Healthy={health.Healthy}, Reason={health.Reason}.");
        if (health.Healthy)
        {
            return new ServiceRemovalResult(true, "SERVICE_EXISTS_HEALTHY", "Service already exists and appears healthy; continuing with config.", false);
        }

        return RemoveServiceRegistrationTolerant(
            ServiceMetadata.ServiceName,
            keepFrontendAlive: true,
            log,
            $"CREATE_1073_RECOVERY_{health.Reason}");
    }

    private static bool ServiceExistsInScm(string serviceName)
    {
        var query = TryOpenService(serviceName, ServiceQueryStatus);
        query.Handle?.Dispose();
        return query.Success;
    }

    private static bool ServiceExistsInRegistry(string serviceName)
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

    private static ServiceHealthResult TestServiceRegistrationHealthy(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key is null)
        {
            return new ServiceHealthResult(false, "SERVICE_REGISTRY_KEY_MISSING");
        }

        var imagePath = key.GetValue("ImagePath") as string;
        var start = key.GetValue("Start");
        var type = key.GetValue("Type");
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new ServiceHealthResult(false, "SERVICE_IMAGE_PATH_MISSING");
        }

        if (start is null)
        {
            return new ServiceHealthResult(false, "SERVICE_START_VALUE_MISSING");
        }

        if (type is null)
        {
            return new ServiceHealthResult(false, "SERVICE_TYPE_VALUE_MISSING");
        }

        var executablePath = TryParseExecutablePath(imagePath);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new ServiceHealthResult(false, $"SERVICE_IMAGE_EXECUTABLE_MISSING Path={executablePath ?? "<unparsed>"} ImagePath={imagePath}");
        }

        return new ServiceHealthResult(true, "OK");
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

    private static void TryEnsureKeepFrontendMarker(Action<string> log)
    {
        try
        {
            EnsureKeepFrontendMarker();
            log($"KeepFrontendMarker: Path={KeepFrontendOnStopMarkerPath}, Result=Created.");
        }
        catch (Exception ex)
        {
            log($"KeepFrontendMarker: Path={KeepFrontendOnStopMarkerPath}, Result=Failure, Exception={ex}.");
        }
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
        var result = TryRunSc(log, arguments);
        if (result.Success)
        {
            return;
        }

        throw new InvalidOperationException(result.Detail);
    }

    private static ScResult TryRunSc(Action<string> log, params string[] arguments)
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
            return new ScResult(false, -1, string.Empty, string.Empty, "Failed to start sc.exe.");
        }

        process.WaitForExit();
        stopwatch.Stop();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        log($"RunSc: Arguments={string.Join(" ", arguments)}, ExitCode={process.ExitCode}, ElapsedMs={stopwatch.ElapsedMilliseconds}, Stdout={(process.ExitCode == 0 ? "<suppressed-success>" : stdout)}, Stderr={(process.ExitCode == 0 ? "<suppressed-success>" : stderr)}.");
        if (process.ExitCode == 0)
        {
            return new ScResult(true, process.ExitCode, stdout, stderr, "OK");
        }

        var detail = stderr;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = stdout;
        }

        var trimmedDetail = string.IsNullOrWhiteSpace(detail)
            ? $"sc.exe exited with code {process.ExitCode}."
            : detail.Trim();
        return new ScResult(false, process.ExitCode, stdout, stderr, trimmedDetail);
    }

    private static DeleteServiceResult TryDeleteServiceRegistration(string serviceName)
    {
        var query = TryOpenService(serviceName, ServiceDelete);
        using var serviceHandle = query.Handle;
        if (!query.Success)
        {
            return query.ErrorCode switch
            {
                ErrorServiceDoesNotExist => new DeleteServiceResult(true, "NOT_INSTALLED", true, false, false, query.ErrorCode, "Service was not installed."),
                ErrorServiceMarkedForDelete => new DeleteServiceResult(true, "SERVICE_PENDING_DELETE", false, true, false, query.ErrorCode, "Service is already marked for deletion."),
                ErrorAccessDenied => new DeleteServiceResult(false, "SERVICE_DELETE_ACCESS_DENIED", false, false, true, query.ErrorCode, query.Detail),
                _ => new DeleteServiceResult(false, "SERVICE_DELETE_FAILED", false, false, true, query.ErrorCode, query.Detail)
            };
        }

        if (DeleteService(serviceHandle!.DangerousGetHandle()))
        {
            return new DeleteServiceResult(true, "SERVICE_DELETE_REQUESTED", false, false, false, 0, "DeleteService succeeded.");
        }

        var error = Marshal.GetLastWin32Error();
        return error switch
        {
            ErrorServiceDoesNotExist => new DeleteServiceResult(true, "NOT_INSTALLED", true, false, false, error, "Service was not installed."),
            ErrorServiceMarkedForDelete => new DeleteServiceResult(true, "SERVICE_PENDING_DELETE", false, true, false, error, "Service is already marked for deletion."),
            ErrorAccessDenied => new DeleteServiceResult(false, "SERVICE_DELETE_ACCESS_DENIED", false, false, true, error, new Win32Exception(error).Message),
            _ => new DeleteServiceResult(false, "SERVICE_DELETE_FAILED", false, false, true, error, new Win32Exception(error).Message)
        };
    }

    private static ServiceRemovalResult WaitForScmRemoval(string serviceName, TimeSpan timeout, Action<string> log)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var query = TryOpenService(serviceName, ServiceQueryStatus);
            query.Handle?.Dispose();
            if (!query.Success)
            {
                if (query.ErrorCode == ErrorServiceDoesNotExist)
                {
                    return new ServiceRemovalResult(true, "REMOVED", "SCM no longer reports the service.", false);
                }

                if (query.ErrorCode == ErrorServiceMarkedForDelete)
                {
                    return new ServiceRemovalResult(false, "SERVICE_PENDING_DELETE", "Service is marked for deletion.", true);
                }
            }

            Thread.Sleep(300);
        }

        var deleteCheck = TryDeleteServiceRegistration(serviceName);
        log($"WaitForScmRemovalDeleteCheck: ServiceName={serviceName}, Code={deleteCheck.Code}, ErrorCode={deleteCheck.ErrorCode}, Detail={deleteCheck.Detail}.");
        if (deleteCheck.PendingDelete)
        {
            return new ServiceRemovalResult(false, "SERVICE_PENDING_DELETE", "Service deletion is pending. Close Services MMC, Task Manager service tab, or any process holding the service handle, then retry; reboot if it remains pending.", true);
        }

        return new ServiceRemovalResult(false, "SERVICE_STILL_PRESENT", "Service still exists in SCM after delete request.", false);
    }

    private static OpenServiceResult TryOpenService(string serviceName, int desiredAccess)
    {
        var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return new OpenServiceResult(false, null, error, $"OpenSCManager failed: {new Win32Exception(error).Message}");
        }

        using var scmHandle = new SafeScHandle(scm);
        var service = OpenService(scmHandle.DangerousGetHandle(), serviceName, desiredAccess);
        if (service == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return new OpenServiceResult(false, null, error, $"OpenService({serviceName}) failed: {new Win32Exception(error).Message}");
        }

        return new OpenServiceResult(true, new SafeScHandle(service), 0, "OK");
    }

    private static bool IsManagedServiceName(string serviceName)
        => string.Equals(serviceName, ServiceMetadata.ServiceName, StringComparison.Ordinal)
           || string.Equals(serviceName, ServiceMetadata.LegacyServiceName, StringComparison.Ordinal);

    private static string? TryParseExecutablePath(string imagePath)
    {
        var trimmed = imagePath.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed[1..endQuote] : null;
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return trimmed[..(exeIndex + 4)];
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, int desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr serviceControlManager, string serviceName, int desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

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

    private sealed record ServiceHealthResult(bool Healthy, string Reason);

    private sealed record ServiceRemovalResult(bool Success, string Code, string Detail, bool IsPendingDelete);

    private sealed record DeleteServiceResult(
        bool Success,
        string Code,
        bool NotInstalled,
        bool PendingDelete,
        bool Fatal,
        int ErrorCode,
        string Detail);

    private sealed record ScResult(bool Success, int ExitCode, string Stdout, string Stderr, string Detail);

    private sealed record OpenServiceResult(bool Success, SafeScHandle? Handle, int ErrorCode, string Detail);

    private sealed class SafeScHandle : SafeHandle
    {
        public SafeScHandle(IntPtr handle)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
}
