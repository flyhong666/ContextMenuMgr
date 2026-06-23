namespace ContextMenuMgr.Contracts;

/// <summary>
/// Provides text helpers for static enhance-menu dictionary values.
/// </summary>
public static class EnhanceMenuTextSanitizer
{
    /// <summary>
    /// Removes Win32 menu accelerator ampersands from plain visible labels.
    /// </summary>
    public static string StripMenuAcceleratorAmpersands(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith('@'))
        {
            return value;
        }

        return value.Replace("&&", "\uF000", StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("\uF000", "&", StringComparison.Ordinal);
    }
}
