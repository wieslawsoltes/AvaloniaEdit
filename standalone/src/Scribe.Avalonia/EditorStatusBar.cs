using Avalonia;
using Avalonia.Controls.Primitives;
namespace Scribe.Avalonia;
public class EditorStatusBar : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty=AvaloniaProperty.Register<EditorStatusBar,string>(nameof(Text),"");
    public string Text {get=>GetValue(TextProperty);set=>SetValue(TextProperty,value);}
}
