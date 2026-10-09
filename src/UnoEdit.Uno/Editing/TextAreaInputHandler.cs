using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using UnoEdit.Utils;

namespace UnoEdit.Editing;

public interface ITextAreaInputHandler
{
    TextArea TextArea { get; }
    void Attach();
    void Detach();
}
public abstract class TextAreaStackedInputHandler : ITextAreaInputHandler
{
    protected TextAreaStackedInputHandler(TextArea textArea) => TextArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
    public TextArea TextArea { get; }
    public virtual void Attach() { }
    public virtual void Detach() { }
    public virtual void OnPreviewKeyDown(KeyRoutedEventArgs e) { }
    public virtual void OnPreviewKeyUp(KeyRoutedEventArgs e) { }
}
public class TextAreaInputHandler : ITextAreaInputHandler
{
    private readonly ObserveAddRemoveCollection<RoutedCommandBinding> _commands;
    private readonly ObserveAddRemoveCollection<ITextAreaInputHandler> _nested;
    public TextAreaInputHandler(TextArea textArea)
    {
        TextArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
        _commands = new ObserveAddRemoveCollection<RoutedCommandBinding>(x => { if (x == null) throw new ArgumentNullException(nameof(x)); if (IsAttached) TextArea.CommandBindings.Add(x); }, x => { if (IsAttached) TextArea.CommandBindings.Remove(x); });
        _nested = new ObserveAddRemoveCollection<ITextAreaInputHandler>(x => { Validate(x); if (IsAttached) x.Attach(); }, x => { if (IsAttached) x.Detach(); });
    }
    private void Validate(ITextAreaInputHandler value)
    {
        if (value == null || value.TextArea != TextArea || ReferenceEquals(value, this) || value is TextAreaInputHandler child && child.ContainsHandler(this)) throw new ArgumentException("Input handler must belong to the same text area and cannot contain itself.");
    }
    private bool ContainsHandler(ITextAreaInputHandler candidate) => _nested.Any(x => ReferenceEquals(x, candidate) || x is TextAreaInputHandler child && child.ContainsHandler(candidate));
    public TextArea TextArea { get; }
    public bool IsAttached { get; private set; }
    public ICollection<RoutedCommandBinding> CommandBindings => _commands;
    public ICollection<KeyBinding> KeyBindings { get; } = new List<KeyBinding>();
    public ICollection<ITextAreaInputHandler> NestedInputHandlers => _nested;
    public void AddBinding(RoutedCommand command, VirtualKeyModifiers modifiers, VirtualKey key, EventHandler<ExecutedRoutedEventArgs> handler)
    {
        CommandBindings.Add(new RoutedCommandBinding(command, handler));
        KeyBindings.Add(new KeyBinding { Command = command, Gesture = new KeyGesture(key, modifiers) });
    }
    public virtual void Attach()
    {
        if (IsAttached) throw new InvalidOperationException("Input handler is already attached.");
        IsAttached = true;
        foreach (var binding in _commands) TextArea.CommandBindings.Add(binding);
        try { foreach (var handler in _nested) handler.Attach(); }
        catch { Detach(); throw; }
    }
    public virtual void Detach()
    {
        if (!IsAttached) return;
        IsAttached = false;
        foreach (var handler in _nested.Reverse().ToArray()) handler.Detach();
        foreach (var binding in _commands) TextArea.CommandBindings.Remove(binding);
    }
    internal void HandleKey(KeyRoutedEventArgs e)
    {
        if (!IsAttached) return;
        foreach (var child in _nested.ToArray()) if (child is TextAreaInputHandler handler) handler.HandleKey(e);
        foreach (var binding in KeyBindings.ToArray()) binding.TryHandle(e, TextArea);
    }
}
