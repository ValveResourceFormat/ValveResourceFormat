namespace GUI.Forms
{
    public class SearchTypeItem
    {
        public string Name { get; private set; }
        public int Id { get; private set; }

        public SearchTypeItem(string name, int id)
        {
            Name = name;
            Id = id;
        }
    }
}