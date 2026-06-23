# CLAUDE.md

The main agent entry point for this repository is:

- `AGENTS.md`

Read and follow `AGENTS.md` before analyzing bugs, adding features, refactoring, or modifying code.

Minimum required reading:

1. `docs/ai-maintainer-playbook.md`
2. `docs/process-and-privilege-flows.md`
3. `docs/developer-guide.md`

Before changing code, identify which flow the task belongs to:

- Flow A: Frontend -> Backend Pipe -> Backend Service
- Flow B: Frontend -> UAC runas -> BackendServiceBootstrapper
- Flow C: Backend Service -> WTS User Token -> CreateProcessAsUser
- Flow D: Frontend -> ProbeHost -> Shell Extension COM

Do not mix privilege, user SID, session ID, `HKCU`, `HKEY_USERS`, Registry Write Protection, ShellNew ACL Lock, ProbeHost, or the UAC bootstrapper.

Tray icon, notification, and shell integration work must use Win32/PInvoke only. Do not introduce Windows App SDK, WinUI, AppNotificationManager, packaged activation, Microsoft.WindowsAppSDK, or versioned Windows SDK TFMs such as `net10.0-windows10.0.xxxxx.0`. TrayHost is the user-session notification agent, and hiding the tray icon must not remove the TrayHost process.

Most detailed documents under `docs/` are written in Simplified Chinese. Do not skip them because of language. Read and translate them internally if needed.

If file import is supported, treat the following as part of this file:

@AGENTS.md
