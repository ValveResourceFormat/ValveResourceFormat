namespace GUI.Controls
{
    public class TreeViewFolder
    {
        public string Text { get; private set; }
        public int ItemCount { get; private set; }

        public TreeViewFolder(string text, int itemCount)
        {
            Text = text;
            ItemCount = itemCount;
        }
    }
}
