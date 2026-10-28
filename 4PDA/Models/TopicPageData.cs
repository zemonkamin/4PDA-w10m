using System.Collections.Generic;

namespace _4PDA.Models
{
    public sealed class TopicPageData
    {
        public TopicPageData()
        {
            Posts = new List<TopicPostItem>();
            PageSize = 20;
            TotalPages = 1;
            CurrentPage = 1;
        }

        public int TopicId { get; set; }
        public int ForumId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public int StartOffset { get; set; }
        public int TargetPostId { get; set; }
        public int RequestedPostId { get; set; }
        public bool CanReply { get; set; }
        public bool IsReplyForbidden { get; set; }
        public string AuthKey { get; set; }
        public List<TopicPostItem> Posts { get; private set; }

        public bool HasMore
        {
            get { return CurrentPage < TotalPages; }
        }
    }
}
