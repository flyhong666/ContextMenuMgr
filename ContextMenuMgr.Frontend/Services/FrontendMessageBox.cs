using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace ContextMenuMgr.Frontend.Services;

/// <summary>
/// Represents the frontend Message Box.
/// </summary>
public static class FrontendMessageBox
{
    /// <summary>
    /// Shows info Async.
    /// </summary>
    public static async Task ShowInfoAsync(
        string message,
        string title,
        string? closeButtonText = null)
    {
        var localization = ResolveLocalization();
        var owner = System.Windows.Application.Current?.MainWindow;
        Keyboard.ClearFocus();
        var messageBox = new MessageBox
        {
            Title = title,
            Content = message,
            Owner = owner,
            CloseButtonText = closeButtonText ?? localization?.Translate("DialogClose") ?? "Close",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24)
        };

        await messageBox.ShowDialogAsync();
        await ClearDialogFocusAsync(owner);
    }

    /// <summary>
    /// Shows error Async.
    /// </summary>
    public static async Task ShowErrorAsync(
        string message,
        string title,
        string? closeButtonText = null)
    {
        var localization = ResolveLocalization();
        var owner = System.Windows.Application.Current?.MainWindow;
        Keyboard.ClearFocus();
        var messageBox = new MessageBox
        {
            Title = title,
            Content = message,
            Owner = owner,
            CloseButtonText = closeButtonText ?? localization?.Translate("DialogClose") ?? "Close",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24)
        };

        await messageBox.ShowDialogAsync();
        await ClearDialogFocusAsync(owner);
    }

    /// <summary>
    /// Shows confirm Async.
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        string message,
        string title,
        string? primaryButtonText = null,
        string? closeButtonText = null)
    {
        var localization = ResolveLocalization();
        var owner = System.Windows.Application.Current?.MainWindow;
        Keyboard.ClearFocus();
        var messageBox = new MessageBox
        {
            Title = title,
            Content = message,
            Owner = owner,
            PrimaryButtonText = primaryButtonText ?? localization?.Translate("DialogConfirm") ?? "Confirm",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Checkmark24),
            CloseButtonText = closeButtonText ?? localization?.Translate("DialogCancel") ?? "Cancel",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24)
        };

        var result = await messageBox.ShowDialogAsync() == MessageBoxResult.Primary;
        await ClearDialogFocusAsync(owner);
        return result;
    }

    private static async Task ClearDialogFocusAsync(System.Windows.Window? owner)
    {
        if (owner is null)
        {
            Keyboard.ClearFocus();
            return;
        }

        await owner.Dispatcher.InvokeAsync(
            () =>
            {
                Keyboard.ClearFocus();
                owner.Focus();
            },
            DispatcherPriority.ApplicationIdle);
    }

    private static LocalizationService? ResolveLocalization()
    {
        return System.Windows.Application.Current is App app
            ? app.TryGetService<LocalizationService>()
            : null;
    }
}
