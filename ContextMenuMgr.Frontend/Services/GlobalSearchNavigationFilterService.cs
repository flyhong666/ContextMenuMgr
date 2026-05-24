using ContextMenuMgr.Contracts;

namespace ContextMenuMgr.Frontend.Services;

public sealed class GlobalSearchNavigationFilterService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, PendingGlobalSearchFilter> _pendingByPageType = [];

    public event EventHandler<GlobalSearchFilterRequestedEventArgs>? FilterRequested;

    public void SetPendingFilter(Type targetPageType, string filterText, string? itemId = null)
    {
        SetPendingFilter(targetPageType, null, false, filterText, itemId);
    }

    public void RequestFilter(
        Type targetPageType,
        ContextMenuCategory? category,
        bool isWindows11,
        string filterText,
        string? itemId = null)
    {
        SetPendingFilter(targetPageType, category, isWindows11, filterText, itemId);

        FrontendDebugLog.Info(
            nameof(GlobalSearchNavigationFilterService),
            "GlobalSearchFilterRequested: "
            + $"TargetPageType={targetPageType.Name}, "
            + $"Category={category?.ToString() ?? "<null>"}, "
            + $"IsWindows11={isWindows11}, "
            + $"FilterText='{SanitizeLogText(filterText)}'.");

        FilterRequested?.Invoke(
            this,
            new GlobalSearchFilterRequestedEventArgs(
                targetPageType,
                category,
                isWindows11,
                filterText,
                itemId));
    }

    public string? ConsumePendingFilter(Type targetPageType)
    {
        return ConsumePendingRequest(targetPageType)?.FilterText;
    }

    public string? PeekPendingFilter(Type targetPageType)
    {
        lock (_gate)
        {
            return _pendingByPageType.TryGetValue(targetPageType, out var pending)
                ? pending.FilterText
                : null;
        }
    }

    public PendingGlobalSearchFilter? ConsumePendingRequest(Type targetPageType)
    {
        lock (_gate)
        {
            if (!_pendingByPageType.Remove(targetPageType, out var pending))
            {
                return null;
            }

            return pending;
        }
    }

    public PendingGlobalSearchFilter? ConsumePendingRequest(ContextMenuCategory category)
    {
        lock (_gate)
        {
            var key = _pendingByPageType
                .FirstOrDefault(pair => pair.Value.Category == category)
                .Key;

            if (key is null || !_pendingByPageType.Remove(key, out var pending))
            {
                return null;
            }

            return pending;
        }
    }

    public PendingGlobalSearchFilter? ConsumePendingWindows11Request()
    {
        lock (_gate)
        {
            var key = _pendingByPageType
                .FirstOrDefault(static pair => pair.Value.IsWindows11)
                .Key;

            if (key is null || !_pendingByPageType.Remove(key, out var pending))
            {
                return null;
            }

            return pending;
        }
    }

    private void SetPendingFilter(
        Type targetPageType,
        ContextMenuCategory? category,
        bool isWindows11,
        string filterText,
        string? itemId)
    {
        lock (_gate)
        {
            _pendingByPageType[targetPageType] = new PendingGlobalSearchFilter(
                targetPageType,
                category,
                isWindows11,
                filterText,
                itemId,
                DateTimeOffset.Now);
        }
    }

    private static string SanitizeLogText(string? text)
    {
        return (text ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

public sealed record PendingGlobalSearchFilter(
    Type TargetPageType,
    ContextMenuCategory? Category,
    bool IsWindows11,
    string FilterText,
    string? ItemId,
    DateTimeOffset CreatedAt);

public sealed class GlobalSearchFilterRequestedEventArgs : EventArgs
{
    public GlobalSearchFilterRequestedEventArgs(
        Type targetPageType,
        ContextMenuCategory? category,
        bool isWindows11,
        string filterText,
        string? itemId)
    {
        TargetPageType = targetPageType;
        Category = category;
        IsWindows11 = isWindows11;
        FilterText = filterText;
        ItemId = itemId;
    }

    public Type TargetPageType { get; }

    public ContextMenuCategory? Category { get; }

    public bool IsWindows11 { get; }

    public string FilterText { get; }

    public string? ItemId { get; }
}
