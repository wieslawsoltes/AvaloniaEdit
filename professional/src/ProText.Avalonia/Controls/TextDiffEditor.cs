using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ProText.Core;
namespace ProText.Avalonia.Controls;
/// <summary>Replaceable side-by-side text diff with bounded comparison and hunk navigation.</summary>
public class TextDiffEditor : TemplatedControl,IDisposable
{
    public static readonly StyledProperty<string> LeftTextProperty=AvaloniaProperty.Register<TextDiffEditor,string>(nameof(LeftText),"");
    public static readonly StyledProperty<string> RightTextProperty=AvaloniaProperty.Register<TextDiffEditor,string>(nameof(RightText),"");
    public static readonly StyledProperty<bool> IgnoreWhitespaceProperty=AvaloniaProperty.Register<TextDiffEditor,bool>(nameof(IgnoreWhitespace));
    public static readonly DirectProperty<TextDiffEditor,string> SummaryProperty=AvaloniaProperty.RegisterDirect<TextDiffEditor,string>(nameof(Summary),o=>o.Summary);
    public string LeftText { get=>GetValue(LeftTextProperty); set=>SetValue(LeftTextProperty,value); }
    public string RightText { get=>GetValue(RightTextProperty); set=>SetValue(RightTextProperty,value); }
    public bool IgnoreWhitespace { get=>GetValue(IgnoreWhitespaceProperty); set=>SetValue(IgnoreWhitespaceProperty,value); }
    private string _summary="No differences";
    public string Summary { get=>_summary; private set=>SetAndRaise(SummaryProperty,ref _summary,value); }
    public ProTextEditor LeftEditor { get; }=new() { IsReadOnly=true,ShowOverview=false,ShowStatusBar=false,EnableAutoPairs=false };
    public ProTextEditor RightEditor { get; }=new() { IsReadOnly=true,ShowOverview=false,ShowStatusBar=false,EnableAutoPairs=false };
    public DiffResult Result { get; private set; }=new([],[],false);
    public EditorCommand PreviousCommand { get; }
    public EditorCommand NextCommand { get; }
    private ContentPresenter? _leftHost,_rightHost;
    private int _hunk=-1;
    public TextDiffEditor() { PreviousCommand=new(()=>Navigate(true)); NextCommand=new(()=>Navigate()); }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if(_leftHost is not null) _leftHost.Content=null; if(_rightHost is not null) _rightHost.Content=null;
        base.OnApplyTemplate(e);
        _leftHost=e.NameScope.Find<ContentPresenter>("PART_LeftHost")??throw new InvalidOperationException("TextDiffEditor requires PART_LeftHost.");
        _rightHost=e.NameScope.Find<ContentPresenter>("PART_RightHost")??throw new InvalidOperationException("TextDiffEditor requires PART_RightHost.");
        _leftHost.Content=LeftEditor; _rightHost.Content=RightEditor; Refresh();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(LeftEditor is not null&&(e.Property==LeftTextProperty||e.Property==RightTextProperty||e.Property==IgnoreWhitespaceProperty)) Refresh();
    }
    public void Refresh()
    {
        Result=LineDiff.Compare(LeftText,RightText,IgnoreWhitespace);
        LeftEditor.Session!.Reset(LeftText); RightEditor.Session!.Reset(RightText); _hunk=-1;
        Summary=$"{Result.Hunks.Count} change groups"+(Result.IsCoarse?" • large-diff coarse mode":"");
        if(Result.Hunks.Count>0) Navigate();
    }
    public void Navigate(bool backwards=false)
    {
        if(Result.Hunks.Count==0) return;
        _hunk=(_hunk+(backwards?-1:1)+Result.Hunks.Count)%Result.Hunks.Count; var hunk=Result.Hunks[_hunk];
        SelectLines(LeftEditor,hunk.LeftStart,hunk.LeftCount); SelectLines(RightEditor,hunk.RightStart,hunk.RightCount);
        Summary=$"Change {_hunk+1} of {Result.Hunks.Count}"+(Result.IsCoarse?" • coarse mode":"");
    }
    private static void SelectLines(ProTextEditor editor,int first,int count)
    {
        var document=editor.Editor.Document; first=Math.Clamp(first,1,document.LineCount); var line=document.GetLineByNumber(first);
        int end=count==0?line.Offset:document.GetLineByNumber(Math.Min(document.LineCount,first+count-1)).EndOffset;
        editor.GoTo(first); editor.Reveal(new(line.Offset,end-line.Offset));
    }
    public void Dispose() { LeftEditor.Dispose(); RightEditor.Dispose(); LeftEditor.Session?.Dispose(); RightEditor.Session?.Dispose(); }
}
