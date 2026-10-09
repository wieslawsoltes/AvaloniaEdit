using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace UnoEdit;

/// <summary>A native keyboard gesture; modifiers match exactly, preserving AltGr.</summary>
public sealed class KeyGesture
{
    public KeyGesture(VirtualKey key, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) { Key = key; KeyModifiers = modifiers; }
    public VirtualKey Key { get; }
    public VirtualKeyModifiers KeyModifiers { get; }
    public bool Matches(KeyRoutedEventArgs e) => e != null && e.Key == Key && CurrentModifiers == KeyModifiers;
    internal static VirtualKeyModifiers CurrentModifiers
    {
        get
        {
            static bool Down(VirtualKey k) => (InputKeyboardSource.GetKeyStateForCurrentThread(k) & CoreVirtualKeyStates.Down) != 0;
            return (Down(VirtualKey.Control) ? VirtualKeyModifiers.Control : 0) |
                (Down(VirtualKey.Shift) ? VirtualKeyModifiers.Shift : 0) |
                (Down(VirtualKey.Menu) ? VirtualKeyModifiers.Menu : 0) |
                (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows) ? VirtualKeyModifiers.Windows : 0);
        }
    }
}

/// <summary>Routes editor commands through native parents, with explicit CanExecute.</summary>
public class RoutedCommand : ICommand
{
    private static WeakReference<UIElement> _focused;
    public string Name { get; }
    public KeyGesture Gesture { get; }
    public RoutedCommand(string name, KeyGesture keyGesture = null) { Name = name ?? throw new ArgumentNullException(nameof(name)); Gesture = keyGesture; }
    internal static void SetFocusedTarget(UIElement target) => _focused = new WeakReference<UIElement>(target);
    private static UIElement FocusedTarget => _focused != null && _focused.TryGetTarget(out var target) ? target : null;
    public event EventHandler CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private IEnumerable<(object Target, RoutedCommandBinding Binding)> Route(UIElement target)
    {
        for (DependencyObject node = target; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is IRoutedCommandBindable bindable)
                foreach (var binding in bindable.CommandBindings.ToArray())
                    if (binding?.Command == this) yield return (node, binding);
    }
    public bool CanExecute(object parameter, UIElement target)
    {
        var args = new CanExecuteRoutedEventArgs(this, parameter);
        foreach (var (node, binding) in Route(target))
        {
            binding.DoCanExecute(node, args);
            if (args.Handled || args.CanExecute) return args.CanExecute;
        }
        return false;
    }
    public void Execute(object parameter, UIElement target)
    {
        var args = new ExecutedRoutedEventArgs(this, parameter);
        foreach (var (node, binding) in Route(target))
            if (binding.DoExecuted(node, args) || args.Handled) return;
    }
    bool ICommand.CanExecute(object parameter) => CanExecute(parameter, FocusedTarget);
    void ICommand.Execute(object parameter) => Execute(parameter, FocusedTarget);
}

public interface IRoutedCommandBindable { IList<RoutedCommandBinding> CommandBindings { get; } }
public class RoutedCommandBinding
{
    public RoutedCommandBinding(RoutedCommand command, EventHandler<ExecutedRoutedEventArgs> executed = null, EventHandler<CanExecuteRoutedEventArgs> canExecute = null)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        if (executed != null) Executed += executed;
        if (canExecute != null) CanExecute += canExecute;
    }
    public RoutedCommand Command { get; }
    public event EventHandler<ExecutedRoutedEventArgs> Executed;
    public event EventHandler<CanExecuteRoutedEventArgs> CanExecute;
    internal bool DoCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (!e.Handled)
        {
            if (CanExecute != null) CanExecute(sender, e);
            else if (Executed != null) e.CanExecute = true;
            if (e.CanExecute) e.Handled = true;
        }
        return e.CanExecute;
    }
    internal bool DoExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var query = new CanExecuteRoutedEventArgs(e.Command, e.Parameter);
        if (!e.Handled && Executed != null && DoCanExecute(sender, query)) { Executed(sender, e); e.Handled = true; return true; }
        if (query.Handled) e.Handled = true;
        return false;
    }
}
public sealed class CanExecuteRoutedEventArgs : EventArgs
{
    public CanExecuteRoutedEventArgs(ICommand command, object parameter) { Command = command ?? throw new ArgumentNullException(nameof(command)); Parameter = parameter; }
    public ICommand Command { get; }
    public object Parameter { get; }
    public bool CanExecute { get; set; }
    public bool Handled { get; set; }
}
public sealed class ExecutedRoutedEventArgs : EventArgs
{
    public ExecutedRoutedEventArgs(ICommand command, object parameter) { Command = command ?? throw new ArgumentNullException(nameof(command)); Parameter = parameter; }
    public ICommand Command { get; }
    public object Parameter { get; }
    public bool Handled { get; set; }
}
public sealed class KeyBinding
{
    public RoutedCommand Command { get; set; }
    public KeyGesture Gesture { get; set; }
    public object CommandParameter { get; set; }
    public void TryHandle(KeyRoutedEventArgs e, UIElement target)
    {
        if (e.Handled || Gesture?.Matches(e) != true || Command?.CanExecute(CommandParameter, target) != true) return;
        Command.Execute(CommandParameter, target); e.Handled = true;
    }
}
