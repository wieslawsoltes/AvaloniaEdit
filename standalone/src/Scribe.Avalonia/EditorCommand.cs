using System.Windows.Input;
namespace Scribe.Avalonia;
public sealed class EditorCommand(Action execute,Func<bool>? canExecute=null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter)=>canExecute?.Invoke()??true;
    public void Execute(object? parameter) { if(CanExecute(parameter)) execute(); }
    public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
