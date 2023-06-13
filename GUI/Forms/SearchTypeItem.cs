namespace GUI.Forms
{
    class SearchTypeItem
    {
        public string Name { get; }
        public SearchType Type { get; }

        public SearchTypeItem(string name, SearchType type)
        {
            Name = name;
            Type = type;
        }
    }
}
