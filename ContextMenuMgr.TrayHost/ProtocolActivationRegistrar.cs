using Microsoft.Win32;

namespace ContextMenuMgr.TrayHost;

internal static class ProtocolActivationRegistrar
{
    public static bool TryRegister(string baseDirectory, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var frontendPath = Path.Combine(baseDirectory, AppIdentity.FrontendExecutableName);
            if (!File.Exists(frontendPath))
            {
                frontendPath = Environment.ProcessPath ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(frontendPath) || !File.Exists(frontendPath))
            {
                errorMessage = $"Frontend executable was not found: {frontendPath}";
                return false;
            }

            using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{AppIdentity.ProtocolScheme}");
            if (protocolKey is null)
            {
                errorMessage = "Failed to create the protocol registry key.";
                return false;
            }

            protocolKey.SetValue(null, $"URL:{AppIdentity.AppDisplayName}", RegistryValueKind.String);
            protocolKey.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            var iconPath = Path.Combine(baseDirectory, "Assets", "AppIcon.ico");
            using (var iconKey = protocolKey.CreateSubKey("DefaultIcon"))
            {
                iconKey?.SetValue(
                    null,
                    File.Exists(iconPath) ? $"\"{iconPath}\",0" : $"\"{frontendPath}\",0",
                    RegistryValueKind.String);
            }

            using (var commandKey = protocolKey.CreateSubKey(@"shell\open\command"))
            {
                commandKey?.SetValue(null, $"\"{frontendPath}\" \"%1\"", RegistryValueKind.String);
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
