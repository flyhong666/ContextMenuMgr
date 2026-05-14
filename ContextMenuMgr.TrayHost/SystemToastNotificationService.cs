using System.Security;
using System.Security.Cryptography;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ContextMenuMgr.TrayHost;

internal sealed class SystemToastNotificationService : IDisposable
{
    private const string NotificationGroup = "PendingApprovals";
    private static readonly TimeSpan NotificationRetention = TimeSpan.FromDays(7);

    private readonly Action<string?> _activated;
    private readonly List<ToastNotification> _liveNotifications = [];
    private readonly object _syncRoot = new();
    private bool _disposed;

    public SystemToastNotificationService(Action<string?> activated)
    {
        _activated = activated;
    }

    public bool TryShowNotification(string title, string message, string? itemId)
    {
        if (_disposed || !OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return false;
        }

        if (!AppUserModelIdShortcutRegistrar.TryEnsureShortcut(AppContext.BaseDirectory, out _, out _))
        {
            return false;
        }

        ToastNotification? toast = null;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml($$"""
<toast launch="open-approvals">
  <visual>
    <binding template="ToastGeneric">
      <text>{{EscapeXml(title)}}</text>
      <text>{{EscapeXml(message)}}</text>
    </binding>
  </visual>
</toast>
""");

            toast = new ToastNotification(xml)
            {
                Group = NotificationGroup,
                Tag = CreateTag(itemId),
                ExpirationTime = DateTimeOffset.Now.Add(NotificationRetention)
            };

            toast.Activated += (_, _) =>
            {
                RemoveLiveNotification(toast);
                _activated(itemId);
            };
            toast.Dismissed += (_, _) => RemoveLiveNotification(toast);
            toast.Failed += (_, _) => RemoveLiveNotification(toast);

            AddLiveNotification(toast);
            ToastNotificationManager.CreateToastNotifier(AppIdentity.AppUserModelId).Show(toast);
            return true;
        }
        catch
        {
            if (toast is not null)
            {
                RemoveLiveNotification(toast);
            }

            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_syncRoot)
        {
            _liveNotifications.Clear();
        }
    }

    private void AddLiveNotification(ToastNotification toast)
    {
        lock (_syncRoot)
        {
            _liveNotifications.Add(toast);
        }
    }

    private void RemoveLiveNotification(ToastNotification toast)
    {
        lock (_syncRoot)
        {
            _liveNotifications.Remove(toast);
        }
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string CreateTag(string? itemId)
    {
        var source = string.IsNullOrWhiteSpace(itemId) ? Guid.NewGuid().ToString("N") : itemId;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return "approval-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
