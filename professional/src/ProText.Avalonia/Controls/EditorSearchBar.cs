using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
namespace ProText.Avalonia.Controls;
/// <summary>A independently themeable, keyboard-accessible find and replace bar.</summary>
public class EditorSearchBar : TemplatedControl
{
    public static readonly StyledProperty<EditorSearchController?> ControllerProperty=AvaloniaProperty.Register<EditorSearchBar,EditorSearchController?>(nameof(Controller));
    public EditorSearchController? Controller { get=>GetValue(ControllerProperty); set=>SetValue(ControllerProperty,value); }
    private TextBox? _query;
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e); _query=e.NameScope.Find<TextBox>("PART_Query");
    }
    public void FocusQuery() { _query?.Focus(); _query?.SelectAll(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Enter) { Controller?.Navigate((e.KeyModifiers&KeyModifiers.Shift)!=0); e.Handled=true; }
        else if(e.Key==Key.Escape) { Controller?.CloseCommand.Execute(null); e.Handled=true; }
    }
}
