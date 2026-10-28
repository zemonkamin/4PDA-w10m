namespace _4PDA.Models
{
    public sealed class NewsArticle
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string Date { get; set; }
        public string Author { get; set; }
        public string ImageUrl { get; set; }
        public string Html { get; set; }
        public string LoadWarning { get; set; }
        public int CommentsCount { get; set; }

        public string CommentsText
        {
            get { return CommentsCount > 0 ? CommentsCount.ToString() : string.Empty; }
        }
    }
}
