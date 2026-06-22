using System.Windows.Input;

namespace ContextMenuMgr.Frontend.Controls.Modern.Navigation;

internal sealed class ModernNavigationCommand(Action<object?> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
