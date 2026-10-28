using System.Collections.Generic;

namespace _4PDA.Models
{
    public sealed class ForumItem
    {
        public ForumItem()
        {
            Children = new List<ForumItem>();
        }

        public int Id { get; set; }
        public int ParentId { get; set; }
        public int Level { get; set; }
        public string Title { get; set; }
        public string LastTopicTitle { get; set; }
        public string LastPostInfo { get; set; }
        public List<ForumItem> Children { get; private set; }

        public bool HasChildren
        {
            get { return Children != null && Children.Count > 0; }
        }
    }
}
