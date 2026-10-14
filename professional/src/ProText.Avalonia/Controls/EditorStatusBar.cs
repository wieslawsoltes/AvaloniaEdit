using Avalonia;
using Avalonia.Controls.Primitives;
namespace ProText.Avalonia.Controls;
public class EditorStatusBar : TemplatedControl
{
    public static readonly StyledProperty<string> PositionTextProperty=AvaloniaProperty.Register<EditorStatusBar,string>(nameof(PositionText),"");
    public static readonly StyledProperty<string> DocumentTextProperty=AvaloniaProperty.Register<EditorStatusBar,string>(nameof(DocumentText),"");
    public string PositionText { get=>GetValue(PositionTextProperty); set=>SetValue(PositionTextProperty,value); }
    public string DocumentText { get=>GetValue(DocumentTextProperty); set=>SetValue(DocumentTextProperty,value); }
}
