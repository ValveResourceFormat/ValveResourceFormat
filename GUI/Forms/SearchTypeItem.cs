namespace GUI.Forms
{
    public class SearchTypeItem
    {
        public string Name { get; private set; }
        public SearchType Type { get; private set; }

        public SearchTypeItem(string name, SearchType type)
        {
            Name = name;
            Type = type;
        }
    }
}
