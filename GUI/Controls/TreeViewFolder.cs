namespace GUI.Controls
{
    public class TreeViewFolder
    {
        public string Text { get; }
        public int ItemCount { get; }

        public TreeViewFolder(string text, int itemCount)
        {
            Text = text;
            ItemCount = itemCount;
        }
    }
}
