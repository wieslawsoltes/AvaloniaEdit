using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
namespace ProText.Avalonia.Services;
internal static class EditorSyntax
{
    private static readonly Dictionary<(string,bool),IHighlightingDefinition?> Cache=[];
    public static IHighlightingDefinition? Get(string language,bool dark)
    {
        if(Cache.TryGetValue((language,dark),out var cached)) return cached;
        if(language=="Plain Text") return null;
        if(language is "XML" or "HTML" or "CSS") return HighlightingManager.Instance.GetDefinition(language);
        string comment=dark?"#809B7C":"#537641",keyword=dark?"#CFA4F8":"#7C3DA6",str=dark?"#AACD92":"#477827",number=dark?"#F4BD83":"#A55C22",type=dark?"#77CCD5":"#167785";
        string words=language=="Python"?"def class return if elif else for while in import from as async await try except finally with yield True False None and or not pass raise":language=="JSON"?"true false null":"using namespace public private protected internal static readonly sealed partial class record struct enum interface void var new return if else switch case default break continue for foreach while do in is as out ref try catch finally throw async await Task get set init true false null this base string int double bool object decimal long";
        string keywords=string.Join("",words.Split(' ').Select(w=>$"<Word>{w}</Word>"));
        string lineComment=language=="Python"?"#":"//";
        string xml=$"""
        <SyntaxDefinition name="ProText-{language}" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="{comment}"/><Color name="Keyword" foreground="{keyword}"/><Color name="String" foreground="{str}"/><Color name="Number" foreground="{number}"/><Color name="Type" foreground="{type}"/>
          <RuleSet>
            <Span color="Comment" begin="{lineComment}"/>
            <Span color="Comment" multiline="true" begin="/\*" end="\*/"/>
            <Span color="String" begin="&quot;" end="&quot;"><RuleSet><Span begin="\\" end="."/></RuleSet></Span>
            <Span color="String" begin="'" end="'"><RuleSet><Span begin="\\" end="."/></RuleSet></Span>
            <Keywords color="Keyword">{keywords}</Keywords>
            <Rule color="Number">\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
            <Rule color="Type">\b[A-Z][a-zA-Z0-9_]*\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;
        using var reader=XmlReader.Create(new StringReader(xml));
        var definition=HighlightingLoader.Load(reader,HighlightingManager.Instance); Cache[(language,dark)]=definition; return definition;
    }
}
