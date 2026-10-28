using System.Collections.Generic;

namespace _4PDA.Models
{
    public sealed class ForumTopicsData
    {
        public ForumTopicsData()
        {
            Items = new List<ForumTopicItem>();
            PageSize = 30;
            TotalPages = 1;
            CurrentPage = 1;
        }

        public int ForumId { get; set; }
        public string Title { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public List<ForumTopicItem> Items { get; private set; }

        public bool HasMore
        {
            get { return CurrentPage < TotalPages; }
        }
    }
}
