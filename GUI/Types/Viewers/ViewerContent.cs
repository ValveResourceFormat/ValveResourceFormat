using System.Collections;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Types.Viewers;

// UI agnostic description of what a viewer wants to display
// the winforms rendering lives in GUI.Controls.ViewerContentPresenter
abstract record ViewerContent
{
    // Plain or syntax highlighted text
    public sealed record Text(string Content, HighlightLanguage Language = HighlightLanguage.KeyValues, IReadOnlyList<KvSourceSpan>? SourceMap = null) : ViewerContent;

    // Text that is produced on demand, rendering the exception text if producing it fails
    public sealed record LazyText(Func<string> GetContent, HighlightLanguage Language = HighlightLanguage.Default) : ViewerContent;

    // A table of objects, one row per item, one column per public property
    public sealed record Grid(IList Rows) : ViewerContent;

    // Multiple named tabs of content
    public sealed record Tabs(IReadOnlyList<ViewerTab> Items) : ViewerContent;
}

record ViewerTab(string Name, ViewerContent Content, bool Select = false);
